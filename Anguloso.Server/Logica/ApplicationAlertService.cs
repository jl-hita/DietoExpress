using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Anguloso.Server.Logica;

/// <summary>
/// Registra incidencias operativas persistentes para que un SuperAdmin pueda detectarlas desde la aplicación
/// sin depender de revisar el log. Nunca propaga una excepción secundaria si el mecanismo de alerta falla.
/// </summary>
public static class ApplicationAlertService
{
    public static async Task RecordAsync(
        angulosodbContext context,
        string severity,
        string component,
        string title,
        string message,
        Exception? exception = null)
    {
        try
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

            await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO system_alerts
                    (severity, component, title, message, technical_details,
                     first_seen_at, last_seen_at, occurrences)
                VALUES
                    ({normalizedSeverity}, {component.Trim()}, {title.Trim()}, {message.Trim()},
                     {technicalDetails}, NOW(), NOW(), 1)
                ON CONFLICT (component, title) WHERE resolved_at IS NULL
                DO UPDATE SET
                    last_seen_at = NOW(),
                    occurrences = system_alerts.occurrences + 1,
                    message = EXCLUDED.message,
                    technical_details = EXCLUDED.technical_details;");
        }
        catch (Exception alertException)
        {
            // La alerta es una ayuda operativa y jamás debe ocultar el error original que la provocó.
            context.Database.GetService<ILogger<ApplicationAlertService>>()
                .LogError(alertException, "No se pudo persistir la alerta de aplicación {Component}/{Title}.", component, title);
        }
    }
}
