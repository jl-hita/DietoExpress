using System.IO;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingPeriodizationRegressionTests
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
    public void TrainingModule_IsOptInAndUsesSeparateExecutionRecords()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPeriodizationController.cs"));
        var ui = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "training-periodization", "training-periodization.component.ts"));

        Assert.Contains("training_profiles", bootstrap);
        Assert.Contains("enabled BOOLEAN NOT NULL DEFAULT TRUE", bootstrap);
        Assert.Contains("training_session_records", bootstrap);
        Assert.Contains("training_collaborations", bootstrap);
        Assert.Contains("ON CONFLICT(session_id, client_id)", controller);
        Assert.Contains("CanAccessClientInternal", controller);
        Assert.Contains("No tener perfil todavía es un estado normal", controller);
        Assert.Contains("managementMode = \"none\"", controller);
        Assert.Contains("timeout({ first: 15000 })", ui);
        Assert.Contains("No se pudieron cargar los planes. Comprueba la conexión y vuelve a intentarlo.", ui);
        Assert.Contains("NOT @manage AND EXISTS (", controller);
        Assert.Contains("InviteCollaborator", controller);
        Assert.Contains("AcceptCollaboration", controller);
        Assert.Contains("RevokeCollaboration", controller);
        Assert.Contains("GetMyInvitations", controller);
        Assert.Contains("NOT @manage AND EXISTS (", controller);
        Assert.Contains("training_collaborations", controller);
        Assert.Contains("tc.status = 'active'", controller);
        Assert.Contains("Activar para este cliente", ui);
        Assert.Contains("Desactivar módulo", ui);
        var portalController = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPortalController.cs"));
        var portalUi = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.ts"));
        Assert.Contains("[Authorize(Roles = \"patient\")]", portalController);
        Assert.Contains("f.client_id = @clientId AND f.enabled = TRUE", portalController);
        Assert.Contains("/api/portal/training/sessions", portalUi);
    }

    [Fact]
    public void GeneratedMesocycle_IsPersistedAtomicallyAndRequiresProfessionalReview()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPeriodizationController.cs"));
        var generator = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "TrainingProgramGenerator.cs"));

        Assert.Contains("[HttpPost(\"plans/{planId:int}/generate-mesocycle\")]", controller);
        Assert.Contains("TrainingProgramGenerator.Generate(request.Generation, candidates)", controller);
        Assert.Contains("BeginTransactionAsync(cancellationToken)", controller);
        Assert.Contains("INSERT INTO training_mesocycles", controller);
        Assert.Contains("INSERT INTO training_microcycles", controller);
        Assert.Contains("INSERT INTO training_sessions", controller);
        Assert.Contains("No se han conservado cambios parciales", controller);
        Assert.Contains("requiresProfessionalReview = true", controller);
        Assert.Contains("selectedThisWeek.Add(candidate.ExerciseId);", generator);
        Assert.DoesNotContain("selectedThisWeek.Add(candidate.ExerciseId);\n\n                    selectedThisWeek.Add(candidate.ExerciseId);", generator);
        var ui = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "training-periodization", "training-periodization.component.ts"));
        Assert.Contains("Generar mesociclo automático", ui);
        Assert.Contains("previewGeneratedMesocycle(plan)", ui);
        Assert.Contains("saveGeneratedMesocycle(plan)", ui);
        Assert.Contains("Descartar vista previa", ui);
        Assert.Contains("nextMesocycleStart(plan)", ui);
        Assert.Contains("autoStartDates[plan.id] || this.nextMesocycleStart(plan)", ui);
    }

    [Fact]
    public void GeneratedPrescriptions_AreStoredAsStructuredJsonAndRemainBackwardCompatible()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPeriodizationController.cs"));
        var portalController = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPortalController.cs"));

        Assert.Contains("UpgradeTrainingPeriodizationSchemaV6(context, logger)", bootstrap);
        Assert.Contains("training-periodization-v6-structured-prescriptions", bootstrap);
        Assert.Contains("ADD COLUMN IF NOT EXISTS exercise_prescription_data JSONB NULL", bootstrap);
        Assert.Contains("exercise_prescription_data::text", controller);
        Assert.Contains("exercise_prescription_data)", controller);
        Assert.Contains("exercisePrescriptionData = reader.IsDBNull(19)", portalController);
        Assert.Contains("exercise_prescription_data::text", portalController);
        Assert.Contains("exercise_prescription_data = CASE WHEN s.exercise_prescription IS DISTINCT FROM @exercisePrescription", controller);
        Assert.Contains("JsonElement? ExercisePrescriptionData = null", controller);
        // The legacy text field is still written and returned for existing web and Android clients.
        Assert.Contains("exercise_prescription, exercise_prescription_data", controller);
        Assert.Contains("exercisePrescription = reader.IsDBNull(13)", portalController);
    }

    [Fact]
    public void PatientPortalRendersStructuredGeneratedExerciseDetailsAndFallsBackToLegacyText()
    {
        var component = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.ts"));
        var template = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.html"));
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPortalController.cs"));

        Assert.Contains("exercisePrescriptionData?: StructuredTrainingPrescription | null", component);
        Assert.Contains("session.exercisePrescriptionData?.exercises?.length; else legacyTrainingPrescription", template);
        Assert.Contains("exercise.exerciseNameSnapshot", template);
        Assert.Contains("exercise.targetRepetitionsInReserve", template);
        Assert.Contains("exercise.restSeconds", template);
        Assert.Contains("trainingProgressionLabel(exercise.progressionRule)", template);
        Assert.Contains("Array<string | number>", component);
        Assert.Contains("const numericLabels = [", component);
        Assert.Contains("'Cuádriceps'", component);
        Assert.Contains("ng-template #legacyTrainingPrescription", template);
        Assert.Contains("exercisePrescriptionData = reader.IsDBNull(19)", controller);
    }

    [Fact]
    public void PlanTemplates_PreserveStructuredExercisePrescriptionWhenCopied()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPeriodizationController.cs"));

        Assert.Contains("exercise_prescription_data::text", controller);
        Assert.Contains("JsonElement? ExercisePrescriptionData = null", controller);
        Assert.Contains("session.ExercisePrescriptionData.Value.GetRawText()", controller);
        Assert.Contains("exercise_prescription_data)", controller);
        Assert.Contains("ExercisePrescriptionData = null", controller);
    }

    [Fact]
    public void SessionTemplates_PreserveStructuredPrescriptionsAndClearThemWhenTextChanges()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPeriodizationController.cs"));
        var ui = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "training-periodization", "training-periodization.component.ts"));

        Assert.Contains("UpgradeTrainingPeriodizationSchemaV7(context, logger)", bootstrap);
        Assert.Contains("training-periodization-v7-structured-session-templates", bootstrap);
        Assert.Contains("ADD COLUMN IF NOT EXISTS exercise_prescription_data JSONB NULL", bootstrap);
        Assert.Contains("exercise_prescription_data::text, created_at", controller);
        Assert.Contains("exercisePrescriptionData = reader.IsDBNull(7)", controller);
        Assert.Contains("request.ExercisePrescriptionData.Value.GetRawText()", controller);
        Assert.Contains("draft.exercisePrescriptionData = saved.exercisePrescriptionData ?? null", ui);
        Assert.Contains("exercisePrescriptionData: draft.exercisePrescriptionData", ui);
        Assert.Contains("exercisePrescriptionData = null", ui);
    }

    [Fact]
    public void GeneratedMesocyclePreviewShowsWeeklyMuscleVolumeDetails()
    {
        var ui = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "training-periodization", "training-periodization.component.ts"));
        var analyzer = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "TrainingProgramQualityAnalyzer.cs"));

        Assert.Contains("generated.qualityReport.weeklyMuscleVolumes", ui);
        Assert.Contains("muscle.primarySets", ui);
        Assert.Contains("muscle.secondarySets", ui);
        Assert.Contains("muscle.primaryFrequencySessions", ui);
        Assert.Contains("HighPrimarySetWarningThreshold = 20", analyzer);
    }

    [Fact]
    public void PatientTrainingFeedback_IsPersistedAndFeedsOnlyARecommendation()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        var portalController = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPortalController.cs"));
        var portalUi = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.html"));
        var portalComponent = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.ts"));

        Assert.Contains("UpgradeTrainingPeriodizationSchemaV5(context, logger)", bootstrap);
        Assert.Contains("training-periodization-v5-adaptation-feedback", bootstrap);
        Assert.Contains("recovery_rating", bootstrap);
        Assert.Contains("progression_target_achieved", bootstrap);
        Assert.Contains("TrainingAdaptationPolicy.Evaluate(feedback)", portalController);
        Assert.Contains("COALESCE(EXCLUDED.outcome, training_session_records.outcome)", portalController);
        Assert.Contains("adaptationRecommendation", portalController);
        Assert.Contains("nonCompletionReason", portalController);
        Assert.Contains("training-outcome-", portalUi);
        Assert.Contains("training-recovery-", portalUi);
        Assert.Contains("training-pain-", portalUi);
        Assert.Contains("trainingAdaptationLabel", portalComponent);
        Assert.Contains("actualDurationMinutes: missed || input.duration == null ? null", portalComponent);
    }

    [Fact]
    public void TrainingOffer_IsPublicAndConsultationChoiceCanBeChanged()
    {
        var userModel = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Models", "users.cs"));
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPeriodizationController.cs"));
        var directoryController = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "DirectoryController.cs"));
        var settings = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "settings", "settings.component.html"));
        var directoryUi = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "directory", "directory.component.html"));
        var consultation = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "guided-consultation", "guided-consultation.component.ts"));
        var trainingUi = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "training-periodization", "training-periodization.component.ts"));
        var portalUi = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.ts"));

        Assert.Contains("directory_sports_training", userModel);
        Assert.Contains("directory_sports_training BOOLEAN NOT NULL DEFAULT FALSE", bootstrap);
        Assert.Contains("OffersSportsTraining = u.directory_sports_training ?? false", directoryController);
        Assert.Contains("directorySportsTraining", settings);
        Assert.Contains("item.offersSportsTraining", directoryUi);
        Assert.Contains("trainingManagementMode", consultation);
        Assert.Contains("managementMode: this.trainingManagementMode", consultation);
        Assert.Contains("management_mode VARCHAR(20) NOT NULL DEFAULT 'self'", bootstrap);
        Assert.Contains("management_mode = EXCLUDED.management_mode", controller);
        Assert.Contains("collaboration", consultation);
        Assert.Contains("Gestión del entrenamiento", trainingUi);
        Assert.Contains("managementMode: this.trainingManagementMode", trainingUi);
        Assert.Contains("record-form", trainingUi);
        Assert.Contains("Guardar realización", trainingUi);
        Assert.DoesNotContain("window.prompt", trainingUi);
        Assert.Contains("UpgradeTrainingPeriodizationSchemaV2(context, logger)", bootstrap);
        Assert.Contains("UpgradeTrainingPeriodizationSchemaV3(context, logger)", bootstrap);
        Assert.Contains("training_mesocycles", bootstrap);
        Assert.Contains("training_microcycles", bootstrap);
        Assert.Contains("CreateMesocycle", controller);
        Assert.Contains("CreateMicrocycle", controller);
        Assert.Contains("[HttpPut(\"mesocycles/{mesocycleId:int}\")]", controller);
        Assert.Contains("[HttpDelete(\"mesocycles/{mesocycleId:int}\")]", controller);
        Assert.Contains("[HttpPost(\"plans/{planId:int}/mesocycles/reorder\")]", controller);
        Assert.Contains("[HttpPut(\"microcycles/{microcycleId:int}\")]", controller);
        Assert.Contains("[HttpDelete(\"microcycles/{microcycleId:int}\")]", controller);
        Assert.Contains("[HttpPost(\"mesocycles/{mesocycleId:int}/microcycles/reorder\")]", controller);
        Assert.Contains("No se puede borrar un mesociclo que contiene microciclos", controller);
        Assert.Contains("await CanManageClient(clientId.Value, cancellationToken)", controller);
        Assert.Contains("c.tenant_id IS NOT NULL", controller);
        Assert.Contains("beginEditMesocycle", trainingUi);
        Assert.Contains("moveMesocycle", trainingUi);
        Assert.Contains("deleteMicrocycle", trainingUi);
        Assert.Contains("existing.start_date <= COALESCE(@endDate, 'infinity'::date)", controller);
        Assert.Contains("COALESCE(existing.end_date, 'infinity'::date) >= @startDate", controller);
        Assert.Contains("Mesociclos", trainingUi);
        Assert.Contains("Añadir microciclo", trainingUi);
        Assert.Contains("Invitaciones de colaboración", trainingUi);
        Assert.Contains("training-invitations", File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "app.routes.ts")));
        Assert.Contains("No tienes invitaciones pendientes", trainingUi);
        Assert.Contains("[HttpPut(\"sessions/{sessionId:int}\")]", controller);
        Assert.Contains("await CanManageClient(clientId.Value, cancellationToken)", controller);
        Assert.Contains("La fecha de la sesión debe estar dentro del periodo del plan.", controller);
        Assert.Contains("session-workspace", trainingUi);
        Assert.Contains("session-editor-grid", trainingUi);
        Assert.Contains("beginEditSession", trainingUi);
        Assert.Contains("saveSession", trainingUi);
        Assert.Contains("Instrucciones", trainingUi);
        Assert.Contains("Calendario de entrenamiento", trainingUi);
        Assert.Contains("week-grid", trainingUi);
        Assert.Contains("week-summary", trainingUi);
        Assert.Contains("shiftWeek(plan, -1)", trainingUi);
        Assert.Contains("goToCurrentWeek(plan)", trainingUi);
        Assert.Contains("setSessionView(plan.id, 'list')", trainingUi);
        Assert.Contains("weekPlannedMinutes", trainingUi);
        Assert.Contains("weekCompletedCount", trainingUi);
        Assert.Contains("exercise_prescription", bootstrap);
        Assert.Contains("UpgradeTrainingPeriodizationSchemaV4(context, logger)", bootstrap);
        Assert.Contains("GetLoadTrend", controller);
        Assert.Contains("load-trend", trainingUi);
        Assert.Contains("exportSessions", trainingUi);
        Assert.Contains("exercisePrescription", trainingUi);
        Assert.Contains("exercisePrescription", portalUi);
        Assert.Contains("editSessionFromCalendar(session)", trainingUi);
        Assert.Contains("recordSessionFromCalendar(session)", trainingUi);
        Assert.Contains("this.setSessionView(session.planId, 'list')", trainingUi);
    }
}
