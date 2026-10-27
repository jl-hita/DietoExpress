using System.IO;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class ExerciseCatalogRegressionTests
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

    [Fact]
    public void RepDbCatalog_IsStoredInPostgresWithCompleteSourcePayloadAndReviewedMetadata()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "ExerciseCatalogService.cs"));
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "ExerciseCatalogController.cs"));
        var program = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));

        Assert.Contains("source_payload JSONB NOT NULL", service);
        Assert.Contains("primary_muscles JSONB", service);
        Assert.Contains("secondary_muscles JSONB", service);
        Assert.Contains("instructions_es JSONB", service);
        Assert.Contains("tips_es JSONB", service);
        Assert.Contains("images JSONB", service);
        Assert.Contains("reviewed_primary_muscles JSONB", service);
        Assert.Contains("reviewed_secondary_muscles JSONB", service);
        Assert.Contains("ON CONFLICT (exercise_id) DO UPDATE", service);
        Assert.Contains("exercise-catalog-repdb-spanish-reset-v2", service);
        Assert.Contains("DELETE FROM exercise_catalog", service);
        Assert.Contains("replaceExisting: true, markBootstrapComplete: true", service);
        Assert.Contains("name_es", service);
        Assert.Contains("instructions_es", service);
        Assert.Contains("ResetLegacyAutoReviewsAsync", service);
        Assert.Contains("review_status VARCHAR(20)", service);
        Assert.Contains("author VARCHAR(120) NOT NULL DEFAULT 'superadmin'", service);
        Assert.Contains("owner_user_id INTEGER", service);
        Assert.Contains("owner_user_id = @owner_user_id AND review_status = 'pending'", service);
        Assert.Contains("author = 'superadmin' OR review_status = 'approved'", service);
        Assert.Contains("includeAllOwners: true", controller);
        Assert.Contains("contributorUserId", controller);
        Assert.Contains("LoadGeneratorCandidatesAsync", service);
        Assert.Contains("IsOwnedByCurrentProfessional", File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "TrainingProgramGenerator.cs")));
        Assert.Contains("review_status = 'approved'", service);
        Assert.Contains("owner_user_id = @owner_user_id", service);
        Assert.Contains("owner_movement_pattern", service);
        Assert.Contains("GeneratePreview", File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPeriodizationController.cs")));
        Assert.Contains("MatSelectModule", File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "exercise-contributions", "exercise-contributions.component.ts")));
        Assert.Contains("WHERE reviewed_by = 'DietoExpress curated metadata v1'", service);
        Assert.DoesNotContain("ApplyCuratedReviewsAsync", service);
        Assert.Contains("EnsureSeededAsync", program);
        Assert.Contains("connection.CreateCommand()", service);
        Assert.Contains("command.CommandText = sql", service);
        Assert.DoesNotContain("ExecuteSqlRawAsync(sql, cancellationToken)", service);
        Assert.Contains("[Authorize(Roles = \"superadmin\")]", controller);
        Assert.DoesNotContain("source_payload FROM exercise_catalog", service);
    }

    [Fact]
    public void ExerciseLibrary_LoadsFromPostgresAndEvaluationUsesCuratedRealExercises()
    {
        var ui = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "training-periodization", "training-periodization.component.ts"));
        var evaluation = File.ReadAllText(Path.Combine(RepoRoot, "Tests", "DietoExpress.Security.Tests", "TrainingGeneratorEvaluationReportTests.cs"));

        Assert.Contains("'/api/exercise-catalog?limit=100'", ui);
        Assert.DoesNotContain("https://exercise-dataset.com/exercises.json", ui);
        Assert.Contains("repdb-free-curated-v1", evaluation);
        Assert.Contains("step-ups", evaluation);
        Assert.Contains("romanian-deadlift", evaluation);
        Assert.Contains("ab-wheel-rollout", evaluation);
        Assert.Contains("Assert.Equal(27, report.ScenarioCount)", evaluation);
    }
}
