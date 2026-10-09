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
        var root = RepoRoot;
        var script = File.ReadAllText(Path.Combine(root, "scripts", "dietoexpress-backup.sh"));
        var privilegedHelper = File.ReadAllText(Path.Combine(root, "scripts", "dietoexpress-pg-backup-root.sh"));

        Assert.Contains("DIETOEXPRESS_BACKUP_DIR", script);
        Assert.Contains("ERROR: DIETOEXPRESS_BACKUP_DIR", script);
        Assert.Contains("sudo -n", script);
        Assert.Contains("DIETOEXPRESS_BACKUP_MODE", script);
        Assert.Contains("--format=custom", privilegedHelper);
        Assert.Contains("--username=postgres", privilegedHelper);
        Assert.Contains("pg_dump", privilegedHelper);
        Assert.DoesNotContain("--no-owner", privilegedHelper);
        Assert.Contains("sha256sum", privilegedHelper);
        Assert.Contains("umask 077", privilegedHelper);
        Assert.Contains("DIETOEXPRESS_BACKUP_RETENTION", privilegedHelper);
        Assert.Contains("tail -n +$((RETENTION + 1))", privilegedHelper);
        Assert.Contains("! -name '*-pre-*'", privilegedHelper);
        Assert.DoesNotContain("/opt/dietoexpress", script);
    }

    [Fact]
    public void BackupAutomation_MustUseWeeklyTimerAndSafeRestoreBoundary()
    {
        var root = RepoRoot;
        var backupSystemdService = File.ReadAllText(Path.Combine(root, "scripts", "systemd", "dietoexpress-backup.service"));
        var backupTimer = File.ReadAllText(Path.Combine(root, "scripts", "systemd", "dietoexpress-backup.timer"));
        var provisioning = File.ReadAllText(Path.Combine(root, "scripts", "provision-dietoexpress-backup-automation.sh"));
        var restoreWrapper = File.ReadAllText(Path.Combine(root, "scripts", "dietoexpress-restore-web.sh"));
        var restoreUnit = File.ReadAllText(Path.Combine(root, "scripts", "systemd", "dietoexpress-restore@.service"));
        var restorePost = File.ReadAllText(Path.Combine(root, "scripts", "dietoexpress-restore-post.sh"));
        var restoreUser = File.ReadAllText(Path.Combine(root, "scripts", "dietoexpress-restore-user.sh"));
        var pgRestoreHelper = File.ReadAllText(Path.Combine(root, "scripts", "dietoexpress-pg-restore-root.sh"));
        var pgBackupHelper = File.ReadAllText(Path.Combine(root, "scripts", "dietoexpress-pg-backup-root.sh"));
        var deploy = File.ReadAllText(Path.Combine(root, ".github", "workflows", "deploy.yml"));
        var controller = File.ReadAllText(Path.Combine(root, "Anguloso.Server", "Controllers", "AdminDatabaseBackupController.cs"));
        var databaseBackupService = File.ReadAllText(Path.Combine(root, "Anguloso.Server", "Logica", "DatabaseBackupService.cs"));
        var adminDashboard = File.ReadAllText(Path.Combine(root, "anguloso.client", "src", "app", "componentes", "admin", "admin-dashboard.component.ts"));
        var maintenanceController = File.ReadAllText(Path.Combine(root, "Anguloso.Server", "Controllers", "MaintenanceController.cs"));
        var maintenanceService = File.ReadAllText(Path.Combine(root, "Anguloso.Server", "Logica", "MaintenanceNoticeService.cs"));
        var appComponent = File.ReadAllText(Path.Combine(root, "anguloso.client", "src", "app", "app.component.ts"));
        var appTemplate = File.ReadAllText(Path.Combine(root, "anguloso.client", "src", "app", "app.component.html"));

        Assert.Contains("OnCalendar=Sun 02:00", backupTimer);
        Assert.Contains("Persistent=true", backupTimer);
        Assert.Contains("DIETOEXPRESS_BACKUP_RETENTION:-8", pgBackupHelper);
        Assert.Contains("systemctl enable --now dietoexpress-backup.timer", provisioning);
        Assert.Contains("/usr/local/sbin/dietoexpress-restore-web", provisioning);
        Assert.Contains("/usr/local/sbin/dietoexpress-pg-backup", provisioning);
        Assert.Contains("/usr/local/sbin/dietoexpress-pg-restore", provisioning);
        Assert.Contains("systemctl start --no-block", restoreWrapper);
        Assert.Contains("runuser -u joso", restoreUnit);
        Assert.Contains("ExecStopPost=/usr/local/sbin/dietoexpress-restore-post", restoreUnit);
        Assert.Contains("maintenance.json", restorePost);
        Assert.Contains("EXIT_STATUS", restorePost);
        Assert.Contains("exit 20", restoreUser);
        Assert.Contains("exit 10", restoreUser);
        Assert.Contains("DIETOEXPRESS_BACKUP_MODE=pre-restore", restoreUser);
        Assert.Contains("/var/lib/dietoexpress-backups", provisioning);
        Assert.Contains("joso no debe poder modificar el almacén", pgRestoreHelper);
        Assert.Contains("stat -c '%u' \"$source_file\"", pgRestoreHelper);
        Assert.Contains("install -d -o root -g \"$BACKUP_GROUP\" -m 0750", pgBackupHelper);
        Assert.Contains("published_dump=false", pgBackupHelper);
        Assert.Contains("PRE_RETENTION", pgBackupHelper);

        Assert.Contains("--backup-pre-restore", provisioning);
        Assert.Contains("flock 9", pgBackupHelper);
        Assert.Contains("flock 9", pgRestoreHelper);
        Assert.DoesNotContain("--no-owner", pgBackupHelper);
        Assert.DoesNotContain("--no-owner", pgRestoreHelper);
        Assert.DoesNotContain("install-dietoexpress-backup-automation.sh", deploy);
        Assert.DoesNotContain("sudo bash /opt/dietoexpress/scripts/", deploy);
        Assert.Contains("[HttpPost(\"{fileName}/verify\")]", controller);
        Assert.Contains("[HttpPost(\"{fileName}/restore\")]", controller);
        Assert.Contains("/usr/local/sbin/dietoexpress-restore-web", databaseBackupService);
        Assert.Contains("maintenance", databaseBackupService);
        Assert.Contains("[HttpGet(\"{fileName}/restore-status\")]", controller);
        Assert.Contains("getDatabaseRestoreStatus", File.ReadAllText(Path.Combine(root, "anguloso.client", "src", "app", "servicios", "admin.service.ts")));
        Assert.Contains("databaseRestoreElapsedSeconds", adminDashboard);
        Assert.Contains("startDatabaseRestorePolling", adminDashboard);
        Assert.Contains("getDatabaseRestoreStatus", adminDashboard);
        Assert.DoesNotContain("databaseRestoreCountdownSeconds", adminDashboard);
        Assert.DoesNotContain("La restauración comenzará en", adminDashboard);
        Assert.DoesNotContain("cancelDatabaseRestoreCountdown", adminDashboard);
        Assert.Contains("[AllowAnonymous]", maintenanceController);
        Assert.Contains("DIETOEXPRESS_MAINTENANCE_FILE", maintenanceService);
        Assert.Contains("timer(0, 2000)", appComponent);
        Assert.Contains("maintenanceNotice", appComponent);
        Assert.Contains("maintenanceNotice", appTemplate);
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
