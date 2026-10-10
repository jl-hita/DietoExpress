using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DeploymentConcurrencyRegressionTests
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
    public void ProductionDeployment_MustNotCancelAnActiveDeployment()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "deploy.yml"));

        Assert.Contains("group: dietoexpress-production", workflow);
        Assert.Contains("cancel-in-progress: false", workflow);
        Assert.DoesNotContain("group: dietoexpress-production\n  cancel-in-progress: true", workflow);
    }

    [Fact]
    public void PullRequestValidation_CanStillCancelObsoleteRuns()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "pr-validation.yml"));

        Assert.Contains("cancel-in-progress: true", workflow);
    }
}
