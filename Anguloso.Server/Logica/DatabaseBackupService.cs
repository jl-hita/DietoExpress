using System.Diagnostics;

namespace Anguloso.Server.Logica;

/// <summary>
/// Gestiona las copias PostgreSQL operativas desde el panel SuperAdmin.
/// La restauración de producción sigue siendo una operación de infraestructura:
/// nunca se ejecuta dentro del proceso web que contiene la propia base de datos.
/// </summary>
public sealed class DatabaseBackupService
{
    private readonly string _backupDir;
    private readonly string _databaseUrl;
    private readonly string _databaseName;
    private readonly string _dbUser;
    private readonly string _dbHost;
    private readonly string _dbPort;
    private readonly string _restoreScript;
    private readonly MaintenanceNoticeService _maintenance;

    public DatabaseBackupService(IConfiguration configuration, MaintenanceNoticeService maintenance)
    {
        _backupDir = configuration["DIETOEXPRESS_BACKUP_DIR"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_BACKUP_DIR") ?? string.Empty;
        _databaseUrl = configuration["DIETOEXPRESS_DATABASE_URL"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_DATABASE_URL") ?? string.Empty;
        _databaseName = configuration["DIETOEXPRESS_DATABASE"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_DATABASE") ?? string.Empty;
        _dbUser = configuration["DIETOEXPRESS_DB_USER"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_DB_USER") ?? string.Empty;
        _dbHost = configuration["DIETOEXPRESS_DB_HOST"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_DB_HOST") ?? "127.0.0.1";
        _dbPort = configuration["DIETOEXPRESS_DB_PORT"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_DB_PORT") ?? "5432";
        _restoreScript = configuration["DIETOEXPRESS_RESTORE_SCRIPT"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_RESTORE_SCRIPT") ?? Path.Combine(AppContext.BaseDirectory, "scripts", "dietoexpress-restore.sh");
        _maintenance = maintenance;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_backupDir) && !string.IsNullOrWhiteSpace(_databaseName);

    public IReadOnlyList<DatabaseBackupInfo> List()
    {
        if (!Directory.Exists(_backupDir)) return [];
        return Directory.EnumerateFiles(_backupDir, "dietoexpress-postgresql-*.dump")
            // Solo se ofrecen para restauración snapshots completos. Los dumps antiguos
            // sin archivo documental asociado siguen en disco, pero no son coherentes
            // con la restauración coordinada y no deben presentarse como restaurables.
            .Where(HasDocumentSnapshot)
            .Select(path => new DatabaseBackupInfo(Path.GetFileName(path), GetLogicalSnapshotSize(path), File.GetCreationTimeUtc(path)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<DatabaseBackupInfo> CreateAsync(CancellationToken ct)
    {
        EnsureConfigured();
        var script = Path.Combine(AppContext.BaseDirectory, "scripts", "dietoexpress-backup.sh");
        if (!File.Exists(script)) script = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "dietoexpress-backup.sh");
        if (!File.Exists(script)) throw new InvalidOperationException("No se encuentra el script operativo de backup.");

        var result = await RunAsync(script, ct);
        if (result.ExitCode != 0) throw new InvalidOperationException($"El backup ha fallado: {result.Output}");

        var latest = List().FirstOrDefault();
        if (latest is null) throw new InvalidOperationException("El backup terminó correctamente pero no se encontró la copia generada.");
        return latest;
    }

    public async Task<string> VerifyAsync(string fileName, CancellationToken ct)
    {
        EnsureConfigured();
        var safeName = ValidateBackupFileName(fileName);
        EnsureBackupExists(safeName);
        var result = await RunRestoreScriptAsync("--verify", safeName, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"La verificación ha fallado: {result.Output}");
        return result.Output;
    }

    public async Task<string> RestoreAsync(string fileName, CancellationToken ct)
    {
        EnsureConfigured();
        var safeName = ValidateBackupFileName(fileName);
        EnsureBackupExists(safeName);

        // El aviso se escribe antes de arrancar systemd y vive fuera de PostgreSQL.
        // Así los usuarios que ya están conectados lo ven antes de que el servicio se detenga
        // y el estado no se pierde durante la restauración de la propia base de datos.
        _maintenance.Activate(
            "Mantenimiento urgente",
            "DietoExpress va a realizar una restauración de la base de datos. La aplicación puede dejar de estar disponible temporalmente. Por favor, no inicies nuevas operaciones hasta que finalice.");

        var wrapper = configurationRestoreWrapper();
        var result = await RunRestoreScriptAsync("--restore", safeName, ct, wrapper);
        if (result.ExitCode != 0)
        {
            // Si el wrapper no llegó a arrancar la unidad, no debemos dejar un aviso permanente.
            _maintenance.Clear();
            throw new InvalidOperationException($"La restauración ha fallado: {result.Output}");
        }

        return result.Output;
    }

    public async Task<DatabaseRestoreStatus> GetRestoreStatusAsync(string fileName, CancellationToken ct)
    {
        EnsureConfigured();
        var safeName = ValidateBackupFileName(fileName);
        var unitName = $"dietoexpress-restore@{safeName}.service";
        var psi = new ProcessStartInfo { FileName = "/usr/bin/systemctl", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.ArgumentList.Add("show"); psi.ArgumentList.Add("--no-page");
        psi.ArgumentList.Add("--property=ActiveState"); psi.ArgumentList.Add("--property=SubState"); psi.ArgumentList.Add("--property=Result");
        psi.ArgumentList.Add("--property=ExecMainStartTimestamp"); psi.ArgumentList.Add("--property=ExecMainExitTimestamp"); psi.ArgumentList.Add("--property=ExecMainStatus");
        psi.ArgumentList.Add(unitName);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo consultar el estado de la restauración.");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) throw new InvalidOperationException($"No se pudo consultar el estado de la restauración: {error.Trim()}");
        var values = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=', 2)).Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.Ordinal);
        var activeState = values.GetValueOrDefault("ActiveState", "unknown");
        var subState = values.GetValueOrDefault("SubState", "unknown");
        var result = values.GetValueOrDefault("Result", "unknown");
        var startTimestamp = values.GetValueOrDefault("ExecMainStartTimestamp", "");
        var exitTimestamp = values.GetValueOrDefault("ExecMainExitTimestamp", "");
        var exitStatus = values.GetValueOrDefault("ExecMainStatus", "");
        var completed = activeState == "inactive" && result == "success";
        var failed = activeState == "failed" || (activeState == "inactive" && result != "success" && result != "unknown");
        var running = activeState is "activating" or "active" || subState is "running" or "start";
        var phase = failed ? "failed" : completed ? "completed" : running ? "restoring" : "starting";
        return new DatabaseRestoreStatus(safeName, unitName, phase, activeState, subState, result, startTimestamp, exitTimestamp, exitStatus, completed, failed);
    }

    public (Stream Content, string ContentType, string FileName) GetBundle(string fileName)
    {
        EnsureConfigured();
        var safeName = ValidateBackupFileName(fileName);
        EnsureBackupExists(safeName);
        if (!HasDocumentSnapshot(Path.Combine(_backupDir, safeName)))
            throw new InvalidOperationException("La copia no contiene un snapshot documental asociado y no puede descargarse como snapshot completo.");

        var stem = Path.GetFileNameWithoutExtension(safeName);
        var suffix = stem["dietoexpress-postgresql-".Length..];
        var documentsName = $"dietoexpress-documents-{suffix}";
        var entries = new List<string>
        {
            safeName,
            safeName[..^".dump".Length] + ".sha256",
            documentsName,
            documentsName + ".sha256"
        };
        var metadataName = safeName[..^".dump".Length] + ".txt";
        if (File.Exists(Path.Combine(_backupDir, metadataName))) entries.Add(metadataName);
        foreach (var entry in entries)
            if (!(string.Equals(entry, documentsName, StringComparison.Ordinal) ? Directory.Exists(Path.Combine(_backupDir, entry)) : File.Exists(Path.Combine(_backupDir, entry))))
                throw new FileNotFoundException($"Falta un componente del snapshot: {entry}");

        var temporaryBundle = Path.Combine(Path.GetTempPath(), $"dietoexpress-snapshot-{Guid.NewGuid():N}.tar.gz");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/tar",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--create");
            psi.ArgumentList.Add("--gzip");
            psi.ArgumentList.Add("--file");
            psi.ArgumentList.Add(temporaryBundle);
            psi.ArgumentList.Add("--directory");
            psi.ArgumentList.Add(_backupDir);
            foreach (var entry in entries) psi.ArgumentList.Add(entry);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo empaquetar el snapshot.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var stderr = stderrTask.GetAwaiter().GetResult();
            _ = stdoutTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"No se pudo empaquetar el snapshot: {stderr.Trim()}");

            var stream = new FileStream(temporaryBundle, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose);
            return (stream, "application/gzip", stem + "-completo.tar.gz");
        }
        catch
        {
            try { File.Delete(temporaryBundle); } catch { /* best-effort cleanup */ }
            throw;
        }
    }

    private long GetLogicalSnapshotSize(string dumpPath)
    {
        var size = new FileInfo(dumpPath).Length;
        var metadataPath = Path.ChangeExtension(dumpPath, ".txt");
        if (!File.Exists(metadataPath)) return size;

        const string prefix = "documents_size_bytes=";
        var value = File.ReadLines(metadataPath)
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return value is not null && long.TryParse(value[prefix.Length..], out var documentsSize)
            ? size + documentsSize
            : size;
    }

    private bool HasDocumentSnapshot(string dumpPath)
    {
        const string prefix = "dietoexpress-postgresql-";
        var stem = Path.GetFileNameWithoutExtension(dumpPath);
        if (!stem.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var suffix = stem[prefix.Length..];
        var snapshot = Path.Combine(_backupDir, $"dietoexpress-documents-{suffix}");
        return Directory.Exists(snapshot) && File.Exists(snapshot + ".sha256");
    }

    private void EnsureBackupExists(string fileName)
    {
        var path = Path.Combine(_backupDir, fileName);
        if (!File.Exists(path)) throw new FileNotFoundException("No se ha encontrado la copia solicitada.", path);
    }

    private string ValidateBackupFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName ?? string.Empty);
        if (!safeName.Equals(fileName, StringComparison.Ordinal) ||
            !safeName.StartsWith("dietoexpress-postgresql-", StringComparison.Ordinal) ||
            !safeName.EndsWith(".dump", StringComparison.Ordinal) ||
            safeName.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')))
            throw new ArgumentException("Nombre de copia no válido.", nameof(fileName));

        return safeName;
    }

    private string configurationRestoreWrapper()
    {
        var wrapper = Environment.GetEnvironmentVariable("DIETOEXPRESS_RESTORE_WRAPPER");
        //return string.IsNullOrWhiteSpace(wrapper) ? "/opt/dietoexpress/scripts/dietoexpress-restore-web.sh" : wrapper;
        return string.IsNullOrWhiteSpace(wrapper)
            ? "/usr/local/sbin/dietoexpress-restore-web"
            : wrapper;
    }

    private async Task<(int ExitCode, string Output)> RunRestoreScriptAsync(string operation, string fileName, CancellationToken ct, string? wrapper = null)
    {
        var executable = wrapper ?? _restoreScript;
        var psi = new ProcessStartInfo
        {
            FileName = wrapper is null ? "/usr/bin/env" : "/usr/bin/sudo",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (wrapper is null)
        {
            psi.ArgumentList.Add("bash");
            psi.ArgumentList.Add(executable);
            psi.ArgumentList.Add(operation);
            psi.ArgumentList.Add(fileName);
        }
        else
        {
            // La restauración modifica PostgreSQL y detiene el servicio, por lo que
            // requiere un wrapper root explícitamente autorizado en sudoers.
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add(executable);
            psi.ArgumentList.Add(fileName);
            psi.ArgumentList.Add("--confirm");
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar la operación de restauración.");
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, string.Join(Environment.NewLine, stdout, stderr).Trim());
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured) throw new InvalidOperationException("Las copias de seguridad no están configuradas en el servidor.");
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string script, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/env",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add(script);
        psi.Environment["DIETOEXPRESS_BACKUP_DIR"] = _backupDir;
        if (!string.IsNullOrWhiteSpace(_databaseUrl)) psi.Environment["DIETOEXPRESS_DATABASE_URL"] = _databaseUrl;
        if (!string.IsNullOrWhiteSpace(_databaseName)) psi.Environment["DIETOEXPRESS_DATABASE"] = _databaseName;
        if (!string.IsNullOrWhiteSpace(_dbUser)) psi.Environment["DIETOEXPRESS_DB_USER"] = _dbUser;
        psi.Environment["DIETOEXPRESS_DB_HOST"] = _dbHost;
        psi.Environment["DIETOEXPRESS_DB_PORT"] = _dbPort;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar la operación de backup.");
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, string.Join(Environment.NewLine, stdout, stderr).Trim());
    }
}

public sealed record DatabaseBackupInfo(string FileName, long SizeBytes, DateTime CreatedAtUtc);

public sealed record DatabaseRestoreStatus(string FileName, string UnitName, string Phase, string ActiveState, string SubState, string Result, string StartTimestamp, string ExitTimestamp, string ExitStatus, bool Completed, bool Failed);
