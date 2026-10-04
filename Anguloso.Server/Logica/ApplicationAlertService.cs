using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Anguloso.Server.Models;

namespace Anguloso.Server.Logica;

/// <summary>
/// Persiste incidencias operativas para que un SuperAdmin pueda detectarlas desde la aplicación
/// sin depender de revisar el log. Si PostgreSQL no está disponible, deja una copia pendiente
/// en disco para importarla automáticamente cuando la base de datos vuelva a estar accesible.
/// </summary>
public static class ApplicationAlertService
{
    private sealed class PendingAlert
    {
        public string Severity { get; set; } = "error";
        public string Component { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? TechnicalDetails { get; set; }
    }

    /// <summary>
    /// Intenta guardar la incidencia en PostgreSQL y, si no es posible, la deja en el spool local.
    /// El mecanismo de alerta nunca propaga una excepción secundaria.
    /// </summary>
    public static async Task RecordAsync(
        angulosodbContext context,
        string severity,
        string component,
        string title,
        string message,
        Exception? exception = null,
        ILogger? logger = null)
    {
        var alert = CreateAlert(severity, component, title, message, exception);

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO system_alerts
                    (severity, component, title, message, technical_details,
                     first_seen_at, last_seen_at, occurrences)
                VALUES
                    ({alert.Severity}, {alert.Component}, {alert.Title}, {alert.Message},
                     {alert.TechnicalDetails}, NOW(), NOW(), 1)
                ON CONFLICT (component, title) WHERE resolved_at IS NULL
                DO UPDATE SET
                    last_seen_at = NOW(),
                    occurrences = system_alerts.occurrences + 1,
                    message = EXCLUDED.message,
                    technical_details = EXCLUDED.technical_details;");
        }
        catch (Exception alertException)
        {
            logger?.LogError(alertException,
                "No se pudo persistir la alerta {Component}/{Title}; se guardará en el spool local.",
                alert.Component, alert.Title);

            await WritePendingAsync(alert, logger);
        }
    }

    /// <summary>
    /// Reintenta las incidencias almacenadas en disco después de un fallo de conexión o bootstrap.
    /// Los archivos se eliminan únicamente después de una inserción confirmada en PostgreSQL.
    /// </summary>
    public static async Task FlushPendingAsync(angulosodbContext context, ILogger? logger = null)
    {
        var directory = GetSpoolDirectory();
        if (!Directory.Exists(directory))
            return;

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(p => p))
        {
            try
            {
                var alert = JsonSerializer.Deserialize<PendingAlert>(await File.ReadAllTextAsync(path));
                if (alert == null)
                    throw new InvalidDataException("El archivo de alerta está vacío o no tiene un formato válido.");

                await context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO system_alerts
                        (severity, component, title, message, technical_details,
                         first_seen_at, last_seen_at, occurrences)
                    VALUES
                        ({alert.Severity}, {alert.Component}, {alert.Title}, {alert.Message},
                         {alert.TechnicalDetails}, NOW(), NOW(), 1)
                    ON CONFLICT (component, title) WHERE resolved_at IS NULL
                    DO UPDATE SET
                        last_seen_at = NOW(),
                        occurrences = system_alerts.occurrences + 1,
                        message = EXCLUDED.message,
                        technical_details = EXCLUDED.technical_details;");

                File.Delete(path);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "No se pudo importar la alerta pendiente {AlertFile}.", path);
                // Si falla una alerta, se conserva para reintentarlo en el siguiente arranque.
            }
        }
    }

    private static PendingAlert CreateAlert(
        string severity,
        string component,
        string title,
        string message,
        Exception? exception)
    {
        var normalizedSeverity = severity.Trim().ToLowerInvariant();
        if (normalizedSeverity != "critical" &&
            normalizedSeverity != "error" &&
            normalizedSeverity != "warning" &&
            normalizedSeverity != "info")
            normalizedSeverity = "error";

        var technicalDetails = exception?.ToString();
        if (technicalDetails?.Length > 20000)
            technicalDetails = technicalDetails[..20000];

        return new PendingAlert
        {
            Severity = normalizedSeverity,
            Component = component.Trim(),
            Title = title.Trim(),
            Message = message.Trim(),
            TechnicalDetails = technicalDetails
        };
    }

    private static async Task WritePendingAsync(PendingAlert alert, ILogger? logger)
    {
        try
        {
            var directory = GetSpoolDirectory();
            Directory.CreateDirectory(directory);

            // Un archivo por incidencia evita que un fallo durante una escritura corrompa todo el historial pendiente.
            var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(alert));
            File.Move(tempPath, path);
        }
        catch (Exception spoolException)
        {
            logger?.LogError(spoolException, "No se pudo guardar la alerta en el spool local.");
        }
    }

    private static string GetSpoolDirectory() =>
        Environment.GetEnvironmentVariable("DIETOEXPRESS_ALERT_SPOOL")
        ?? (OperatingSystem.IsWindows()
            ? Path.Combine(AppContext.BaseDirectory, "AlertSpool")
            : "/var/lib/dietoexpress/AlertSpool");
}
