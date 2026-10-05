using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using Anguloso.Server.Logica;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/directory")]
public class DirectoryController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly ILicenseService _licenseService;
    private readonly AppointmentConcurrencyService _appointmentConcurrency;
    private readonly AutomationService _automationService;
    private readonly GoogleCalendarService _googleCalendar;
    private readonly EmailServ _emailServ;
    private readonly PatientPortalAccessService _portalAccessService;
    private readonly ILogger<DirectoryController> _logger;

    public DirectoryController(
        angulosodbContext context,
        ILicenseService licenseService,
        AppointmentConcurrencyService appointmentConcurrency,
        AutomationService automationService,
        GoogleCalendarService googleCalendar,
        EmailServ emailServ,
        PatientPortalAccessService portalAccessService,
        ILogger<DirectoryController> logger)
    {
        _context = context;
        _licenseService = licenseService;
        _appointmentConcurrency = appointmentConcurrency;
        _automationService = automationService;
        _googleCalendar = googleCalendar;
        _emailServ = emailServ;
        _portalAccessService = portalAccessService;
        _logger = logger;
    }

    // Solo los profesionales que activan expresamente la visibilidad salen al directorio público.
    [AllowAnonymous]
    [HttpGet("professionals")]
    public async Task<ActionResult<IEnumerable<DirectoryProfileDto>>> Search([FromQuery] DirectorySearchDto filter)
    {
        var query = _context.users.AsNoTracking()
            .Where(u => u.archived_at == null && u.role == "nutritionist" && u.directory_enabled == true);

        if (!string.IsNullOrWhiteSpace(filter.City))
            query = query.Where(u => u.directory_city != null && EF.Functions.ILike(u.directory_city, $"%{filter.City.Trim()}%"));
        if (filter.Online == true)
            query = query.Where(u => u.online_consultations == true);
        if (!string.IsNullOrWhiteSpace(filter.Speciality))
            query = query.Where(u => u.directory_specialties != null && EF.Functions.ILike(u.directory_specialties, $"%{filter.Speciality.Trim()}%"));

        return Ok(await query.OrderBy(u => u.directory_city).ThenBy(u => u.full_name).Take(100)
            .Select(u => new DirectoryProfileDto
            {
                Username = u.username,
                Slug = u.directory_slug ?? string.Empty,
                FullName = u.full_name ?? string.Empty,
                ClinicName = u.clinic_name ?? string.Empty,
                City = u.directory_city ?? string.Empty,
                ClinicLogo = u.clinic_logo ?? string.Empty,
                PublicBio = u.directory_bio ?? string.Empty,
                Specialties = u.directory_specialties ?? string.Empty,
                OnlineConsultations = u.online_consultations ?? false
            }).ToListAsync());
    }

    [AllowAnonymous]
    [HttpGet("professionals/{slug}")]
    public async Task<ActionResult<DirectoryProfileDto>> GetBySlug(string slug)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var profile = await _context.users.AsNoTracking()
            .Where(u => u.archived_at == null && u.directory_enabled == true && u.directory_slug == normalized)
            .Select(u => new DirectoryProfileDto
            {
                Username = u.username,
                Slug = u.directory_slug ?? string.Empty,
                FullName = u.full_name ?? string.Empty,
                ClinicName = u.clinic_name ?? string.Empty,
                // La ficha pública no expone la dirección exacta de la consulta.
                City = u.directory_city ?? string.Empty,
                ClinicLogo = u.clinic_logo ?? string.Empty,
                PublicBio = u.directory_bio ?? string.Empty,
                Specialties = u.directory_specialties ?? string.Empty,
                OnlineConsultations = u.online_consultations ?? false
            }).FirstOrDefaultAsync();

        return profile == null ? NotFound() : Ok(profile);
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("professionals/{slug}/appointments")]
    public async Task<ActionResult<PublicAppointmentConfirmationDto>> RequestPublicAppointment(
        string slug,
        [FromBody] PublicAppointmentRequestDto request)
    {
        // El endpoint es anónimo, por lo que todo el contexto de seguridad se reconstruye
        // desde el perfil público: nunca aceptamos tenantId o nutritionistId enviados por el navegador.
        var normalized = slug.Trim().ToLowerInvariant();
        var fullName = request.FullName.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var phone = request.Phone?.Trim() ?? string.Empty;
        var notes = request.PatientNotes?.Trim();

        if (fullName.Length is < 2 or > 200)
            return BadRequest(new { message = "El nombre debe tener entre 2 y 200 caracteres." });
        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { message = "El email no es válido." });
        if (phone.Length > 50)
            return BadRequest(new { message = "El teléfono no puede superar los 50 caracteres." });
        if (notes?.Length > 500)
            return BadRequest(new { message = "El comentario no puede superar los 500 caracteres." });
        if (request.DurationMinutes is < 15 or > 120)
            return BadRequest(new { message = "La duración de la cita no es válida." });

        var professional = await _context.users.AsNoTracking()
            .Where(u => u.archived_at == null &&
                        u.directory_enabled == true &&
                        u.directory_slug == normalized)
            .Select(u => new { u.id, u.tenant_id, u.full_name })
            .FirstOrDefaultAsync();

        if (professional == null || !professional.tenant_id.HasValue)
            return NotFound();

        if (request.StartsAt.Kind != DateTimeKind.Utc)
            return BadRequest(new { message = "La fecha de la cita debe incluir zona horaria." });

        var startsUtc = request.StartsAt;
        var endsUtc = startsUtc.AddMinutes(request.DurationMinutes);
        if (startsUtc <= DateTime.UtcNow.AddMinutes(5))
            return BadRequest(new { message = "La cita debe ser futura." });

        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _appointmentConcurrency.LockAsync(professional.tenant_id.Value, professional.id);

        var local = TimeZoneInfo.ConvertTimeFromUtc(startsUtc, GetMadridTimeZone());
        var day = (int)local.DayOfWeek;
        var time = TimeOnly.FromDateTime(local);
        var validRule = await _context.nutritionist_availability.AsNoTracking()
            .Where(a => a.tenant_id == professional.tenant_id.Value &&
                        a.nutritionist_id == professional.id &&
                        a.is_active &&
                        a.day_of_week == day)
            .ToListAsync();

        if (!validRule.Any(r => r.slot_minutes == request.DurationMinutes &&
                                time >= r.start_time &&
                                time.AddMinutes(request.DurationMinutes) <= r.end_time &&
                                ((time.ToTimeSpan() - r.start_time.ToTimeSpan()).TotalMinutes % r.slot_minutes) == 0))
            return Conflict(new { message = "Ese horario ya no está disponible." });

        var overlap = await _context.patient_appointments.AnyAsync(a =>
            a.tenant_id == professional.tenant_id.Value &&
            a.nutritionist_id == professional.id &&
            (a.status == "requested" || a.status == "confirmed") &&
            a.starts_at < endsUtc && a.ends_at > startsUtc);
        if (overlap)
            return Conflict(new { message = "Ese horario ya no está disponible." });

        // Limitamos las reservas públicas pendientes/futuras por email para evitar que una misma
        // persona bloquee una agenda con decenas de solicitudes. El límite es por tenant, no por IP:
        // el rate limiting ya cubre abuso automatizado y esta segunda barrera cubre intentos distribuidos.
        const int maxActivePublicAppointmentsPerEmail = 3;
        var activePublicAppointments = await _context.patient_appointments
            .AsNoTracking()
            .Where(a => a.tenant_id == professional.tenant_id.Value &&
                        (a.status == "requested" || a.status == "confirmed") &&
                        a.starts_at > DateTime.UtcNow &&
                        a.client.email != null &&
                        a.client.email.ToLower() == email)
            .CountAsync();
        if (activePublicAppointments >= maxActivePublicAppointmentsPerEmail)
            return Conflict(new { message = "Has alcanzado el límite de reservas futuras. Si necesitas otra cita, contacta con el profesional." });

        // La disponibilidad también puede estar bloqueada en el calendario externo del profesional.
        // Se comprueba bajo el mismo lock antes de crear la cita para no presentar como libre
        // un hueco que el nutricionista tenga ocupado fuera de DietoExpress.
        if (await _googleCalendar.IsBlockedAsync(professional.id, professional.tenant_id.Value, startsUtc, endsUtc))
            return Conflict(new { message = "Ese horario está ocupado en el calendario externo del nutricionista." });

        var existing = await _context.clients
            .FirstOrDefaultAsync(c => c.tenant_id == professional.tenant_id.Value &&
                                      c.archived_at == null &&
                                      c.email.ToLower() == email);

        clients client;
        if (existing != null)
        {
            var activeAssignment = await _context.client_nutritionist_assignments
                .FirstOrDefaultAsync(a => a.client_id == existing.id && a.is_active);
            if (activeAssignment != null && activeAssignment.nutritionist_id != professional.id)
                return Conflict(new { message = "Ya existe un paciente con esos datos asociado a otro profesional de esta clínica." });

            client = existing;
            if (activeAssignment == null)
            {
                var assignmentCheck = await _licenseService.CanAssignClientAsync(
                    professional.tenant_id.Value, professional.id, client.id);
                if (!assignmentCheck.Allowed)
                    return BadRequest(assignmentCheck.Reason);

                // Un cliente ya existente puede llegar desde una baja o desasignación.
                // Actualizar el profesional propietario mantiene coherentes los límites
                // de licencia con la asignación que acabamos de crear.
                client.user_id = professional.id;
                _context.client_nutritionist_assignments.Add(new client_nutritionist_assignments
                {
                    client_id = client.id,
                    nutritionist_id = professional.id,
                    assigned_by_user_id = professional.id,
                    assigned_at = DateTime.UtcNow,
                    is_active = true
                });
            }
        }
        else
        {
            var licenseCheck = await _licenseService.CanCreateClientAsync(
                professional.tenant_id.Value, professional.id);
            if (!licenseCheck.Allowed)
                return BadRequest(licenseCheck.Reason);

            client = new clients
            {
                user_id = professional.id,
                tenant_id = professional.tenant_id.Value,
                full_name = fullName,
                email = email,
                phone = phone,
                gender = string.Empty,
                notes = string.Empty,
                created_at = DateTime.UtcNow,
                lifecycle_status = "pending_info"
            };
            _context.clients.Add(client);
            await _context.SaveChangesAsync();

            _context.client_nutritionist_assignments.Add(new client_nutritionist_assignments
            {
                client_id = client.id,
                nutritionist_id = professional.id,
                assigned_by_user_id = professional.id,
                assigned_at = DateTime.UtcNow,
                is_active = true
            });
        }

        var appointment = new patient_appointments
        {
            tenant_id = professional.tenant_id.Value,
            client_id = client.id,
            nutritionist_id = professional.id,
            starts_at = startsUtc,
            ends_at = endsUtc,
            status = "requested",
            patient_notes = notes
        };
        _context.patient_appointments.Add(appointment);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        // La reserva pública convierte al visitante en paciente operativo. La provisión documental se
        // ejecuta como trabajo persistente después del commit: sobrevive a reinicios y dispone de reintentos
        // con backoff, de modo que un fallo de disco/almacenamiento no deje el alta en un estado irrecuperable.
        try
        {
            await _automationService.ScheduleActionAsync(
                appointment.tenant_id,
                "provision_patient_documents",
                new AutomationService.ProvisionPatientDocumentsAction(
                    appointment.client_id,
                    professional.id,
                    ForClientCreation: true,
                    IncludeAllRequired: false),
                DateTime.UtcNow,
                null,
                $"documents:provision:{appointment.tenant_id}:{appointment.client_id}:creation",
                maxAttempts: 8,
                // La reserva ya está confirmada en PostgreSQL: la provisión es trabajo durable y no debe
                // perderse si el navegador cancela la petición justo después del commit.
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            // La reserva ya está confirmada; registramos la incidencia sin convertir un fallo
            // operativo del sistema documental en un fallo de la reserva.
            _logger.LogError(ex, "No se pudo programar la provisión documental del paciente {ClientId}.", appointment.client_id);
        }

        // Un paciente nuevo necesita una puerta de entrada al portal para completar la documentación
        // provisionada por la reserva. No regeneramos tokens de pacientes ya operativos para no invalidar
        // sesiones/enlaces existentes: en ese caso sigue disponible el flujo normal de acceso.
        if (client.passcode_hash == null && client.access_token == null && !string.IsNullOrWhiteSpace(client.email))
        {
            try
            {
                var accessLink = await _portalAccessService.CreateAccessLinkAsync(client.id, CancellationToken.None);
                if (accessLink != null)
                {
                    var safeName = System.Net.WebUtility.HtmlEncode(client.full_name ?? "Paciente");
                    var safeLink = System.Net.WebUtility.HtmlEncode(accessLink);
                    await _emailServ.SendEmailAsync(
                        client.email!,
                        "Tu reserva en DietoExpress",
                        $"<h2>Hola, {safeName}</h2>" +
                        $"<p>Hemos recibido tu solicitud de cita con {System.Net.WebUtility.HtmlEncode(professional.full_name ?? "tu nutricionista")}.</p>" +
                        $"<p>Antes de la consulta puedes completar tus datos y la documentación desde tu portal:</p>" +
                        $"<p><a href='{safeLink}'>Acceder a mi portal</a></p>" +
                        "<p>Este enlace caduca en 24 horas y solo puede utilizarse una vez.</p>");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo enviar el acceso inicial al portal para el paciente {ClientId}.", client.id);
            }
        }

        try
        {
            await _automationService.PublishEventAsync(
                appointment.tenant_id,
                "appointment.requested",
                "appointment",
                appointment.id.ToString(),
                new AutomationService.AppointmentStatusPayload(
                    appointment.id, appointment.client_id, appointment.nutritionist_id, appointment.starts_at),
                $"appointment:{appointment.id}:requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo registrar la automatización de reserva pública {AppointmentId}.", appointment.id);
        }

        return Ok(new PublicAppointmentConfirmationDto
        {
            StartsAt = appointment.starts_at,
            EndsAt = appointment.ends_at,
            Status = appointment.status,
            NutritionistName = professional.full_name ?? string.Empty
        });
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpGet("professionals/{slug}/availability")]
    public async Task<ActionResult<IEnumerable<AppointmentSlotDto>>> GetPublicAvailability(
        string slug,
        [FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 60);
        var normalized = slug.Trim().ToLowerInvariant();

        var professional = await _context.users.AsNoTracking()
            .Where(u => u.archived_at == null &&
                        u.directory_enabled == true &&
                        u.directory_slug == normalized)
            .Select(u => new { u.id, u.tenant_id, u.full_name })
            .FirstOrDefaultAsync();

        if (professional == null || !professional.tenant_id.HasValue)
            return NotFound();

        var availability = await _context.nutritionist_availability.AsNoTracking()
            .Where(a => a.tenant_id == professional.tenant_id.Value &&
                        a.nutritionist_id == professional.id &&
                        a.is_active)
            .ToListAsync();

        var from = DateTime.UtcNow;
        var to = from.AddDays(days);
        var appointments = await _context.patient_appointments.AsNoTracking()
            .Where(a => a.tenant_id == professional.tenant_id.Value &&
                        a.nutritionist_id == professional.id &&
                        a.ends_at > from &&
                        a.starts_at < to &&
                        (a.status == "requested" || a.status == "confirmed"))
            .Select(a => new { a.starts_at, a.ends_at })
            .ToListAsync();

        var localZone = GetMadridTimeZone();
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(from, localZone).Date;
        var slots = new List<AppointmentSlotDto>();

        // La disponibilidad pública reutiliza las mismas reglas horarias de la agenda.
        // No se expone información de pacientes ni de citas ocupadas: solo huecos libres.
        for (var day = localStart; day <= localStart.AddDays(days); day = day.AddDays(1))
        {
            var dayOfWeek = (int)day.DayOfWeek;
            foreach (var rule in availability.Where(a => a.day_of_week == dayOfWeek))
            {
                for (var localTime = rule.start_time;
                     localTime.AddMinutes(rule.slot_minutes) <= rule.end_time;
                     localTime = localTime.AddMinutes(rule.slot_minutes))
                {
                    var localDateTime = day.Add(localTime.ToTimeSpan());
                    var utc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, localZone);
                    var endUtc = utc.AddMinutes(rule.slot_minutes);

                    if (utc <= from || utc >= to)
                        continue;
                    if (appointments.Any(a => a.starts_at < endUtc && a.ends_at > utc))
                        continue;
                    if (await _googleCalendar.IsBlockedAsync(professional.id, professional.tenant_id.Value, utc, endUtc))
                        continue;

                    slots.Add(new AppointmentSlotDto
                    {
                        StartsAt = utc,
                        EndsAt = endUtc,
                        NutritionistId = professional.id,
                        NutritionistName = professional.full_name
                    });
                }
            }
        }

        return Ok(slots.OrderBy(s => s.StartsAt).Take(200));
    }

    private static TimeZoneInfo GetMadridTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time"); }
    }

}
