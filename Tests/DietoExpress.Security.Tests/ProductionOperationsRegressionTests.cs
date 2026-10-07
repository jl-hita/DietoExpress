using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class ProductionOperationsRegressionTests
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
    public void ProductionOperations_MustDocumentRecoveryAndOperationalBoundaries()
    {
        var root = RepoRoot;
        var operations = File.ReadAllText(Path.Combine(root, "docs", "OPERACION_1_0.md"));
        var deployment = File.ReadAllText(Path.Combine(root, ".github", "workflows", "deploy.yml"));

        Assert.Contains("Backup", operations);
        Assert.Contains("Restauración", operations);
        Assert.Contains("Rollback", operations);
        Assert.Contains("Recuperación ante pérdida completa del servidor", operations);
        Assert.Contains("Soporte de primera línea", operations);
        Assert.Contains("Alertas externas operativas y verificadas", operations);
        Assert.Contains("Restauración de un backup probada en infraestructura independiente", operations);
        Assert.Contains("Smoke test producción", deployment);
        Assert.Contains("test -d /var/lib/dietoexpress/Logs", deployment);
        Assert.Contains("test -d /var/lib/dietoexpress/AlertSpool", deployment);
    }

    [Fact]
    public void ProductionBackupScript_MustFailWithoutExplicitDestination()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "dietoexpress-backup.sh"));

        Assert.Contains("DIETOEXPRESS_BACKUP_DIR", script);
        Assert.Contains("ERROR: DIETOEXPRESS_BACKUP_DIR", script);
        Assert.Contains("pg_dump", script);
        Assert.Contains("--format=custom", script);
        Assert.Contains("sha256sum", script);
        Assert.Contains("umask 077", script);
        Assert.DoesNotContain("/opt/dietoexpress", script);
    }

    [Fact]
    public void ProductionHealthcheck_MustCoverCoreServicesAndDisk()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "dietoexpress-healthcheck.sh"));

        Assert.Contains("dietoexpress.service", script);
        Assert.Contains("nginx", script);
        Assert.Contains("postgresql", script);
        Assert.Contains("disk_usage_root<90%", script);
        Assert.Contains("AlertSpool", script);
    }
}
