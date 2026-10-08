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

    public DatabaseBackupService(IConfiguration configuration)
    {
        _backupDir = configuration["DIETOEXPRESS_BACKUP_DIR"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_BACKUP_DIR") ?? string.Empty;
        _databaseUrl = configuration["DIETOEXPRESS_DATABASE_URL"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_DATABASE_URL") ?? string.Empty;
        _databaseName = configuration["DIETOEXPRESS_DATABASE"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_DATABASE") ?? string.Empty;
        _dbUser = configuration["DIETOEXPRESS_DB_USER"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_DB_USER") ?? string.Empty;
        _dbHost = configuration["DIETOEXPRESS_DB_HOST"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_DB_HOST") ?? "127.0.0.1";
        _dbPort = configuration["DIETOEXPRESS_DB_PORT"] ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_DB_PORT") ?? "5432";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_backupDir) && (!string.IsNullOrWhiteSpace(_databaseUrl) || !string.IsNullOrWhiteSpace(_databaseName));

    public IReadOnlyList<DatabaseBackupInfo> List()
    {
        if (!Directory.Exists(_backupDir)) return [];
        return Directory.EnumerateFiles(_backupDir, "dietoexpress-postgresql-*.dump")
            .Select(path => new DatabaseBackupInfo(Path.GetFileName(path), new FileInfo(path).Length, File.GetCreationTimeUtc(path)))
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

    public (string Path, string ContentType, string FileName) GetDump(string fileName)
    {
        EnsureConfigured();
        var safeName = Path.GetFileName(fileName);
        if (!safeName.Equals(fileName, StringComparison.Ordinal) ||
            !safeName.StartsWith("dietoexpress-postgresql-", StringComparison.Ordinal) ||
            !safeName.EndsWith(".dump", StringComparison.Ordinal))
            throw new ArgumentException("Nombre de copia no válido.", nameof(fileName));

        var path = Path.Combine(_backupDir, safeName);
        if (!File.Exists(path)) throw new FileNotFoundException("No se ha encontrado la copia solicitada.");
        return (path, "application/octet-stream", safeName);
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
