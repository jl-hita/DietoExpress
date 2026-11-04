using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class ProductionSmokeWorkflowRegressionTests
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
    public void ProductionDeploymentWorkflow_PerformsPostRestartSmokeTest()
    {
        // El workflow delega la activación y los smoke tests al script versionado.
        var deployScript = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "dietoexpress-deploy-atomic.sh"));

        Assert.Contains("sudo systemctl is-active --quiet dietoexpress.service", deployScript);
        Assert.Contains("https://$DOMAIN/", deployScript);
        Assert.Contains("https://$DOMAIN/api/directory/professionals", deployScript);
        Assert.Contains("python3 -c 'import json;", deployScript);
        Assert.Contains("grep -Fq \"DietoExpress\"", deployScript);
        Assert.Contains("rollback \"falló el smoke test de frontend o API\"", deployScript);
        Assert.Contains("rollback \"no se pudo reiniciar systemd\"", deployScript);
        Assert.Contains("PREVIOUS_BACKEND", deployScript);
        Assert.Contains("PREVIOUS_WEB", deployScript);
        Assert.Contains("[[ \"$attempt\" -lt 12 ]]", deployScript);
    }
}
