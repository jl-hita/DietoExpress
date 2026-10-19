using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/professional/consultation-actions")]
[Authorize(Policy = "Professional")]
public sealed class ProfessionalConsultationActionsController : ControllerBase
{
    private readonly angulosodbContext _db;
    private readonly EmailServ _emailServ;
    private readonly ITenantContextService _tenantContext;
    private readonly GoogleCalendarService _googleCalendar;
    private readonly AutomationService _automation;
    private readonly IVideoMeetingProvider _videoMeetings;

    public ProfessionalConsultationActionsController(
        angulosodbContext db,
        EmailServ emailServ,
        ITenantContextService tenantContext,
        GoogleCalendarService googleCalendar,
        AutomationService automation,
        IVideoMeetingProvider videoMeetings)
    {
        _db = db;
        _emailServ = emailServ;
        _tenantContext = tenantContext;
        _googleCalendar = googleCalendar;
        _automation = automation;
        _videoMeetings = videoMeetings;
    }

    [HttpGet("slots")]
    public async Task<IActionResult> GetSlots(
        [FromQuery] int clientId,
        [FromQuery] int durationMinutes = 30,
        [FromQuery] int days = 30)
    {
        var context = await ResolveClientAsync(clientId);
        if (context == null) return NotFound();
        if (!await CanAccessClientAsync(context)) return Forbid();
        if (durationMinutes is < 15 or > 120) return BadRequest(new { message = "La duración debe estar entre 15 y 120 minutos." });

        days = Math.Clamp(days, 1, 60);
        var from = DateTime.UtcNow;
        var to = from.AddDays(days);

        var availability = await _db.nutritionist_availability.AsNoTracking()
            .Where(a => a.tenant_id == context.TenantId && a.nutritionist_id == context.NutritionistId && a.is_active)
            .ToListAsync();

        var appointments = await _db.patient_appointments.AsNoTracking()
            .Where(a => a.tenant_id == context.TenantId && a.nutritionist_id == context.NutritionistId &&
                        a.ends_at > from && a.starts_at < to &&
                        (a.status == "requested" || a.status == "confirmed"))
            .Select(a => new { a.starts_at, a.ends_at })
            .ToListAsync();

        var zone = await UserTimeZoneResolver.ForUserAsync(_db, context.NutritionistId);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(from, zone).Date;
        var slots = new List<object>();

        for (var day = localStart; day <= localStart.AddDays(days); day = day.AddDays(1))
        {
            var dayOfWeek = (int)day.DayOfWeek;
            foreach (var rule in availability.Where(a => a.day_of_week == dayOfWeek))
            {
                for (var localTime = rule.start_time;
                     localTime.AddMinutes(durationMinutes) <= rule.end_time;
                     localTime = localTime.AddMinutes(rule.slot_minutes))
                {
                    var localDateTime = day.Add(localTime.ToTimeSpan());
                    if (!UserTimeZoneResolver.TryConvertLocalToUtc(localDateTime, zone, out var utc)) continue;
                    var endUtc = utc.AddMinutes(durationMinutes);
                    if (utc <= from.AddMinutes(5) || utc >= to) continue;
                    if (appointments.Any(a => a.starts_at < endUtc && a.ends_at > utc)) continue;
                    if (await _googleCalendar.IsBlockedAsync(context.NutritionistId, context.TenantId, utc, endUtc)) continue;

                    slots.Add(new { startsAt = utc, endsAt = endUtc, nutritionistId = context.NutritionistId });
                    if (slots.Count >= 30) return Ok(slots);
                }
            }
        }

        return Ok(slots);
    }

