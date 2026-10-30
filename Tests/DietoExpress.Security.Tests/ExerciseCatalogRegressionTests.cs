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
        Assert.Contains("[HttpGet(\"review-stats\")]", controller);
        Assert.Contains("GetReviewStatsAsync", controller);
        Assert.Contains("COUNT(*) FILTER (WHERE review_status = 'pending')", service);
        Assert.Contains("repdb_auto_approved_v2", service);
        Assert.Contains("pending_missing_description", service);
        Assert.Contains("pending_missing_difficulty", service);
        var reviewUi = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "admin", "admin-exercise-review.component.ts"));
        Assert.Contains("'/api/exercise-catalog/review-stats'", reviewUi);
        Assert.Contains("s.repDbPending", reviewUi);
        Assert.Contains("s.repDbAutoApprovedV2", reviewUi);
        Assert.Contains("[HttpGet(\"review-queue/export\")]", controller);
        Assert.Contains("totalPending = stats.pending", controller);
        Assert.Contains("complete = items.Count == stats.pending", controller);
        Assert.Contains("ExportReviewQueueAsync", controller);
        Assert.Contains("description_es, description_en", service);
        Assert.Contains("tips_es::text", service);
        Assert.Contains("source_payload::text", service);
        Assert.Contains("const string repairSql", service);
        Assert.Contains("source_payload->>'name_es'", service);
        Assert.Contains("instructions_es = CASE", service);
        Assert.Contains("EnsureRepDbQualityCurationAsync", service);
        Assert.Contains("EnsureRepDbCatalogRefreshV4Async", service);
        Assert.Contains("exercise-catalog-repdb-curation-refresh-v4", service);
        Assert.Contains("EnsureRepDbExerciseMetadataCurationV4Async", service);
        Assert.Contains("exercise-catalog-repdb-exercise-metadata-v4", service);
        Assert.Contains("dumbbell-push-press", service);
        Assert.Contains("behind-the-neck-lat-pulldown", service);
        Assert.Contains("RepDbExerciseCuration.ShouldExclude(id, sourceCategory, equipment, variationGroup)", service);
        Assert.Contains("jsonb_each(@preferred_variants::jsonb)", service);
        Assert.Contains("PreferredIdsByVariationGroup", service);
        Assert.Contains("category = 'cardio'", service);
        var curation = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "RepDbExerciseCuration.cs"));
        Assert.Contains("\"cardio\"", curation);
        Assert.Contains("\"endurance\"", curation);
        Assert.Contains("RequiresManualReview", curation);
        Assert.Contains("exercise-catalog-repdb-quality-curation-v2", service);
        Assert.Contains("exercise-catalog-repdb-description-difficulty-v2", service);
        Assert.Contains("owner_user_id IS NULL", service);
        Assert.Contains("exercise_id = ANY(@excluded_ids)", service);
        Assert.Contains("lower(g.goal) NOT IN ('hypertrophy', 'strength')", service);
        Assert.Contains("\"banded-sumo-walk\"", curation);
        Assert.Contains("[\"clean\"] = []", curation);
        Assert.Contains("string.Equals(category, \"olympic\"", curation);
        Assert.Contains("secondaryMusclesJson", service);
        Assert.Contains("EnsureRepDbCatalogRefreshAsync", service);
        Assert.Contains("exercise-catalog-repdb-curation-refresh-v3", service);
        Assert.Contains("EnsureRepDbGeneratorScopeCurationAsync", service);
        Assert.Contains("exercise-catalog-repdb-generator-scope-v3", service);
        Assert.Contains("DietoExpress generator curation v3", service);
        Assert.Contains("newly eligible exercises", service);
        Assert.Contains("review_status = 'rejected'", service);
        Assert.Contains("lower(COALESCE(category, ''))", service);
        Assert.Contains("every startup", service);
        Assert.Contains("\"hammer-curl\"", curation);
        Assert.Contains("\"band-assisted-pull-ups\"", curation);
        Assert.DoesNotContain("\"decline-db-fly\"", curation);
        var excludedIds = curation.Split("public static readonly string[] ExcludedIds =")[1].Split("];")[0];
        Assert.DoesNotContain("\"leg-extension\"", excludedIds);
        Assert.DoesNotContain("\"lateral-raise\"", excludedIds);
        Assert.Contains("Rules are intentionally auditable", curation);
        Assert.Contains("CurateVariationGroup", curation);
        Assert.Contains("[\"farmer-carry\"]", curation);
        Assert.Contains("[\"leg-extension\"]", curation);
        Assert.Contains("\"banded-standing-curl\"", curation);
        Assert.Contains("\"squat\"", curation);
        Assert.Contains("\"front-squat\"", curation);
        Assert.Contains("\"deadlift\"", curation);
        Assert.Contains("\"romanian-deadlift\"", curation);
        Assert.Contains("\"db-bench-press\"", curation);
        Assert.Contains("\"incline-db-press\"", curation);
        Assert.Contains("\"leg-press\"", curation);
        Assert.Contains("\"ohp\"", curation);
        Assert.Contains("\"lat-pulldown\"", curation);
        Assert.Contains("behind-the-neck-lat-pulldown", curation);
        Assert.Contains("IsAutoApprovalCandidate", curation);
        var autoReviewer = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "RepDbExerciseAutoReviewer.cs"));
        Assert.Contains("IsAccessoryExercise", autoReviewer);
        Assert.DoesNotContain("IsAutoApprovalCandidate(id)", autoReviewer);
        Assert.Contains("HasUsefulInstructions", autoReviewer);
        Assert.Contains("MapEquipment", autoReviewer);
        Assert.Contains("leg extension", autoReviewer);
        Assert.Contains("lateral raise", autoReviewer);
        Assert.Contains("curl femoral", autoReviewer);
        Assert.Contains("var selectionPriority = IsAccessoryExercise", autoReviewer);
        Assert.Contains("selectionPriority", autoReviewer);

        // Every anatomical identifier present in the reviewed RepDB exports must have an exact mapping.
        // In particular, substring matching "lat" must never turn lateral deltoid into latissimus.
        Assert.Contains("\"anterior_deltoid\" or \"lateral_deltoid\" or \"posterior_deltoid\" or \"deltoids\" => \"Deltoids\"", autoReviewer);
        Assert.Contains("\"latissimus_dorsi\" or \"lats\" => \"Lats\"", autoReviewer);
        Assert.Contains("\"quadratus_lumborum\" => \"ErectorSpinae\"", autoReviewer);
        Assert.Contains("\"adductors\" or \"adductor_longus\"", autoReviewer);
        Assert.Contains("\"abductors\" or \"tensor_fasciae_latae\"", autoReviewer);
        Assert.Contains("\"hip_flexors\" or \"iliopsoas\"", autoReviewer);
        Assert.Contains("\"serratus_anterior\" => \"Serratus\"", autoReviewer);
        Assert.Contains("\"tibialis_anterior\" => \"TibialisAnterior\"", autoReviewer);
        Assert.Contains("\"supraspinatus\" or \"infraspinatus\"", autoReviewer);
        Assert.DoesNotContain("Has(v, \"lat\"", autoReviewer);
        Assert.Contains("\"push press\"", autoReviewer);
        Assert.Contains("\"pike push ups\"", autoReviewer);
        Assert.Contains("\"dips\"", autoReviewer);
        Assert.Contains("\"bulgarian split squat\"", autoReviewer);
        Assert.Contains("\"split squat\"", autoReviewer);
        Assert.Contains("\"landmine press\"", autoReviewer);
        Assert.Contains("\"sentadilla cosaca\"", autoReviewer);
        Assert.Contains("\"curl de pierna de pie\"", autoReviewer);
        Assert.Contains("pike-push-ups", service);
        Assert.Contains("single-leg-press", service);
        Assert.Contains("\"banded-standing-curl\"", autoReviewer);

        var muscleTaxonomy = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "TrainingPrescription.cs"));
        Assert.Contains("Adductors", muscleTaxonomy);
        Assert.Contains("Abductors", muscleTaxonomy);
        Assert.Contains("HipFlexors", muscleTaxonomy);
        Assert.Contains("Serratus", muscleTaxonomy);
        Assert.Contains("TibialisAnterior", muscleTaxonomy);
        Assert.Contains("RotatorCuff", muscleTaxonomy);

        Assert.DoesNotContain("source_payload FROM exercise_catalog", service);
    }

    [Fact]
    public void ExerciseManagerFiltersCatalogAndEnforcesOwnerOrSuperadminPermissions()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "ExerciseCatalogService.cs"));
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "ExerciseCatalogController.cs"));
        var routes = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "app.routes.ts"));
        var manager = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "exercise-manager", "exercise-manager.component.ts"));

        Assert.Contains("movementPattern", service);
        Assert.Contains("primaryMuscle", service);
        Assert.Contains("secondaryMuscle", service);
        Assert.Contains("reviewStatus", service);
        Assert.Contains("includeAllOwners ||", service);
        Assert.Contains("owner_user_id = @actor_user_id", service);
        Assert.Contains("UpdateProfessionalContributionAsync", controller);
        Assert.Contains("all && isSuperAdmin", controller);
        Assert.Contains("[HttpPut(\"{id}/manage\")]", controller);
        Assert.Contains("exercise-manager", routes);
        Assert.Contains("item.canEdit", manager);
        Assert.Contains("newExercise()", manager);
        var searchMethod = service.Substring(service.IndexOf("SearchAsync("));
        Assert.DoesNotContain("await EnsureSchemaAsync(cancellationToken);", searchMethod.Split("public async Task<bool> SaveReviewAsync")[0]);
        Assert.Contains("timeout(30000)", manager);
        Assert.Contains("TimeoutError", manager);
        Assert.Contains("ChangeDetectorRef", manager);
        Assert.Contains("this.changeDetector.detectChanges()", manager);
        Assert.Contains("this.rows = Array.isArray(rows) ? [...rows] : []", manager);
        Assert.Contains("const requestId = ++this.searchRequestId", manager);
        Assert.Contains("if (requestId !== this.searchRequestId) return;", manager);
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
