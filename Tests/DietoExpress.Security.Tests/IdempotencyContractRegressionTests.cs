using Xunit;

namespace DietoExpress.Security.Tests;

public class IdempotencyContractRegressionTests
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
    public void Idempotency_IsOptInAndOnlyProtectsDeclaredWriteOperations()
    {
        var middleware = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Middleware", "IdempotencyMiddleware.cs"));
        var biometrics = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "BiometricsController.cs"));
        var checkins = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "PatientCheckinsController.cs"));
        var portal = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "PatientPortalController.cs"));
        var plans = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "AdminPlansController.cs"));
        var recipes = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "RecipesController.cs"));

        Assert.Contains("GetMetadata<IdempotentAttribute>() is null", middleware);
        Assert.Contains("IsWriteMethod(request.Method)", middleware);
        Assert.Contains("[Idempotent]", biometrics);
        Assert.Contains("[Idempotent]", checkins);
        Assert.Contains("[Idempotent]", portal);
        Assert.Contains("[Idempotent]", plans);
        Assert.Contains("[Idempotent]", recipes);
        Assert.Contains("TryGetValue(\"Idempotency-Key\"", middleware);
        Assert.Contains("Idempotency-Key", middleware);
    }

    [Fact]
    public void Idempotency_UsesPersistentRecordsAndRejectsChangedPayloads()
    {
        var middleware = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Middleware", "IdempotencyMiddleware.cs"));
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));

        Assert.Contains("pg_advisory_xact_lock", middleware);
        Assert.Contains("FixedTimeEquals", middleware);
        Assert.Contains("StatusCodes.Status409Conflict", middleware);
        Assert.Contains("api_idempotency_records", middleware);
        Assert.Contains("uq_api_idempotency_scope_key", bootstrap);
        Assert.Contains("BeginTransactionAsync", middleware);
        Assert.Contains("CommitAsync", middleware);
        Assert.DoesNotContain("created_at >= NOW()", middleware);
    }

    [Fact]
    public void Idempotency_Header_IsAllowedForBrowserClients()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));

        Assert.Contains("\"Idempotency-Key\"", program);
        Assert.Contains("\"Idempotency-Replayed\"", program);
    }
}
