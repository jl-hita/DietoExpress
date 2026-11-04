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

    [Fact]
    public void AtomicDeployment_MustUseVersionedDirectoriesAndAtomicLinks()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "dietoexpress-deploy-atomic.sh"));

        Assert.Contains("releases", script);
        Assert.Contains("flock -n 9", script);
        Assert.Contains("mv -Tf", script);
        Assert.Contains("PREVIOUS_BACKEND", script);
        Assert.Contains("PREVIOUS_WEB", script);
        Assert.Contains("rollback", script);
        Assert.Contains("dietoexpress-smoke-directory.json", script);
        Assert.DoesNotContain("rm -rf /opt/dietoexpress/*", script);
        Assert.DoesNotContain("rm -rf /var/www/dietoexpress/*", script);
    }

    [Fact]
    public void ProductionWorkflow_MustDelegateDeploymentToAtomicScript()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "deploy.yml"));

        Assert.Contains("scripts/dietoexpress-deploy-atomic.sh", workflow);
        Assert.Contains("DIETOEXPRESS_VERSION: ${{ github.sha }}", workflow);
        Assert.DoesNotContain("rm -rf /opt/dietoexpress/*", workflow);
        Assert.DoesNotContain("rm -rf /var/www/dietoexpress/*", workflow);
    }

    [Fact]
    public void DeploymentManual_MustDescribeVersionedPathsAndRollback()
    {
        var manual = File.ReadAllText(Path.Combine(RepoRoot, "docs", "GUIA_DESPLIEGUE_LINUX_PRODUCCION.md"));

        Assert.Contains("/opt/dietoexpress/releases", manual);
        Assert.Contains("/opt/dietoexpress/current", manual);
        Assert.Contains("/var/www/dietoexpress-releases", manual);
        Assert.Contains("rollback", manual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PostgreSQL", manual);
    }
    [Fact]
    public void AtomicDeployment_MustNormalizeReleasePermissionsAndVerifyNginxReadAccess()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "dietoexpress-deploy-atomic.sh"));

        Assert.Contains("chown -R joso:joso", script);
        Assert.Contains("chmod 0755", script);
        Assert.Contains("chmod 0644", script);
        Assert.Contains("chmod 0750", script);
        Assert.Contains("sudo -u nginx test -x", script);
        Assert.Contains("sudo -u nginx test -r", script);
        Assert.Contains("restorecon -RF", script);
    }

}
