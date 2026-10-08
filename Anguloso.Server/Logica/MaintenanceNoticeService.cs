using System.Text.Json;

namespace Anguloso.Server.Logica;

/// <summary>
/// Aviso de mantenimiento que debe sobrevivir a una parada de DietoExpress.
/// Se almacena fuera de PostgreSQL para que siga siendo válido mientras la base de datos se restaura.
/// </summary>
public sealed class MaintenanceNoticeService
{
    private const string DefaultPath = "/var/lib/dietoexpress/maintenance.json";
    private readonly string _path;
    private readonly object _sync = new();

    public MaintenanceNoticeService(IConfiguration configuration)
    {
        _path = configuration["DIETOEXPRESS_MAINTENANCE_FILE"]
            ?? Environment.GetEnvironmentVariable("DIETOEXPRESS_MAINTENANCE_FILE")
            ?? DefaultPath;
    }

    public MaintenanceNotice? Get()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_path)) return null;
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<MaintenanceNotice>(json);
            }
            catch
            {
                // Un aviso roto no debe impedir que la aplicación arranque.
                return null;
            }
        }
    }

    public void Activate(string title, string message)
    {
        var notice = new MaintenanceNotice(true, title, message, DateTimeOffset.UtcNow);
        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(notice));
            File.Move(temporary, _path, true);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch
            {
                // El aviso no es crítico para la operación del servicio; systemd volverá a intentarlo al finalizar la restauración.
            }
        }
    }
}

public sealed record MaintenanceNotice(bool Active, string Title, string Message, DateTimeOffset StartedAtUtc);
