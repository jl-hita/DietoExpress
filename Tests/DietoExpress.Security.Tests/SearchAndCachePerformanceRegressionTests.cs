using System.Text.RegularExpressions;
using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Xunit;

namespace DietoExpress.Security.Tests;

public class SearchAndCachePerformanceRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    private static string ReadServer(params string[] path) => File.ReadAllText(Path.Combine([RepoRoot, "Anguloso.Server", .. path]));

    // ---- Búsqueda: comodines escapados ----

    [Theory]
    [InlineData("pollo", "%pollo%")]
    [InlineData("  pollo  ", "%pollo%")]
    [InlineData("100%", "%100\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("a\\b", "%a\\\\b%")]
    [InlineData("%", "%\\%%")]
    public void SqlSearch_EscapesUserWildcards(string input, string expected)
    {
        Assert.Equal(expected, SqlSearch.Contains(input));
    }

    [Fact]
    public void Controllers_NeverBuildLikePatternsFromRawUserText()
    {
        // Un patrón %{texto}% sin escapar deja que el usuario introduzca comodines y evita los índices.
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers"), "*.cs")
                     .Concat(new[] { Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "OpenFoodFactsService.cs") }))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, "ILike\\([^;]*\\$\"%\\{"))
                offenders.Add(Path.GetFileName(file) + ": " + match.Value.Trim());
            foreach (Match match in Regex.Matches(text, "pattern = \\$\"%\\{"))
                offenders.Add(Path.GetFileName(file) + ": " + match.Value.Trim());
        }
        Assert.True(offenders.Count == 0, "Patrones ILIKE sin escapar: " + string.Join(" | ", offenders));
    }

    // ---- Índices ----

    [Fact]
    public void Schema_IndexesTheForeignKeysOfDietsAndRecipesAndTheSearchColumns()
    {
        var source = ReadServer("Logica", "DatabaseBootstrap.cs");
        Assert.Contains("UpgradePerformanceSchemaV1(context, logger);", source);
        foreach (var statement in new[]
        {
            "idx_meal_items_meal_id ON meal_items(meal_id)",
            "idx_meal_items_food_id ON meal_items(food_id)",
            "idx_recipe_items_recipe_id ON recipe_items(recipe_id)",
            "idx_recipe_items_food_id ON recipe_items(food_id)",
            "idx_foods_name_unaccent_trgm ON foods USING gin (immutable_unaccent(name) gin_trgm_ops)",
            "idx_clients_full_name_unaccent_trgm ON clients USING gin (immutable_unaccent(full_name) gin_trgm_ops)",
            "idx_clients_email_trgm ON clients USING gin (email gin_trgm_ops)",
            "idx_clients_phone_trgm ON clients USING gin (phone gin_trgm_ops)",
        })
            Assert.Contains(statement, source);
        Assert.Contains("LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT", source);
    }

    [Fact]
    public void FoodAndPatientSearch_UseTheIndexedUnaccentExpression()
    {
        var food = ReadServer("Logica", "OpenFoodFactsService.cs");
        Assert.Contains("EF.Functions.ILike(SqlFunctions.ImmutableUnaccent(f.name), SqlFunctions.ImmutableUnaccent(likePattern))", food);

        var clients = ReadServer("Controllers", "ClientsController.cs");
        Assert.Contains("EF.Functions.ILike(SqlFunctions.ImmutableUnaccent(c.full_name), SqlFunctions.ImmutableUnaccent(pattern))", clients);

        Assert.Contains("HasName(\"immutable_unaccent\")", ReadServer("Models", "BillingModelConfiguration.cs"));
    }

    // ---- Caché pública ----

    [Fact]
    public async Task Cache_ComputesOnceAndReusesTheValue()
    {
        using var cache = new PublicResponseCache();
        var calls = 0;
        Func<Task<string?>> factory = () => { Interlocked.Increment(ref calls); return Task.FromResult<string?>("valor"); };

        Assert.Equal("valor", await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), factory));
        Assert.Equal("valor", await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(1), factory));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cache_DoesNotStoreNullsOrFailures()
    {
        using var cache = new PublicResponseCache();
        var calls = 0;

        Assert.Null(await cache.GetOrCreateAsync<string>("ausente", TimeSpan.FromMinutes(1), () => { calls++; return Task.FromResult<string?>(null); }));
        Assert.Null(await cache.GetOrCreateAsync<string>("ausente", TimeSpan.FromMinutes(1), () => { calls++; return Task.FromResult<string?>(null); }));
        Assert.Equal(2, calls);

        var failures = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync<string>("falla", TimeSpan.FromMinutes(1), () => { failures++; throw new InvalidOperationException(); }));
        Assert.Equal("recuperado", await cache.GetOrCreateAsync("falla", TimeSpan.FromMinutes(1), () => Task.FromResult<string?>("recuperado")));
        Assert.Equal(1, failures);
    }

    [Fact]
    public async Task Cache_SharesOneComputationBetweenSimultaneousRequests()
    {
        using var cache = new PublicResponseCache();
        var calls = 0;
        var gate = new TaskCompletionSource();

        async Task<string?> Slow()
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return "listo";
        }

        var requests = Enumerable.Range(0, 10).Select(_ => cache.GetOrCreateAsync("simultanea", TimeSpan.FromMinutes(1), Slow)).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(requests);

        Assert.All(results, r => Assert.Equal("listo", r));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cache_ExpiresEntries()
    {
        using var cache = new PublicResponseCache();
        var calls = 0;
        Func<Task<string?>> factory = () => Task.FromResult<string?>("v" + Interlocked.Increment(ref calls));

        Assert.Equal("v1", await cache.GetOrCreateAsync("exp", TimeSpan.FromMilliseconds(30), factory));
        await Task.Delay(120);
        Assert.Equal("v2", await cache.GetOrCreateAsync("exp", TimeSpan.FromMilliseconds(30), factory));
    }

    [Fact]
    public async Task Cache_BoundsItsSizeSoVaryingFiltersCannotExhaustMemory()
    {
        using var cache = new PublicResponseCache();
        var calls = 0;
        // Más claves que el tope de entradas: la caché no debe crecer sin límite ni fallar.
        for (var i = 0; i < 1200; i++)
            await cache.GetOrCreateAsync("clave" + i, TimeSpan.FromMinutes(1), () => { calls++; return Task.FromResult<string?>("x"); });
        Assert.Equal(1200, calls);

        // Una clave demasiado larga no se cachea, pero la petición sigue funcionando.
        var longKey = new string('a', 600);
        Assert.Equal("x", await cache.GetOrCreateAsync(longKey, TimeSpan.FromMinutes(1), () => Task.FromResult<string?>("x")));
    }

    [Fact]
    public void PublicDirectoryEndpoints_AreCachedAndRateLimited()
    {
        var directory = ReadServer("Controllers", "DirectoryController.cs");
        Assert.Equal(2, Regex.Matches(directory, Regex.Escape("[EnableRateLimiting(\"public-read\")]")).Count);
        Assert.Contains("_publicCache.GetOrCreateAsync(key, PublicCacheTtl, () => SearchCoreAsync(filter))", directory);
        Assert.Contains("\"directory:profile:\" + normalized", directory);

        var sitemap = ReadServer("Controllers", "PublicSitemapController.cs");
        Assert.Contains("EnableRateLimiting(\"public-read\")", sitemap);
        Assert.Contains("TimeSpan.FromMinutes(10)", sitemap);

        var program = ReadServer("Program.cs");
        Assert.Contains("options.AddPolicy(\"public-read\"", program);
        Assert.Contains("AddSingleton<PublicResponseCache>()", program);
    }
}
