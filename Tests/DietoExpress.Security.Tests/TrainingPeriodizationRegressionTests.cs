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
        Assert.Contains("Activar para este cliente", ui);
        Assert.Contains("Desactivar módulo", ui);
        var portalController = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPortalController.cs"));
        var portalUi = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.ts"));
        Assert.Contains("[Authorize(Roles = \"patient\")]", portalController);
        Assert.Contains("f.client_id = @clientId AND f.enabled = TRUE", portalController);
        Assert.Contains("/api/portal/training/sessions", portalUi);
    }
}
