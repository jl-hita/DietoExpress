using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Anguloso.Server.Logica;

public sealed record AppointmentEmailResult(bool Success, string Message);

/// <summary>
/// Compone los emails de cita con un enlace mágico al portal cuyo vencimiento
/// coincide como mínimo con las 24 horas posteriores al final previsto de la cita.
/// </summary>
public sealed class AppointmentEmailService
{
    private readonly angulosodbContext _context;
    private readonly EmailServ _emailServ;
    private readonly PatientPortalAccessService _portalAccessService;

    public AppointmentEmailService(
        angulosodbContext context,
        EmailServ emailServ,
        PatientPortalAccessService portalAccessService)
    {
        _context = context;
        _emailServ = emailServ;
        _portalAccessService = portalAccessService;
    }

    public async Task<AppointmentEmailResult> SendAppointmentEmailAsync(
        int appointmentId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _context.patient_appointments
            .Include(a => a.client)
            .Include(a => a.nutritionist)
            .SingleOrDefaultAsync(a => a.id == appointmentId, cancellationToken);

        if (appointment == null)
            return new AppointmentEmailResult(false, "No se ha encontrado la cita.");
        if (appointment.status is not ("requested" or "confirmed"))
            return new AppointmentEmailResult(false, "Solo se puede enviar el email de una cita pendiente o confirmada.");
        if (string.IsNullOrWhiteSpace(appointment.client.email))
            return new AppointmentEmailResult(false, "El paciente no tiene una dirección de email registrada.");

        // Si SMTP falla, conservamos el enlace anterior del paciente: un reenvío fallido
        // no debe invalidar el único enlace que sí pudo haber recibido.
        var previousToken = appointment.client.access_token;
        var previousExpiry = appointment.client.access_token_expires_at;
        var previousTokenVersion = appointment.client.portal_token_version;

        var endsAtUtc = DateTime.SpecifyKind(appointment.ends_at, DateTimeKind.Utc);
        var minimumExpiry = endsAtUtc.AddHours(24);
        var expiry = minimumExpiry > DateTime.UtcNow.AddHours(24)
            ? minimumExpiry
            : DateTime.UtcNow.AddHours(24);
        var accessLink = await _portalAccessService.CreateAccessLinkAsync(
            appointment.client_id,
            cancellationToken,
            expiry);

        if (string.IsNullOrWhiteSpace(accessLink))
            return new AppointmentEmailResult(false, "No se ha podido generar el enlace de acceso al portal.");

        var zone = await UserTimeZoneResolver.ForUserAsync(_context, appointment.nutritionist_id);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(appointment.starts_at, zone);
        var dateText = localStart.ToString("dddd, d 'de' MMMM 'a las' HH:mm", new CultureInfo("es-ES"));
        var safeName = System.Net.WebUtility.HtmlEncode(appointment.client.full_name ?? "Paciente");
        var safeNutritionist = System.Net.WebUtility.HtmlEncode(appointment.nutritionist.full_name ?? "tu nutricionista");
        var safeLink = System.Net.WebUtility.HtmlEncode(accessLink);
        var modalityText = appointment.modality == "online" ? "online" : "presencial";
        var statusText = appointment.status == "confirmed" ? "confirmada" : "pendiente de confirmación";
        var subject = appointment.status == "confirmed" ? "Tu cita ha sido confirmada" : "Información de tu cita";
        var videoText = appointment.modality == "online"
            ? appointment.status == "confirmed"
                ? "<p>La videollamada estará disponible desde 15 minutos antes de la hora prevista de inicio y hasta 15 minutos después de la hora prevista de finalización. La duración máxima de la consulta sigue estando limitada por las condiciones del servicio.</p>"
                : "<p>La cita es online. Cuando se confirme, podrás acceder a la videollamada desde tu portal de paciente.</p>"
            : string.Empty;

        var body = $@"<h2>Hola, {safeName}</h2>
<p>{safeNutritionist} te informa de una cita {statusText} para el {dateText}.</p>
<p>Modalidad: {modalityText}.</p>
<p><a href='{safeLink}'>Acceder al portal de paciente</a></p>
{videoText}
<p>Este enlace personal es de un solo uso y caduca, como mínimo, 24 horas después de la finalización prevista de la cita. Si vuelves a necesitar acceso, solicita a tu nutricionista que te reenvíe el email.</p>";

        BoolMensaje result;
        try
        {
            result = await _emailServ.SendEmailAsync(
                appointment.client.email,
                subject,
                body,
                idempotencyKey);
        }
        catch
        {
            appointment.client.access_token = previousToken;
            appointment.client.access_token_expires_at = previousExpiry;
            appointment.client.portal_token_version = previousTokenVersion;
            await _context.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        if (!result.Exito)
        {
            appointment.client.access_token = previousToken;
            appointment.client.access_token_expires_at = previousExpiry;
            appointment.client.portal_token_version = previousTokenVersion;
            await _context.SaveChangesAsync(cancellationToken);
            return new AppointmentEmailResult(false, "No se ha podido enviar el email. Revisa la configuración SMTP e inténtalo de nuevo.");
        }

        return new AppointmentEmailResult(true, "Email de la cita enviado al paciente.");
    }
}
