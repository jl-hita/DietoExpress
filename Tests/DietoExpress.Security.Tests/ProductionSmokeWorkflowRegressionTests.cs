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
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "deploy.yml"));

        Assert.Contains("systemctl is-active --quiet dietoexpress.service", workflow);
        Assert.Contains("https://jlhitap.duckdns.org/", workflow);
        Assert.Contains("https://jlhitap.duckdns.org/api/directory/professionals", workflow);
        Assert.Contains("python3 -c 'import json;", workflow);
        Assert.Contains("grep -Fq \"DietoExpress\"", workflow);
    }
}
