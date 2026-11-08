using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class StartupCatalogReadinessTests
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
    public void Startup_RequiresExerciseCatalogInitializationBeforeDatabaseIsReady()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));
        var exerciseInitialization = program.IndexOf("await exerciseCatalog.EnsureSeededAsync()", StringComparison.Ordinal);
        var ready = program.IndexOf("databaseReady = true", StringComparison.Ordinal);

        Assert.True(exerciseInitialization >= 0);
        Assert.True(ready > exerciseInitialization);
        Assert.Contains("bedca-import-complete:", program);
    }

    [Fact]
    public void ExerciseBootstrap_RepairsMissingCuratedIdsEvenWhenOldMarkersExist()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "ExerciseCatalogService.cs"));

        Assert.Contains("exercise-catalog-ideal-strength-hypertrophy-v2", service);
        Assert.Contains("present == RepDbExerciseCuration.CuratedCatalogIds.Length", service);
        Assert.Contains("replaceRepDbOnly: true", service);
        Assert.Contains("preserve", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("replaceExisting: true, markBootstrapComplete: true", service);
    }

    [Fact]
    public void BedcaImport_DoesNotSwallowFoodPersistenceFailures()
    {
        var client = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "BEDCAClient.cs"));

        Assert.Contains("startup catalog was not marked complete", client);
        Assert.Contains("throw new InvalidOperationException", client);
    }
}
