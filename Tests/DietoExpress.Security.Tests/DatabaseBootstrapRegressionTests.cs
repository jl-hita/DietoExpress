using System.Text.RegularExpressions;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DatabaseBootstrapRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    [Fact]
    public void Bootstrap_DollarQuotedPostgresBlocksHaveMatchingDelimiters()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));

        Assert.False(Regex.IsMatch(bootstrap, @"(?m)^\s*DO \$(?!\$)\s*$"),
            "Se encontró un bloque DO con delimitador de apertura incompleto.");
        Assert.False(Regex.IsMatch(bootstrap, @"(?m)^\s*END \$(?!\$);?\s*$"),
            "Se encontró un bloque DO con delimitador de cierre incompleto.");
    }

 
    [Fact]
    public void Bootstrap_RepairsStripeColumnsBeforeSaaSSeedUpdates()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        var methodStart = source.IndexOf("public static void UpgradeSaaSSchema(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "No se encontró la migración SaaS que inicializa el catálogo de planes.");
        var nextMethod = source.IndexOf("public static void UpgradeSaaSSchemaV2(", methodStart, StringComparison.Ordinal);
        Assert.True(nextMethod > methodStart, "No se pudo delimitar la migración SaaS base.");
        var bootstrap = source[methodStart..nextMethod];
        var billingRepair = bootstrap.IndexOf("ALTER TABLE subscription_plans ADD COLUMN IF NOT EXISTS stripe_additional_monthly_price_id", StringComparison.Ordinal);
        var seedUpdate = bootstrap.IndexOf("SET stripe_monthly_price_id = 'price_1UKdmV0RD4LdDkcU7ueOlu1B'", StringComparison.Ordinal);
        Assert.True(billingRepair >= 0, "UpgradeSaaSSchema debe reparar las columnas de Stripe antes de migrar datos.");
        Assert.True(seedUpdate > billingRepair, "Los UPDATE de Stripe no pueden ejecutarse antes de garantizar las columnas dentro de UpgradeSaaSSchema.");
        var billingModel = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Models", "BillingModelConfiguration.cs"));
        Assert.Contains("entity.Property(e => e.stripe_additional_monthly_price_id).HasMaxLength(255);", billingModel);
        Assert.Contains("entity.Property(e => e.stripe_additional_yearly_price_id).HasMaxLength(255);", billingModel);
    }

    [Fact]
    public void Bootstrap_InitializesBillingSchemaBeforeSubsequentSaaSMigrations()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        var baseSaas = bootstrap.IndexOf("UpgradeSaaSSchema(context, logger);", StringComparison.Ordinal);
        var billing = bootstrap.IndexOf("BillingSchemaBootstrap.Initialize(context, logger);", StringComparison.Ordinal);
        var nextSaas = bootstrap.IndexOf("UpgradeSaaSSchemaV2(context, logger);", StringComparison.Ordinal);
        Assert.True(baseSaas >= 0 && billing > baseSaas && nextSaas > billing,
            "El esquema base de billing debe inicializarse entre SaaS v1 y las migraciones SaaS posteriores.");
    }

    [Fact]
    public void Bootstrap_ScalarTableVerificationAliasesAggregateAsValue()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        var verifyStart = bootstrap.IndexOf("private static void VerifyCurrentSchema(", StringComparison.Ordinal);
        Assert.True(verifyStart >= 0, "No se encontró la verificación final del esquema.");
        var verification = bootstrap[verifyStart..];
        Assert.Contains("SELECT string_agg(required_table, ', ' ORDER BY required_table) AS \\"Value\\"", verification);
        Assert.Contains("SqlQueryRaw<string>(sql)", verification);
    }

    [Fact]
    public void Bootstrap_VerifiesEveryMappedEfColumn()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        Assert.Contains("context.Model.GetEntityTypes()", bootstrap);
        Assert.Contains("property.GetColumnName(storeObject)", bootstrap);
        Assert.Contains("information_schema.columns actual", bootstrap);
        Assert.Contains("Faltan columnas", bootstrap);
    }

    [Fact]
    public void Bootstrap_VerifiesEveryTableCreatedBySchemaBootstrappers()
    {
        var schemaFiles = new[]
        {
            "Anguloso.Server/Logica/DatabaseBootstrap.cs",
            "Anguloso.Server/Logica/BillingSchemaBootstrap.cs",
            "Anguloso.Server/Logica/LegalCommunicationsSchema.cs",
            "Anguloso.Server/Logica/SupportSchemaBootstrap.cs",
            "Anguloso.Server/Logica/PublicDirectoryVerificationSchema.cs"
        };

        var sources = schemaFiles
            .Select(path => File.ReadAllText(Path.Combine(RepoRoot, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
        var createdTables = sources
            .SelectMany(source => Regex.Matches(source, @"CREATE TABLE IF NOT EXISTS\s+([a-zA-Z_][a-zA-Z0-9_]*)",
                RegexOptions.IgnoreCase).Cast<Match>())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bootstrap = sources[0];
        var verifyMatch = Regex.Match(bootstrap,
            @"SELECT unnest\(ARRAY\[([\s\S]*?)\]\) AS required_table",
            RegexOptions.IgnoreCase);
        Assert.True(verifyMatch.Success, "No se encontró la lista de tablas verificadas por el bootstrap.");

        var verifiedTables = Regex.Matches(verifyMatch.Groups[1].Value, @"'([^']+)'")
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = createdTables.Except(verifiedTables).OrderBy(table => table).ToArray();
        Assert.True(missing.Length == 0,
            $"El contrato de verificación no incluye estas tablas: {string.Join(", ", missing)}");
    }
}
