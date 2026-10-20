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
    }
}
