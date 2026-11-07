using System.IO;
using System.Reflection;
using System.Text.Json;
using Anguloso.Server.Logica;
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

    [Theory]
    [InlineData("barbell-curl", "Curl con Barra", "Barbell Curl", "barbell", "Beginner")]
    [InlineData("cable-curl", "Curl con Cable", "Cable Curl", "cable", "Beginner")]
    [InlineData("incline-db-curl", "Curl con mancuernas en banco inclinado", "Incline Dumbbell Curl", "dumbbell", "Intermediate")]
    [InlineData("hammer-curl", "Curl de Martillo con Mancuernas", "Dumbbell Hammer Curl", "dumbbell", "Beginner")]
    public void RepDbAutoReviewer_ApprovesCuratedBicepsCurlVariants(
        string id, string nameEs, string nameEn, string equipment, string difficulty)
    {
        var reviewerType = typeof(TrainingProgramGenerator).Assembly.GetType(
            "Anguloso.Server.Logica.RepDbExerciseAutoReviewer", throwOnError: true)!;
        var tryBuild = reviewerType.GetMethod(
            "TryBuild", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No se encontró el auto-revisor RepDB.");
        var metadata = tryBuild.Invoke(null, new object?[]
        {
            id,
            nameEs,
            nameEn,
            "Ejercicio de aislamiento para trabajar los flexores del codo.",
            JsonSerializer.Serialize(new[]
            {
                "Flexiona el codo manteniendo el brazo y el tronco estables.",
                "Desciende la carga lentamente hasta la extensión controlada."
            }),
            difficulty,
            equipment,
            "pull",
            "isolation",
            "upper_arms",
            JsonSerializer.Serialize(new[] { "biceps_brachii" }),
            JsonSerializer.Serialize(new[] { "brachialis" }),
            JsonSerializer.Serialize(new[] { "hypertrophy" }),
            "[]",
            false
        }) ?? throw new InvalidOperationException($"El auto-revisor no aprobó {id}.");

        var metadataType = metadata.GetType();
        Assert.Equal("Accessory", metadataType.GetProperty("MovementPattern")!.GetValue(metadata));
        var primaryMuscles = (string[])metadataType.GetProperty("PrimaryMuscles")!.GetValue(metadata)!;
        Assert.Equal(new[] { "Biceps" }, primaryMuscles);
        Assert.Contains(equipment,
            (string[])metadataType.GetProperty("RequiredEquipment")!.GetValue(metadata)!);
    }



    [Fact]
    public void RepDbAutoReviewer_KeepsSmithMachineAsSpecificEquipment()
    {
        var reviewerType = typeof(TrainingProgramGenerator).Assembly.GetType(
            "Anguloso.Server.Logica.RepDbExerciseAutoReviewer", throwOnError: true)!;
        var tryBuild = reviewerType.GetMethod(
            "TryBuild", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No se encontró el auto-revisor RepDB.");
        var metadata = tryBuild.Invoke(null, new object?[]
        {
            "smith-machine-bench-press",
            "Press de Banca en Máquina Smith",
            "Smith Machine Bench Press",
            "Ejercicio de empuje horizontal para trabajar el pecho.",
            JsonSerializer.Serialize(new[]
            {
                "Coloca los pies firmes y ajusta el banco bajo la barra guiada.",
                "Desciende la barra hacia el pecho con control.",
                "Empuja hasta extender los brazos sin bloquear bruscamente los codos."
            }),
            "intermediate",
            "smith_machine",
            "push",
            "compound",
            "chest",
            """["pectoralis_major"]""",
            """["triceps_brachii"]""",
            """["hypertrophy"]""",
            "[]",
            false
        }) ?? throw new InvalidOperationException("El auto-revisor no aprobó el press Smith.");

        var requiredEquipment = (string[])metadata.GetType()
            .GetProperty("RequiredEquipment")!.GetValue(metadata)!;
        Assert.Contains("smith-machine", requiredEquipment);
        Assert.DoesNotContain("machine", requiredEquipment);
    }

    [Fact]
    public void RepDbExerciseCuration_PreservesAuditedMuscleCorrections()
    {
        var curationType = typeof(TrainingProgramGenerator).Assembly.GetType(
            "Anguloso.Server.Logica.RepDbExerciseCuration", throwOnError: true)!;
        var curate = curationType.GetMethod(
            "CurateMusclesJson", BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("No se encontró la corrección de musculatura RepDB.");

        string Curated(string id, bool primary, string source) =>
            (string)curate.Invoke(null, new object?[] { id, primary, source })!;

        Assert.Equal("""["gluteus_maximus","quadriceps","hamstrings"]""",
            Curated("hex-bar-deadlift", true, """["erector_spinae","gluteus_maximus","quadriceps"]"""));
        Assert.Equal("""["erector_spinae","forearm_flexors","trapezius"]""",
            Curated("hex-bar-deadlift", false, """["forearm_flexors","hamstrings","trapezius"]"""));
        Assert.Equal("""["gluteus_maximus","quadriceps","adductors"]""",
            Curated("cossack-squat", true, """["gluteus_maximus","quadriceps"]"""));
        Assert.Equal("""["hamstrings"]""",
            Curated("cossack-squat", false, """["adductors","hamstrings"]"""));
        Assert.Equal("""["rectus_abdominis"]""",
            Curated("sit-ups", true, """["hip_flexors","rectus_abdominis"]"""));
        Assert.Equal("""["infraspinatus","teres_minor"]""",
            Curated("cable-external-rotation", true, """["posterior_deltoid"]"""));
    }

    [Fact]
    public void RepDbAutoReviewer_MapsTibialisRaiseAndAllRequiredEquipment()
    {
        var reviewerType = typeof(TrainingProgramGenerator).Assembly.GetType(
            "Anguloso.Server.Logica.RepDbExerciseAutoReviewer", throwOnError: true)!;
        var tryBuild = reviewerType.GetMethod(
            "TryBuild", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No se encontró el auto-revisor RepDB.");
        var metadata = tryBuild.Invoke(null, new object?[]
        {
            "kettlebell-tibialis-raise",
            "Elevación de Tibial con Pesa Rusa",
            "Kettlebell Tibialis Raise",
            "Ejercicio de dorsiflexión de tobillo para trabajar el tibial anterior.",
            JsonSerializer.Serialize(new[]
            {
                "Siéntate en un banco con una pierna extendida y el talón apoyado.",
                "Cuelga una pesa rusa del antepié y eleva los dedos hacia la espinilla.",
                "Baja lentamente y cambia de pierna al completar las repeticiones."
            }),
            "intermediate",
            "kettlebell",
            "push",
            "isolation",
            "lower_legs",
            """["tibialis_anterior"]""",
            "[]",
            """["hypertrophy","rehabilitation"]""",
            """["knee_safe","requires_bench"]""",
            false
        }) ?? throw new InvalidOperationException("El auto-revisor no aprobó el ejercicio de tibial anterior.");

        var metadataType = metadata.GetType();
        Assert.Equal("Accessory", metadataType.GetProperty("MovementPattern")!.GetValue(metadata));
        Assert.Contains("TibialisAnterior",
            (string[])metadataType.GetProperty("PrimaryMuscles")!.GetValue(metadata)!);
        var requiredEquipment = (string[])metadataType.GetProperty("RequiredEquipment")!.GetValue(metadata)!;
        Assert.Contains("bench", requiredEquipment);
        Assert.Contains("kettlebell", requiredEquipment);
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
        Assert.Contains("[HttpGet(\"export\")]", controller);
        Assert.Contains("[Authorize(Roles = \"superadmin\")]", controller);
        Assert.Contains("ExportCatalogAsync", controller);
        Assert.Contains("totalRecords = items.Count", controller);
        Assert.Contains("FROM exercise_catalog", service);
        Assert.Contains("ExportCatalogAsync", service);
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
        Assert.Contains("EnsureRepDbAccessoryPatternCurationV1Async", service);
        Assert.Contains("exercise-catalog-repdb-accessory-pattern-v1", service);
        Assert.Contains("reviewed_movement_pattern = 'Accessory'", service);
        Assert.Contains("reviewed_selection_priority = 55", service);
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
        Assert.Contains("\"band-assisted-pull-ups\"", curation);
        Assert.DoesNotContain("\"decline-db-fly\"", curation);
        var excludedIds = curation.Split("public static readonly string[] ExcludedIds =")[1].Split("];")[0];
        Assert.DoesNotContain("\"hammer-curl\"", excludedIds);
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
        var manager = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "exercise-manager", "exercise-manager.component.ts"));
        Assert.Contains("IsAccessoryExercise", autoReviewer);
        Assert.Contains("if (IsAccessoryExercise(id, es, en)) return \"Accessory\";", autoReviewer);
        Assert.Contains("'Accessory'", manager);
        Assert.Contains("'Accessory'", reviewUi);
        var contributions = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "exercise-contributions", "exercise-contributions.component.ts"));
        Assert.Contains("'Accessory'", contributions);
        Assert.DoesNotContain("IsAutoApprovalCandidate(id)", autoReviewer);
        Assert.Contains("HasUsefulInstructions", autoReviewer);
        Assert.Contains("MapEquipment", autoReviewer);
        Assert.Contains("leg extension", autoReviewer);
        Assert.Contains("lateral raise", autoReviewer);
        Assert.Contains("curl femoral", autoReviewer);
        Assert.Contains("var selectionPriority = GetSelectionPriority", autoReviewer);
        Assert.Contains(".Append(\"GeneralFitness\")", autoReviewer);
        Assert.Contains("EnsureRepDbGeneralFitnessGoalCurationV1Async", service);
        Assert.Contains("selectionPriority", autoReviewer);
        Assert.Contains("GetSelectionPriority", autoReviewer);
        Assert.Contains("machine shoulder press", autoReviewer);
        Assert.Contains("exercise-catalog-repdb-selection-priority-v1", service);
        Assert.Contains("EnsureRepDbSelectionPriorityCurationV1Async", service);
        Assert.Contains("reviewed_by LIKE 'RepDB automatic metadata%'", service);

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
    public void RepDbAutomaticMetadataRefresh_IsVersionedAndNeverTouchesHumanReviews()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "ExerciseCatalogService.cs"));
        Assert.Contains("EnsureRepDbAutomaticMetadataRefreshV1Async", service);
        Assert.Contains("exercise-catalog-repdb-automatic-metadata-refresh-v1", service);
        Assert.Contains("reviewed_by LIKE 'RepDB automatic metadata%'", service);
        Assert.Contains("review_status = 'pending', reviewed = FALSE", service);
        Assert.Contains("RepDB automatic metadata v5", service);
    }

    [Fact]
    public void RepDbSelectionPriority_PrefersScalableBasicsAndLoadedAccessories()
    {
        var reviewerType = typeof(TrainingProgramGenerator).Assembly
            .GetType("Anguloso.Server.Logica.RepDbExerciseAutoReviewer");
        Assert.NotNull(reviewerType);
        var method = reviewerType!.GetMethod(
            "GetSelectionPriority",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        int Priority(string id, string name, string pattern) =>
            (int)method!.Invoke(null, new object?[] { id, name, name, pattern })!;

        Assert.True(
            Priority("lat-pulldown", "Jalón en polea", "VerticalPull") >
            Priority("assisted-pull-ups", "Dominadas asistidas", "VerticalPull"));
        Assert.True(
            Priority("machine-leg-extension", "Extensión de cuádriceps en máquina", "Accessory") >
            Priority("bodyweight-lateral-raise", "Elevación lateral sin carga", "Accessory"));
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
        Assert.Contains("Assert.Equal(81, report.ScenarioCount)", evaluation);
    }


    [Fact]
    public void IdealRepDbCatalog_IsExplicitAndRejectsKnownFalsePositiveMovementNames()
    {
        var curationType = typeof(TrainingProgramGenerator).Assembly
            .GetType("Anguloso.Server.Logica.RepDbExerciseCuration");
        Assert.NotNull(curationType);
        var ids = (string[])curationType!.GetField(
            "CuratedCatalogIds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.GetValue(null)!;
        Assert.Equal(97, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("bench-press", ids);
        Assert.Contains("lat-pulldown", ids);
        Assert.Contains("squat", ids);
        Assert.Contains("romanian-deadlift", ids);
        Assert.Contains("leg-extension", ids);
        Assert.Contains("reverse-nordic-curl", ids);
        Assert.Contains("ball-leg-curl", ids);
        Assert.Contains("banded-standing-curl", ids);
        Assert.Contains("trx-hamstring-curl", ids);
        Assert.Contains("hammer-curl", ids);
        Assert.Contains("hip-abduction", ids);
        Assert.Contains("plate-loaded-glute-drive", ids);
        Assert.Contains("shrug", ids);
        Assert.Contains("straight-arm-pulldown", ids);
        Assert.Contains("overhead-tricep-extension", ids);
        Assert.Contains("incline-push-ups", ids);
        Assert.Contains("db-fly", ids);
        Assert.Contains("kettlebell-tibialis-raise", ids);
        var curateVariationGroup = curationType.GetMethod(
            "CurateVariationGroup", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        Assert.NotNull(curateVariationGroup);
        Assert.Equal("tibialis-raise", curateVariationGroup!.Invoke(
            null, new object?[] { "kettlebell-tibialis-raise", "calf-raise" }));
        Assert.Equal("leg-curl", curateVariationGroup.Invoke(null, new object?[] { "ball-leg-curl", "leg-curl" }));
        Assert.Equal("leg-curl", curateVariationGroup.Invoke(null, new object?[] { "banded-standing-curl", "bicep-curl" }));
        Assert.Equal("leg-curl", curateVariationGroup.Invoke(null, new object?[] { "trx-hamstring-curl", "leg-curl" }));
        Assert.Equal("leg-extension", curateVariationGroup.Invoke(null, new object?[] { "reverse-nordic-curl", "leg-extension" }));
        Assert.DoesNotContain("crow-pose", ids);
        Assert.DoesNotContain("dumbbell-side-bend", ids);

        var autoReviewer = typeof(TrainingProgramGenerator).Assembly
            .GetType("Anguloso.Server.Logica.RepDbExerciseAutoReviewer");
        Assert.NotNull(autoReviewer);
        var infer = autoReviewer!.GetMethod(
            "InferPattern", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(infer);
        string? Pattern(string id, string es, string en) =>
            (string?)infer!.Invoke(null, new object?[] { id, es, en, "[]", null, null, null, Array.Empty<string>() });

        Assert.Null(Pattern("crow-pose", "Postura del Cuervo", "Crow Pose"));
        Assert.Null(Pattern("dumbbell-side-bend", "Flexión lateral con mancuerna", "Dumbbell Side Bend"));
        Assert.Equal("Accessory", Pattern("hammer-curl", "Curl de Martillo con Mancuernas", "Dumbbell Hammer Curl"));
        Assert.Equal("Accessory", Pattern("kettlebell-tibialis-raise", "Elevación de Tibial con Pesa Rusa", "Kettlebell Tibialis Raise"));
        Assert.Equal("VerticalPull", Pattern("lat-pulldown", "Jalón al pecho", "Lat Pulldown"));
        Assert.Equal("Squat", Pattern("front-squat", "Sentadilla frontal", "Front Squat"));
        Assert.Equal("HorizontalPush", Pattern("db-bench-press", "Press de banca con mancuernas", "Dumbbell Bench Press"));
        Assert.Equal("SingleLeg", Pattern("bulgarian-split-squat", "Sentadilla búlgara", "Bulgarian Split Squat"));
        Assert.Equal("VerticalPush", Pattern("machine-shoulder-press", "Press de hombros en máquina", "Machine Shoulder Press"));
        Assert.Equal("Core", Pattern("machine-seated-crunch", "Crunch en máquina", "Machine Seated Crunch"));
        Assert.Equal("Core", Pattern("dead-bug", "Dead Bug", "Dead Bug"));
        Assert.Equal("Hinge", Pattern("machine-back-extension", "Extensión lumbar en máquina", "Machine Back Extension"));
        Assert.Equal("Squat", Pattern("horizontal-leg-press", "Prensa horizontal", "Horizontal Leg Press"));
        Assert.Equal("HorizontalPull", Pattern("kneeling-cable-row", "Remo arrodillado en polea", "Kneeling Cable Row"));
        Assert.Equal("VerticalPush", Pattern("machine-shoulder-press", "Press de hombros en máquina", "Machine Shoulder Press"));
        Assert.Equal("Core", Pattern("cable-crunch", "Crunch con cable", "Cable Crunch"));
        Assert.Equal("Core", Pattern("cable-pallof-press", "Press Pallof", "Cable Pallof Press"));
        Assert.Equal("Core", Pattern("dumbbell-farmers-walk", "Caminata del granjero", "Dumbbell Farmer's Walk"));

        var generateInstructions = autoReviewer.GetMethod(
            "GenerateInstructionsEs", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        var hasInstructions = autoReviewer.GetMethod(
            "HasUsefulInstructions", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        Assert.NotNull(generateInstructions);
        Assert.NotNull(hasInstructions);
        Assert.True((bool)hasInstructions!.Invoke(null, new object?[]
            { """["Flexiona la cadera de forma controlada.","Vuelve lentamente a la posición inicial.","Repite."]""" })!);
        Assert.False((bool)hasInstructions.Invoke(null, new object?[]
            { """["Flexiona la cadera de forma controlada.","Repite."]""" })!);
        Assert.Equal("HorizontalPush", Pattern("incline-push-ups", "Push-up inclinado", "Incline Push-Up"));
        Assert.Equal("Hinge", Pattern("plate-loaded-glute-drive", "Empuje de Glúteo en Máquina de Discos", "Plate-Loaded Glute Drive"));
        foreach (var id in ids)
        {
            var generated = (string)generateInstructions!.Invoke(null, new object?[] { id, id, id })!;
            Assert.True((bool)hasInstructions!.Invoke(null, new object?[] { generated })!, $"Faltan instrucciones en español para {id}.");
        }
    }

    [Fact]
    public void ScalableCuratedExercises_AreEligibleForBeginnerPrograms()
    {
        var reviewer = typeof(TrainingProgramGenerator).Assembly
            .GetType("Anguloso.Server.Logica.RepDbExerciseAutoReviewer");
        Assert.NotNull(reviewer);
        var build = reviewer!.GetMethod("TryBuild", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        Assert.NotNull(build);

        var cases = new[]
        {
            (Id: "dumbbell-romanian-deadlift", Equipment: "dumbbell", Muscles: """["gluteus_maximus"]"""),
            (Id: "hip-thrust", Equipment: "barbell", Muscles: """["gluteus_maximus"]"""),
            (Id: "machine-shoulder-press", Equipment: "shoulder_press_machine", Muscles: """["anterior_deltoid"]"""),
            (Id: "cable-crunch", Equipment: "cable", Muscles: """["rectus_abdominis"]"""),
            (Id: "cable-pallof-press", Equipment: "cable", Muscles: """["obliques"]"""),
            (Id: "side-plank", Equipment: "bodyweight", Muscles: """["obliques"]"""),
            (Id: "reverse-lunge", Equipment: "dumbbell", Muscles: """["quadriceps"]"""),
            (Id: "step-ups", Equipment: "dumbbell", Muscles: """["quadriceps"]"""),
            (Id: "chest-supported-db-row", Equipment: "dumbbell", Muscles: """["latissimus_dorsi"]"""),
            (Id: "single-arm-db-row", Equipment: "dumbbell", Muscles: """["latissimus_dorsi"]"""),
            (Id: "assisted-pull-ups", Equipment: "pull-up bar", Muscles: """["latissimus_dorsi"]""")
        };

        foreach (var item in cases)
        {
            var metadata = build!.Invoke(null, new object?[]
            {
                item.Id, item.Id, item.Id, "Descripción de prueba suficientemente completa.",
                """["Primera instrucción suficientemente clara.","Segunda instrucción suficientemente clara."]""",
                "intermediate", item.Equipment, null, null, null, item.Muscles, "[]",
                """["strength"]""", "[]", item.Equipment == "bodyweight"
            });
            Assert.NotNull(metadata);
            Assert.Equal("Beginner", metadata!.GetType().GetProperty("MinimumExperience")!.GetValue(metadata));
        }
    }

    [Fact]
    public void IdealCatalogBootstrapReplacesOnlyRepDbRowsAndPreservesProfessionalContributions()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "ExerciseCatalogService.cs"));
        Assert.Contains("exercise-catalog-ideal-strength-hypertrophy-v1", service);
        Assert.Contains("replaceRepDbOnly: true", service);
        Assert.Contains("DELETE FROM exercise_catalog", service);
        Assert.Contains("reviewed_by IS NULL OR reviewed_by LIKE 'RepDB automatic metadata%'", service);
        Assert.Contains("AND (source <> 'repdb-free' OR exercise_id = ANY(@curated_ids))", service);
        Assert.Contains("Add(command, \"curated_ids\", RepDbExerciseCuration.CuratedCatalogIds)", service);
        Assert.Contains("markIdealCatalogComplete: true", service);
        Assert.Contains("Training plans keep their historical exercise-name/ID snapshots", service);
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "ExerciseCatalogController.cs"));
        Assert.DoesNotContain("[HttpGet(\"audit-export\")]", controller);
        Assert.DoesNotContain("ExportRepDbAuditCatalog", controller);
    }

    [Fact]
    public void RepDbCuration_KeepsUsefulTibialisAndRotatorCuffAccessoriesEligible()
    {
        var curationType = typeof(TrainingProgramGenerator).Assembly
            .GetType("Anguloso.Server.Logica.RepDbExerciseCuration");
        Assert.NotNull(curationType);
        var shouldExclude = curationType!.GetMethod(
            "ShouldExclude", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
            binder: null, types: new[] { typeof(string), typeof(string), typeof(string), typeof(string) }, modifiers: null);
        Assert.NotNull(shouldExclude);
        bool Excluded(string id, string category, string equipment, string? variationGroup) =>
            (bool)shouldExclude!.Invoke(null, new object?[] { id, category, equipment, variationGroup })!;
        // The 466-row local export omitted it, but the official 701-exercise upstream contains it and the whitelist now includes it.
        Assert.False(Excluded("kettlebell-tibialis-raise", "strength", "kettlebell", "calf-raise"));
        Assert.False(Excluded("cable-external-rotation", "strength", "cable", null));
        Assert.False(Excluded("side-lying-hip-adduction", "strength", "", "hip-adduction"));

        var curation = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "RepDbExerciseCuration.cs"));
        var autoReviewer = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "RepDbExerciseAutoReviewer.cs"));
        Assert.Contains("[\"tibialis-raise\"] = [\"kettlebell-tibialis-raise\"]", curation);
        Assert.Contains("\"kettlebell-tibialis-raise\" => \"tibialis-raise\"", curation);
        Assert.Contains(@"[""infraspinatus"",""teres_minor""]", curation);
        Assert.Contains("tibialis raise", autoReviewer);
        Assert.Contains("external rotation", autoReviewer);
    }


    [Fact]
    public void RepDbAutoReviewer_ClassifiesKneeFlexionHamstringCurlsAsAccessories()
    {
        var reviewerType = typeof(TrainingProgramGenerator).Assembly
            .GetType("Anguloso.Server.Logica.RepDbExerciseAutoReviewer");
        Assert.NotNull(reviewerType);
        var method = reviewerType!.GetMethod(
            "InferPattern",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        string? Pattern(string id, string es, string en) =>
            (string?)method!.Invoke(null, new object?[]
            {
                id, es, en, "[]", "pull", "isolation", "lower_legs", new[] { "hamstrings" }
            });

        Assert.Equal("Accessory", Pattern("leg-curl", "Curl femoral", "Leg Curl"));
        Assert.Equal("Accessory", Pattern("seated-leg-curl", "Curl femoral sentado", "Seated Leg Curl"));
        Assert.Equal("Accessory", Pattern("nordic-hamstring-curl", "Curl nórdico", "Nordic Hamstring Curl"));
        Assert.Equal("Accessory", Pattern("ball-leg-curl", "Curl de Piernas con Pelota de Estabilidad", "Stability Ball Leg Curl"));
        Assert.Equal("Accessory", Pattern("banded-standing-curl", "Curl de Pierna de Pie con Banda", "Banded Standing Leg Curl"));
        Assert.Equal("Accessory", Pattern("trx-hamstring-curl", "Curl de Isquiotibiales con TRX", "TRX Hamstring Curl"));
        Assert.Equal("Accessory", Pattern("reverse-nordic-curl", "Nordic Curl Inverso", "Reverse Nordic Curl"));
        Assert.Equal("Hinge", Pattern("romanian-deadlift", "Peso muerto rumano", "Romanian Deadlift"));
        Assert.Equal("Hinge", Pattern("dumbbell-romanian-deadlift", "Peso muerto rumano con mancuernas", "Dumbbell Romanian Deadlift"));
    }

    [Fact]
    public void RepDbLegCurlPatternMigration_OnlyTouchesAutomaticRepDbApprovals()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "ExerciseCatalogService.cs"));
        Assert.Contains("EnsureRepDbLegCurlPatternCurationV1Async", service);
        Assert.Contains("exercise-catalog-repdb-leg-curl-accessory-pattern-v1", service);
        Assert.Contains("reviewed_by LIKE 'RepDB automatic metadata%'", service);
        Assert.Contains("owner_user_id IS NULL", service);
        Assert.Contains("nordic-hamstring-curl", service);
    }

}