    [HttpPost("appointment")]
    public async Task<IActionResult> CreateAppointment([FromBody] ConsultationAppointmentRequest request)
    {
        if (request == null) return BadRequest();
        var context = await ResolveClientAsync(request.ClientId);
        if (context == null) return NotFound();
        if (!await CanAccessClientAsync(context)) return Forbid();
        if (request.DurationMinutes is < 15 or > 120) return BadRequest(new { message = "La duración no es válida." });
        var modality = request.Modality?.Trim().ToLowerInvariant() ?? "in_person";
        if (modality is not ("in_person" or "online")) return BadRequest(new { message = "La modalidad no es válida." });
        if (modality == "online" && !await _db.users.AsNoTracking().AnyAsync(u => u.id == context.NutritionistId && u.tenant_id == context.TenantId && u.online_consultations == true))
            return BadRequest(new { message = "El profesional no tiene habilitadas las consultas online." });
        if (request.StartsAt.Kind != DateTimeKind.Utc || request.StartsAt <= DateTime.UtcNow.AddMinutes(5))
            return BadRequest(new { message = "La fecha debe ser futura y estar expresada en UTC." });

        var endsAt = request.StartsAt.AddMinutes(request.DurationMinutes);
        // La disponibilidad semanal limita las reservas públicas, no la agenda privada del profesional.
        // Las citas manuales pueden pactarse fuera de horario, pero nunca solaparse con otra cita.
        if (await HasAppointmentOverlapAsync(context, request.StartsAt, endsAt))
            return Conflict(new { message = "Ese horario ya está ocupado por otra cita." });
        if (await _googleCalendar.IsBlockedAsync(context.NutritionistId, context.TenantId, request.StartsAt, endsAt))
            return Conflict(new { message = "Ese horario está ocupado en el calendario externo." });

        VideoMeetingRoom? videoRoom = null;
        if (modality == "online")
        {
            try
            {
                videoRoom = await _videoMeetings.CreatePrivateRoomAsync(
                    $"appointment-pending-{Guid.NewGuid():N}",
                    request.StartsAt,
                    endsAt,
                    HttpContext.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                HttpContext.RequestServices.GetRequiredService<ILogger<ProfessionalConsultationActionsController>>()
                    .LogError(ex, "El proveedor de vídeo no está disponible al crear una cita online para el cliente {ClientId} del tenant {TenantId}.", request.ClientId, context.TenantId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Las consultas online no están disponibles en este momento. Puedes elegir modalidad presencial o intentarlo más tarde.", code = "VIDEO_PROVIDER_UNAVAILABLE", reportable = true });
            }
            catch (HttpRequestException ex)
            {
                HttpContext.RequestServices.GetRequiredService<ILogger<ProfessionalConsultationActionsController>>()
                    .LogError(ex, "Falló la petición al proveedor de vídeo al crear una cita online para el cliente {ClientId} del tenant {TenantId}.", request.ClientId, context.TenantId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "No se ha podido preparar la sala de la consulta online. Puedes elegir modalidad presencial o intentarlo más tarde.", code = "VIDEO_PROVIDER_UNAVAILABLE", reportable = true });
            }
        }

        var appointment = new patient_appointments
        {
            tenant_id = context.TenantId,
            client_id = request.ClientId,
            nutritionist_id = context.NutritionistId,
            starts_at = request.StartsAt,
            ends_at = endsAt,
            status = "confirmed",
            modality = modality,
            video_provider = videoRoom?.Provider,
            video_room_name = videoRoom?.RoomName,
            video_room_url = videoRoom?.RoomUrl,
            video_expires_at = videoRoom?.ExpiresAtUtc,
            patient_notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        _db.patient_appointments.Add(appointment);
        await _db.SaveChangesAsync();

        try
        {
            await _automation.PublishEventAsync(
                appointment.tenant_id,
                "appointment.confirmed",
                "appointment",
                appointment.id.ToString(),
                new AutomationService.AppointmentStatusPayload(appointment.id, appointment.client_id, appointment.nutritionist_id, appointment.starts_at, appointment.modality),
                $"appointment:{appointment.id}:confirmed");
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<ProfessionalConsultationActionsController>>()
                .LogError(ex, "No se pudo registrar la automatización de confirmación de la cita {AppointmentId}.", appointment.id);
        }

        // Las citas creadas manualmente nacen confirmadas; no pasan por UpdateStatus,
        // así que enviamos aquí el mismo aviso por email que recibe una cita confirmada.
        var patient = await _db.clients.AsNoTracking()
            .Where(c => c.id == appointment.client_id && c.tenant_id == appointment.tenant_id)
            .Select(c => new { c.email, c.full_name })
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(patient?.email))
        {
            try
            {
                var zone = await UserTimeZoneResolver.ForUserAsync(_db, appointment.nutritionist_id);
                var localStart = TimeZoneInfo.ConvertTimeFromUtc(appointment.starts_at, zone);
                var dateText = localStart.ToString("dddd, d 'de' MMMM 'a las' HH:mm", new System.Globalization.CultureInfo("es-ES"));
                var safeName = System.Net.WebUtility.HtmlEncode(patient.full_name ?? "Paciente");
                var safeNutritionist = System.Net.WebUtility.HtmlEncode(
                    await _db.users.AsNoTracking()
                        .Where(u => u.id == appointment.nutritionist_id && u.tenant_id == appointment.tenant_id)
                        .Select(u => u.full_name)
                        .FirstOrDefaultAsync() ?? "tu nutricionista");
                var modalityText = modality == "online" ? "online" : "presencial";
                var emailResult = await _emailServ.SendEmailAsync(
                    patient.email,
                    "Tu cita ha sido confirmada",
                    $"<h2>Hola, {safeName}</h2><p>{safeNutritionist} ha agendado y confirmado tu cita para el {dateText}.</p><p>Modalidad: {modalityText}.</p><p>Puedes consultar los detalles desde tu portal de paciente.</p>",
                    $"appointment:{appointment.id}:confirmed");
                if (!emailResult.Exito)
                {
                    HttpContext.RequestServices.GetRequiredService<ILogger<ProfessionalConsultationActionsController>>()
                        .LogWarning("No se pudo enviar el email de confirmación de la cita manual {AppointmentId} al paciente {ClientId}.", appointment.id, appointment.client_id);
                }
            }
            catch (Exception ex)
            {
                HttpContext.RequestServices.GetRequiredService<ILogger<ProfessionalConsultationActionsController>>()
                    .LogWarning(ex, "No se pudo enviar el email de confirmación de la cita manual {AppointmentId} al paciente {ClientId}.", appointment.id, appointment.client_id);
            }
        }

        return Ok(new
        {
            id = appointment.id,
            startsAt = appointment.starts_at,
            endsAt = appointment.ends_at,
            status = appointment.status,
            clientId = appointment.client_id,
            nutritionistId = appointment.nutritionist_id,
            modality = appointment.modality,
            videoProvider = appointment.video_provider,
            videoRoomUrl = appointment.video_room_url,
            videoExpiresAt = appointment.video_expires_at
        });
    }

    private Task<bool> HasAppointmentOverlapAsync(ClientContext context, DateTime startsAt, DateTime endsAt)
    {
        return _db.patient_appointments.AsNoTracking().AnyAsync(a =>
            a.tenant_id == context.TenantId && a.nutritionist_id == context.NutritionistId &&
            (a.status == "requested" || a.status == "confirmed") &&
            a.starts_at < endsAt && a.ends_at > startsAt);
    }

    private async Task<ClientContext?> ResolveClientAsync(int clientId)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue) return null;
        var client = await _db.clients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.id == clientId && c.tenant_id == tenantId.Value && c.archived_at == null);
        if (client == null) return null;

        var assigned = await _db.client_nutritionist_assignments.AsNoTracking()
            .Where(a => a.client_id == clientId && a.is_active && a.nutritionist.tenant_id == tenantId.Value)
            .OrderByDescending(a => a.assigned_at)
            .Select(a => (int?)a.nutritionist_id)
            .FirstOrDefaultAsync();

        var nutritionistId = assigned ?? client.user_id;
        return nutritionistId.HasValue ? new ClientContext(tenantId.Value, nutritionistId.Value) : null;
    }

    private Task<bool> CanAccessClientAsync(ClientContext context)
    {
        var userId = _tenantContext.UserId;
        return Task.FromResult(
            User.IsInRole("clinic_admin") ||
            (userId.HasValue && userId.Value == context.NutritionistId));
    }

    private sealed record ClientContext(int TenantId, int NutritionistId);

    public sealed class ConsultationAppointmentRequest
    {
        public int ClientId { get; set; }
        public DateTime StartsAt { get; set; }
        public int DurationMinutes { get; set; } = 30;
        public string? Notes { get; set; }
        public string Modality { get; set; } = "in_person";
    }
}
