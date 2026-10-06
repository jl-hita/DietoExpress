using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "requested", "confirmed", "cancelled", "completed", "no_show" };

    private readonly angulosodbContext _context;
    private readonly EmailServ _emailServ;
    private readonly NotificationService _notifications;
    private readonly AutomationService _automationService;
    private readonly GoogleCalendarService _googleCalendar;
    private readonly AppointmentConcurrencyService _appointmentConcurrency;
    private readonly IVideoMeetingProvider _videoMeetings;

    public AppointmentsController(angulosodbContext context, EmailServ emailServ, NotificationService notifications, AutomationService automationService, GoogleCalendarService googleCalendar, AppointmentConcurrencyService appointmentConcurrency, IVideoMeetingProvider videoMeetings)
    {
        _context = context;
        _emailServ = emailServ;
        _notifications = notifications;
        _automationService = automationService;
        _googleCalendar = googleCalendar;
        _appointmentConcurrency = appointmentConcurrency;
        _videoMeetings = videoMeetings;
    }

    [Authorize(Roles = "patient")]
    [HttpGet("slots")]
    public async Task<ActionResult<IEnumerable<AppointmentSlotDto>>> GetPatientSlots([FromQuery] int days = 30)
    {
        var clientId = GetPatientClientId();
        if (clientId == null) return Unauthorized();
        days = Math.Clamp(days, 1, 60);

        var client = await _context.clients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.id == clientId.Value && c.archived_at == null);
        if (client == null) return NotFound();

        var assignment = await _context.client_nutritionist_assignments.AsNoTracking()
            .Where(a => a.client_id == client.id && a.is_active && a.nutritionist.tenant_id == client.tenant_id)
            .OrderByDescending(a => a.assigned_at)
            .Select(a => new { a.nutritionist_id, NutritionistName = a.nutritionist.full_name })
            .FirstOrDefaultAsync();
        var nutritionistId = assignment?.nutritionist_id ?? client.user_id;
        if (nutritionistId == null) return Ok(Array.Empty<AppointmentSlotDto>());

        // La disponibilidad se almacena en hora local; los huecos se generan en esa zona
        // y se convierten a UTC antes de compararlos con las citas persistidas.
        var availability = await _context.nutritionist_availability.AsNoTracking()
            .Where(a => a.tenant_id == client.tenant_id && a.nutritionist_id == nutritionistId && a.is_active)
            .ToListAsync();

        var from = DateTime.UtcNow;
        var to = from.AddDays(days);
        var appointments = await _context.patient_appointments.AsNoTracking()
            .Where(a => a.tenant_id == client.tenant_id && a.nutritionist_id == nutritionistId &&
                        a.ends_at > from && a.starts_at < to &&
                        (a.status == "requested" || a.status == "confirmed"))
            .Select(a => new { a.starts_at, a.ends_at })
            .ToListAsync();

        var slots = new List<AppointmentSlotDto>();
        var localZone = GetMadridTimeZone();
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localZone).Date;

        for (var day = localStart; day <= localStart.AddDays(days); day = day.AddDays(1))
        {
            var dayOfWeek = (int)day.DayOfWeek;
            var rules = availability.Where(a => a.day_of_week == dayOfWeek);
            foreach (var rule in rules)
            {
                for (var localTime = rule.start_time; localTime.AddMinutes(rule.slot_minutes) <= rule.end_time; localTime = localTime.AddMinutes(rule.slot_minutes))
                {
                    var localDateTime = day.Add(localTime.ToTimeSpan());
                    var utc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, localZone);
                    var endUtc = utc.AddMinutes(rule.slot_minutes);
                    if (utc <= from || utc >= to) continue;
                    if (appointments.Any(a => a.starts_at < endUtc && a.ends_at > utc)) continue;
                    slots.Add(new AppointmentSlotDto { StartsAt = utc, EndsAt = endUtc, NutritionistId = nutritionistId.Value, NutritionistName = assignment?.NutritionistName });
                }
            }
        }

        return Ok(slots.OrderBy(s => s.StartsAt).Take(200));
    }

    [Authorize(Roles = "patient")]
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<AppointmentDto>>> GetPatientAppointments()
    {
        var clientId = GetPatientClientId();
        if (clientId == null) return Unauthorized();
        var items = await _context.patient_appointments.AsNoTracking()
            .Where(a => a.client_id == clientId.Value)
            .OrderByDescending(a => a.starts_at)
            .Take(100)
            .Select(a => ToDto(a))
            .ToListAsync();
        return Ok(items);
    }

    [Authorize(Roles = "patient")]
    [HttpPost]
    public async Task<ActionResult<AppointmentDto>> RequestAppointment([FromBody] CreateAppointmentRequestDto request)
    {
        var clientId = GetPatientClientId();
        if (clientId == null) return Unauthorized();
        if (request.DurationMinutes is < 15 or > 120) return BadRequest(new { message = "La duración de la cita no es válida." });
        var requestedModality = request.Modality?.Trim().ToLowerInvariant() ?? "in_person";
        if (requestedModality is not ("in_person" or "online")) return BadRequest(new { message = "La modalidad de la cita no es válida." });
        if (request.PatientNotes?.Length > 2000) return BadRequest(new { message = "El comentario no puede superar los 2000 caracteres." });

        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == clientId.Value && c.archived_at == null);
        if (client == null) return NotFound();
        if (client.tenant_id == null) return BadRequest(new { message = "El paciente no está asociado a una clínica." });

        var assignment = await _context.client_nutritionist_assignments
            .Where(a => a.client_id == client.id && a.is_active && a.nutritionist.tenant_id == client.tenant_id)
            .OrderByDescending(a => a.assigned_at)
            .FirstOrDefaultAsync();
        var nutritionistId = assignment?.nutritionist_id ?? client.user_id;
        if (nutritionistId == null) return BadRequest(new { message = "No tienes un nutricionista asignado para reservar una cita." });

        var nutritionistBelongsToTenant = await _context.users.AsNoTracking()
            .AnyAsync(u => u.id == nutritionistId.Value && u.tenant_id == client.tenant_id);
        if (!nutritionistBelongsToTenant)
            return BadRequest(new { message = "El nutricionista asignado no está disponible." });
        if (requestedModality == "online" && !await _context.users.AsNoTracking().AnyAsync(u => u.id == nutritionistId.Value && u.tenant_id == client.tenant_id && u.online_consultations == true))
            return BadRequest(new { message = "El nutricionista no tiene habilitadas las consultas online." });

        if (request.StartsAt.Kind != DateTimeKind.Utc)
            return BadRequest(new { message = "La fecha de la cita debe incluir zona horaria." });

        var startsUtc = request.StartsAt;
        if (startsUtc <= DateTime.UtcNow.AddMinutes(5)) return BadRequest(new { message = "La cita debe ser futura." });
        var endsUtc = startsUtc.AddMinutes(request.DurationMinutes);

        // El mismo lock se usa en reservas públicas y del portal: la comprobación y el INSERT
        // quedan serializados por profesional dentro de la transacción PostgreSQL.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _appointmentConcurrency.LockAsync(client.tenant_id!.Value, nutritionistId.Value);

        // Se comprueba de nuevo el hueco justo antes de insertar: la lista mostrada al paciente
        // puede haber quedado obsoleta mientras otro usuario reservaba la misma franja.
        var validSlot = await IsAvailableSlotAsync(client.tenant_id!.Value, nutritionistId.Value, startsUtc, endsUtc);
        if (!validSlot) return Conflict(new { message = "Ese horario ya no está disponible. Actualiza la lista de citas e inténtalo de nuevo." });

        if (await _googleCalendar.IsBlockedAsync(nutritionistId.Value, client.tenant_id.Value, startsUtc, endsUtc))
            return Conflict(new { message = "Ese horario está ocupado en el calendario externo del nutricionista." });

        var overlap = await _context.patient_appointments.AnyAsync(a =>
            a.tenant_id == client.tenant_id.Value &&
            a.nutritionist_id == nutritionistId.Value &&
            (a.status == "requested" || a.status == "confirmed") &&
            a.starts_at < endsUtc && a.ends_at > startsUtc);
        if (overlap) return Conflict(new { message = "Ese horario acaba de ser reservado por otra persona." });

        var appointment = new patient_appointments
        {
            tenant_id = client.tenant_id!.Value,
            client_id = client.id,
            nutritionist_id = nutritionistId.Value,
            starts_at = startsUtc,
            ends_at = endsUtc,
            status = "requested",
            modality = requestedModality,
            patient_notes = request.PatientNotes?.Trim()
        };
        _context.patient_appointments.Add(appointment);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        await _notifications.CreateForPatientAsync(
            appointment.tenant_id,
            appointment.client_id,
            "appointment_requested",
            "Solicitud de cita enviada",
            "Tu solicitud de cita se ha enviado correctamente. Recibirás una notificación cuando tu nutricionista la confirme.",
            "/patient?tab=appointments");

        try
        {
            await _automationService.PublishEventAsync(
                appointment.tenant_id,
                "appointment.requested",
                "appointment",
                appointment.id.ToString(),
                new AutomationService.AppointmentStatusPayload(
                    appointment.id,
                    appointment.client_id,
                    appointment.nutritionist_id,
                    appointment.starts_at),
                $"appointment:{appointment.id}:requested");
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<AppointmentsController>>()
                .LogError(ex, "No se pudo registrar la automatización de solicitud de cita {AppointmentId}.", appointment.id);
        }

        return Ok(await ToDtoQuery(appointment.id));
    }

    [Authorize(Roles = "patient")]
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelPatientAppointment(int id)
    {
        var clientId = GetPatientClientId();
        if (clientId == null) return Unauthorized();
        var appointment = await _context.patient_appointments.FirstOrDefaultAsync(a => a.id == id && a.client_id == clientId.Value);
        if (appointment == null) return NotFound();
        if (appointment.status is "cancelled" or "completed" or "no_show")
            return BadRequest(new { message = "Esta cita ya no se puede cancelar." });
        if (appointment.starts_at <= DateTime.UtcNow) return BadRequest(new { message = "No puedes cancelar una cita que ya ha comenzado." });

        appointment.status = "cancelled";
        appointment.updated_at = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(appointment.starts_at, GetMadridTimeZone());
        var dateText = localStart.ToString("dddd, d 'de' MMMM 'a las' HH:mm", new System.Globalization.CultureInfo("es-ES"));
        await _notifications.CreateForPatientAsync(
            appointment.tenant_id,
            appointment.client_id,
            "appointment_cancelled",
            "Cita cancelada",
            $"Has cancelado tu cita del {dateText}.",
            "/patient?tab=appointments");

        return NoContent();
    }

    [Authorize(Roles = "clinic_admin,nutritionist")]
    [HttpGet("professional")]
    public async Task<ActionResult<IEnumerable<AppointmentDto>>> GetProfessionalAppointments([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (userId == null || tenantId == null) return Unauthorized();
        var start = from ?? DateTime.UtcNow.AddDays(-90);
        var end = to ?? DateTime.UtcNow.AddDays(60);
        if (start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc)
            return BadRequest(new { message = "El intervalo de fechas debe incluir zona horaria." });
        if (end <= start) return BadRequest(new { message = "El intervalo de fechas no es válido." });
        if ((end - start).TotalDays > 365)
            return BadRequest(new { message = "El intervalo de consulta no puede superar un año." });

        var query = _context.patient_appointments.AsNoTracking().Where(a => a.tenant_id == tenantId && a.starts_at < end && a.ends_at > start);
        if (!User.IsInRole("clinic_admin"))
            query = query.Where(a => a.nutritionist_id == userId.Value);

        return Ok(await query.OrderBy(a => a.starts_at).Take(500).Select(a => ToDto(a)).ToListAsync());
    }

    [Authorize(Roles = "clinic_admin,nutritionist")]
    [HttpGet("availability")]
    public async Task<ActionResult<IEnumerable<AvailabilityDto>>> GetAvailability()
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (userId == null || tenantId == null) return Unauthorized();
        return Ok(await _context.nutritionist_availability.AsNoTracking()
            .Where(a => a.tenant_id == tenantId && a.nutritionist_id == userId.Value)
            .OrderBy(a => a.day_of_week).ThenBy(a => a.start_time)
            .Select(a => new AvailabilityDto { Id = a.id, DayOfWeek = a.day_of_week, StartTime = a.start_time.ToString(), EndTime = a.end_time.ToString(), SlotMinutes = a.slot_minutes, IsActive = a.is_active })
            .ToListAsync());
    }

    [Authorize(Roles = "clinic_admin,nutritionist")]
    [HttpPost("availability")]
    public async Task<ActionResult<AvailabilityDto>> SaveAvailability([FromBody] SaveAvailabilityRequestDto request)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (userId == null || tenantId == null) return Unauthorized();
        if (!ValidateAvailabilityRequest(request, out var start, out var end, out var validationMessage))
            return BadRequest(new { message = validationMessage });

        // Antes de guardar el horario se comprueba el solapamiento en la misma zona de datos del profesional;
        // permitir dos reglas coincidentes produciría slots duplicados al calcular disponibilidad.
        var overlapsExisting = await _context.nutritionist_availability.AnyAsync(a =>
            a.tenant_id == tenantId.Value &&
            a.nutritionist_id == userId.Value &&
            a.day_of_week == request.DayOfWeek &&
            a.start_time < end &&
            a.end_time > start &&
            a.start_time != start);
        if (overlapsExisting)
            return Conflict(new { message = "Este horario se solapa con otro horario del mismo día." });

        var existing = await _context.nutritionist_availability.FirstOrDefaultAsync(a =>
            a.tenant_id == tenantId.Value && a.nutritionist_id == userId.Value &&
            a.day_of_week == request.DayOfWeek && a.start_time == start);

        if (existing == null)
        {
            existing = new nutritionist_availability { tenant_id = tenantId.Value, nutritionist_id = userId.Value, day_of_week = request.DayOfWeek, start_time = start };
            _context.nutritionist_availability.Add(existing);
        }

        existing.end_time = end;
        existing.slot_minutes = request.SlotMinutes;
        existing.is_active = request.IsActive;
        await _context.SaveChangesAsync();

        return Ok(new AvailabilityDto { Id = existing.id, DayOfWeek = existing.day_of_week, StartTime = existing.start_time.ToString(), EndTime = existing.end_time.ToString(), SlotMinutes = existing.slot_minutes, IsActive = existing.is_active });
    }


    [Authorize(Roles = "clinic_admin,nutritionist")]
    [HttpPut("availability/{id:int}")]
    public async Task<ActionResult<AvailabilityDto>> UpdateAvailability(int id, [FromBody] UpdateAvailabilityRequestDto request)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (userId == null || tenantId == null) return Unauthorized();
        var existing = await _context.nutritionist_availability.FirstOrDefaultAsync(a =>
            a.id == id && a.tenant_id == tenantId.Value && a.nutritionist_id == userId.Value);
        if (existing == null) return NotFound();

        if (!ValidateAvailabilityRequest(request, out var start, out var end, out var validationMessage))
            return BadRequest(new { message = validationMessage });

        if (await HasFutureReservedAppointmentsForRuleAsync(existing))
            return Conflict(new { message = "No se puede modificar este horario porque tiene citas reservadas." });

        var overlapsExisting = await _context.nutritionist_availability.AnyAsync(a =>
            a.id != id && a.tenant_id == tenantId.Value && a.nutritionist_id == userId.Value &&
            a.day_of_week == request.DayOfWeek && a.start_time < end && a.end_time > start);
        if (overlapsExisting)
            return Conflict(new { message = "Este horario se solapa con otro horario del mismo día." });

        existing.day_of_week = request.DayOfWeek;
        existing.start_time = start;
        existing.end_time = end;
        existing.slot_minutes = request.SlotMinutes;
        existing.is_active = request.IsActive;
        await _context.SaveChangesAsync();

        return Ok(ToAvailabilityDto(existing));
    }

    [Authorize(Roles = "clinic_admin,nutritionist")]
    [HttpDelete("availability/{id:int}")]
    public async Task<IActionResult> DeleteAvailability(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (userId == null || tenantId == null) return Unauthorized();

        var existing = await _context.nutritionist_availability.FirstOrDefaultAsync(a =>
            a.id == id && a.tenant_id == tenantId.Value && a.nutritionist_id == userId.Value);
        if (existing == null) return NotFound();

        if (await HasFutureReservedAppointmentsForRuleAsync(existing))
            return Conflict(new { message = "No se puede eliminar este horario porque tiene citas reservadas. Puedes pausarlo en su lugar." });

        _context.nutritionist_availability.Remove(existing);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = "clinic_admin,nutritionist")]
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<AppointmentDto>> UpdateStatus(int id, [FromBody] UpdateAppointmentStatusRequestDto request)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (userId == null || tenantId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Status) || !AllowedStatuses.Contains(request.Status))
            return BadRequest(new { message = "Estado de cita no válido." });

        var requestedStatus = request.Status.ToLowerInvariant();
        if (request.ProfessionalNotes?.Length > 4000)
            return BadRequest(new { message = "Las notas profesionales no pueden superar los 4000 caracteres." });

        var appointment = await _context.patient_appointments.Include(a => a.client).Include(a => a.nutritionist).FirstOrDefaultAsync(a => a.id == id && a.tenant_id == tenantId);
        if (appointment == null) return NotFound();
        if (!User.IsInRole("clinic_admin") && appointment.nutritionist_id != userId.Value) return Forbid();

        if (appointment.status is "cancelled" or "completed" or "no_show" &&
            requestedStatus != appointment.status)
            return BadRequest(new { message = "Una cita cerrada no puede cambiar de estado." });

        // Las citas forman una máquina de estados cerrada: impedir saltos arbitrarios evita
        // reabrir citas finalizadas o generar eventos de automatización incoherentes.
        var transitionAllowed = appointment.status switch
        {
            "requested" => requestedStatus is "requested" or "confirmed" or "cancelled",
            "confirmed" => requestedStatus is "confirmed" or "cancelled" or "completed" or "no_show",
            "cancelled" or "completed" or "no_show" => requestedStatus == appointment.status,
            _ => false
        };
        if (!transitionAllowed)
            return BadRequest(new { message = "La transición de estado de la cita no es válida." });

        var previousStatus = appointment.status;
        VideoMeetingRoom? createdVideoRoom = null;
        if (requestedStatus == "confirmed" && previousStatus != "confirmed" &&
            appointment.modality == "online" && string.IsNullOrWhiteSpace(appointment.video_room_name))
        {
            try
            {
                createdVideoRoom = await _videoMeetings.CreatePrivateRoomAsync(
                    $"appointment-{appointment.id}",
                    appointment.starts_at,
                    appointment.ends_at,
                    HttpContext.RequestAborted);

                appointment.video_provider = createdVideoRoom.Provider;
                appointment.video_room_name = createdVideoRoom.RoomName;
                appointment.video_room_url = createdVideoRoom.RoomUrl;
                appointment.video_expires_at = createdVideoRoom.ExpiresAtUtc;
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Las consultas online no están disponibles en este momento."
                });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "No se ha podido preparar la sala de la consulta online."
                });
            }
        }

        appointment.status = requestedStatus;
        appointment.professional_notes = request.ProfessionalNotes?.Trim();
        appointment.updated_at = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        if ((requestedStatus == "confirmed" && previousStatus != "confirmed") ||
            (requestedStatus == "cancelled" && previousStatus != "cancelled") ||
            (requestedStatus == "no_show" && previousStatus != "no_show"))
        {
            var eventType = requestedStatus switch
            {
                "confirmed" => "appointment.confirmed",
                "cancelled" => "appointment.cancelled",
                "no_show" => "appointment.no_show",
                _ => null
            };
            if (eventType != null)
            {
                try
                {
                    await _automationService.PublishEventAsync(
                        appointment.tenant_id,
                        eventType,
                        "appointment",
                        appointment.id.ToString(),
                        new AutomationService.AppointmentStatusPayload(
                            appointment.id,
                            appointment.client_id,
                            appointment.nutritionist_id,
                            appointment.starts_at),
                        $"appointment:{appointment.id}:{eventType}");
                }
                catch (Exception ex)
                {
                    HttpContext.RequestServices.GetRequiredService<ILogger<AppointmentsController>>()
                        .LogError(ex, "No se pudo registrar el evento de automatización {EventType} para la cita {AppointmentId}.", eventType, appointment.id);
                }
            }
        }

        if (requestedStatus == "completed" && previousStatus != "completed")
        {
            try
            {
                await _automationService.PublishEventAsync(
                    appointment.tenant_id,
                    "appointment.completed",
                    "appointment",
                    appointment.id.ToString(),
                    new AutomationService.AppointmentCompletedPayload(
                        appointment.id,
                        appointment.client_id,
                        appointment.nutritionist_id),
                    $"appointment:{appointment.id}:completed");
            }
            catch (Exception ex)
            {
                HttpContext.RequestServices.GetRequiredService<ILogger<AppointmentsController>>()
                    .LogError(ex, "No se pudo registrar la automatización de la cita completada {AppointmentId}.", appointment.id);
            }
        }

        if (requestedStatus == "cancelled" && previousStatus != "cancelled")
        {
            if (!string.IsNullOrWhiteSpace(appointment.video_room_name))
            {
                try
                {
                    await _videoMeetings.DeleteRoomAsync(appointment.video_room_name, HttpContext.RequestAborted);
                    appointment.video_room_name = null;
                    appointment.video_room_url = null;
                    appointment.video_provider = null;
                    appointment.video_expires_at = null;
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    HttpContext.RequestServices.GetRequiredService<ILogger<AppointmentsController>>()
                        .LogWarning(ex, "No se pudo eliminar la sala online de la cita {AppointmentId}.", appointment.id);
                }
            }
            var safeName = System.Net.WebUtility.HtmlEncode(appointment.client.full_name ?? "Paciente");
            var safeNutritionist = System.Net.WebUtility.HtmlEncode(appointment.nutritionist.full_name ?? "tu nutricionista");
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(appointment.starts_at, GetMadridTimeZone());
            var dateText = localStart.ToString("dddd, d 'de' MMMM 'a las' HH:mm", new System.Globalization.CultureInfo("es-ES"));

            await _notifications.CreateForPatientAsync(
                appointment.tenant_id,
                appointment.client_id,
                "appointment_cancelled",
                "Tu cita ha sido cancelada",
                $"{appointment.nutritionist.full_name ?? "Tu nutricionista"} ha cancelado la cita del {dateText}.",
                "/patient?tab=appointments");

            if (!string.IsNullOrWhiteSpace(appointment.client.email))
            {
                try
                {
                    await _emailServ.SendEmailAsync(
                        appointment.client.email,
                        "Tu cita ha sido cancelada",
                        $"<h2>Hola, {safeName}</h2><p>{safeNutritionist} ha cancelado la cita que tenías prevista para el {dateText}.</p><p>Puedes entrar en tu portal de paciente para consultar tus próximas citas y reservar otro horario disponible.</p>");
                }
                catch (Exception ex)
                {
                    HttpContext.RequestServices.GetRequiredService<ILogger<AppointmentsController>>()
                        .LogWarning(ex, "No se pudo enviar el email de cancelación de la cita {AppointmentId}.", appointment.id);
                }
            }
        }
        else if (requestedStatus == "confirmed" && previousStatus != "confirmed")
        {
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(appointment.starts_at, GetMadridTimeZone());
            var dateText = localStart.ToString("dddd, d 'de' MMMM 'a las' HH:mm", new System.Globalization.CultureInfo("es-ES"));

            await _notifications.CreateForPatientAsync(
                appointment.tenant_id,
                appointment.client_id,
                "appointment_confirmed",
                "Cita confirmada",
                $"Tu nutricionista ha confirmado tu cita del {dateText}.",
                "/patient?tab=appointments");

            if (!string.IsNullOrWhiteSpace(appointment.client.email))
            {
                try
                {
                    await _emailServ.SendEmailAsync(
                        appointment.client.email,
                        "Tu cita ha sido confirmada",
                        $"<h2>Hola, {System.Net.WebUtility.HtmlEncode(appointment.client.full_name ?? "Paciente")}</h2><p>Tu nutricionista ha confirmado la cita prevista para el {dateText}.</p><p>Puedes consultar todos los detalles desde tu portal de paciente.</p>");
                }
                catch (Exception ex)
                {
                    HttpContext.RequestServices.GetRequiredService<ILogger<AppointmentsController>>()
                        .LogWarning(ex, "No se pudo enviar el email de confirmación de la cita {AppointmentId}.", appointment.id);
                }
            }
        }

        return Ok(await ToDtoQuery(appointment.id));
    }

    [Authorize(Roles = "patient,nutritionist")]
    [HttpGet("{id:int}/video-access")]
    public async Task<IActionResult> GetVideoAccess(int id)
    {
        var appointment = await _context.patient_appointments.AsNoTracking()
            .Include(a => a.client)
            .Include(a => a.nutritionist)
            .FirstOrDefaultAsync(a => a.id == id);

        if (appointment == null) return NotFound();
        if (appointment.modality != "online" || appointment.status != "confirmed" ||
            string.IsNullOrWhiteSpace(appointment.video_room_name) ||
            string.IsNullOrWhiteSpace(appointment.video_room_url) ||
            !appointment.video_expires_at.HasValue)
            return Conflict(new { message = "La cita no dispone de una consulta online activa." });

        if (User.IsInRole("patient"))
        {
            var clientId = GetPatientClientId();
            if (clientId == null || clientId.Value != appointment.client_id)
                return NotFound();
        }
        else
        {
            var userId = AuthHelpers.GetUserId(User);
            var tenantId = AuthHelpers.GetTenantId(User);
            if (userId == null || tenantId == null || tenantId.Value != appointment.tenant_id || userId.Value != appointment.nutritionist_id)
                return Forbid();
        }

        var now = DateTime.UtcNow;
        var notBefore = appointment.starts_at.AddMinutes(-15);
        if (now < notBefore)
            return Conflict(new { message = "La sala estará disponible 15 minutos antes de la cita.", availableFrom = notBefore });
        if (now >= appointment.video_expires_at.Value)
            return Conflict(new { message = "El acceso a la sala ha expirado." });

        var isOwner = User.IsInRole("nutritionist");
        var displayName = isOwner
            ? (appointment.nutritionist.full_name ?? "Nutricionista")
            : (appointment.client.full_name ?? "Paciente");
        var token = await _videoMeetings.CreateMeetingTokenAsync(
            appointment.video_room_name,
            displayName,
            $"{(isOwner ? "nutritionist" : "patient")}:{(isOwner ? appointment.nutritionist_id : appointment.client_id)}",
            isOwner,
            notBefore,
            appointment.video_expires_at.Value,
            HttpContext.RequestAborted);

        return Ok(new
        {
            provider = appointment.video_provider,
            roomUrl = appointment.video_room_url,
            token,
            expiresAt = appointment.video_expires_at.Value
        });
    }

    private bool ValidateAvailabilityRequest(SaveAvailabilityRequestDto request, out TimeOnly start, out TimeOnly end, out string message)
    {
        start = default;
        end = default;
        message = string.Empty;
        if (request.DayOfWeek is < 0 or > 6) { message = "El día de la semana no es válido."; return false; }
        if (request.SlotMinutes is < 15 or > 240) { message = "La duración de las citas no es válida."; return false; }
        if (!TimeOnly.TryParse(request.StartTime, out start) || !TimeOnly.TryParse(request.EndTime, out end) || end <= start)
        { message = "El horario no es válido."; return false; }
        // El intervalo debe poder dividirse exactamente en slots; así la disponibilidad generada nunca deja un bloque parcial.
        var totalMinutes = (int)(end - start).TotalMinutes;
        if (totalMinutes < request.SlotMinutes || totalMinutes % request.SlotMinutes != 0)
        { message = "El horario debe contener bloques completos de la duración seleccionada."; return false; }
        return true;
    }

    private async Task<bool> HasFutureReservedAppointmentsForRuleAsync(nutritionist_availability rule)
    {
        var now = DateTime.UtcNow;
        var appointments = await _context.patient_appointments.AsNoTracking()
            .Where(a => a.tenant_id == rule.tenant_id && a.nutritionist_id == rule.nutritionist_id &&
                        a.starts_at > now && (a.status == "requested" || a.status == "confirmed"))
            .Select(a => new { a.starts_at, a.ends_at })
            .ToListAsync();
        var zone = GetMadridTimeZone();
        return appointments.Any(a =>
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(a.starts_at, zone);
            var day = (int)local.DayOfWeek;
            var time = TimeOnly.FromDateTime(local);
            return day == rule.day_of_week && time >= rule.start_time && time < rule.end_time;
        });
    }

    private static AvailabilityDto ToAvailabilityDto(nutritionist_availability a) => new()
    {
        Id = a.id, DayOfWeek = a.day_of_week, StartTime = a.start_time.ToString(),
        EndTime = a.end_time.ToString(), SlotMinutes = a.slot_minutes, IsActive = a.is_active
    };

    private int? GetPatientClientId()
    {
        if (!User.IsInRole("patient")) return null;
        var raw = User.FindFirstValue("clientId");
        return int.TryParse(raw, out var id) ? id : null;
    }

    private async Task<bool> IsAvailableSlotAsync(int tenantId, int nutritionistId, DateTime startsUtc, DateTime endsUtc)
    {
        var zone = GetMadridTimeZone();
        var local = TimeZoneInfo.ConvertTimeFromUtc(startsUtc, zone);
        var duration = (int)(endsUtc - startsUtc).TotalMinutes;
        if (duration <= 0) return false;
        var day = (int)local.DayOfWeek;
        var time = TimeOnly.FromDateTime(local);
        var rules = await _context.nutritionist_availability.AsNoTracking()
            .Where(a => a.tenant_id == tenantId && a.nutritionist_id == nutritionistId && a.is_active && a.day_of_week == day)
            .ToListAsync();
        return rules.Any(r => duration == r.slot_minutes &&
                              time >= r.start_time && time.AddMinutes(duration) <= r.end_time &&
                              ((time.ToTimeSpan() - r.start_time.ToTimeSpan()).TotalMinutes % r.slot_minutes) == 0);
    }

    private async Task<AppointmentDto> ToDtoQuery(int id) =>
        await _context.patient_appointments.AsNoTracking().Where(a => a.id == id).Select(a => ToDto(a)).FirstAsync();

    private static AppointmentDto ToDto(patient_appointments a) => new()
    {
        Id = a.id, StartsAt = a.starts_at, EndsAt = a.ends_at, Status = a.status,
        PatientNotes = a.patient_notes, ProfessionalNotes = a.professional_notes,
        ClientId = a.client_id, ClientName = a.client.full_name,
        NutritionistId = a.nutritionist_id, NutritionistName = a.nutritionist.full_name,
        Modality = a.modality, VideoProvider = a.video_provider, VideoRoomUrl = a.video_room_url, VideoExpiresAt = a.video_expires_at
    };

    private static TimeZoneInfo GetMadridTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time"); }
    }
}
