using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
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
    private readonly IPublicFunnelAnalyticsService _funnelAnalytics;

    public DirectoryController(
        angulosodbContext context,
        ILicenseService licenseService,
        AppointmentConcurrencyService appointmentConcurrency,
        AutomationService automationService,
        GoogleCalendarService googleCalendar,
        EmailServ emailServ,
        PatientPortalAccessService portalAccessService,
        ILogger<DirectoryController> logger,
        IPublicFunnelAnalyticsService funnelAnalytics)
    {
        _context = context;
        _licenseService = licenseService;
        _appointmentConcurrency = appointmentConcurrency;
        _automationService = automationService;
        _googleCalendar = googleCalendar;
        _emailServ = emailServ;
        _portalAccessService = portalAccessService;
        _logger = logger;
        _funnelAnalytics = funnelAnalytics;
    }

    // Solo los profesionales que activan expresamente la visibilidad salen al directorio público.
    [AllowAnonymous]
    [HttpGet("professionals")]
    public async Task<ActionResult<IEnumerable<DirectoryProfileDto>>> Search([FromQuery] DirectorySearchDto filter)
    {
        var query = _context.users.AsNoTracking()
            .Where(u => u.archived_at == null && u.role == "nutritionist" && u.directory_enabled == true && (u.directory_publication_status == "published" || u.directory_publication_status == "verified"));

        if (!string.IsNullOrWhiteSpace(filter.City))
            query = query.Where(u => u.directory_city != null && EF.Functions.ILike(u.directory_city, $"%{filter.City.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(filter.Province))
        {
            var provincePattern = $"%{filter.Province.Trim()}%";
            query = query.Where(u =>
                u.directory_province != null &&
                EF.Functions.ILike(
                    EF.Functions.Unaccent(u.directory_province),
                    EF.Functions.Unaccent(provincePattern)));
        }
        if (filter.Online == true)
            query = query.Where(u => u.online_consultations == true);
        if (!string.IsNullOrWhiteSpace(filter.Speciality))
            query = query.Where(u => u.directory_specialties != null && EF.Functions.ILike(u.directory_specialties, $"%{filter.Speciality.Trim()}%"));

        if (!string.IsNullOrWhiteSpace(filter.Goal))
        {
            var goal = filter.Goal.Trim().ToLowerInvariant();
            var goalTerms = goal switch
            {
                "pérdida de peso" or "perdida de peso" => new[] { "pérdida de peso", "perdida de peso", "obesidad", "control de peso" },
                "nutrición deportiva" or "nutricion deportiva" => new[] { "nutrición deportiva", "nutricion deportiva", "deporte" },
                "obesidad" => new[] { "obesidad", "pérdida de peso", "perdida de peso" },
                _ => new[] { goal }
            };
            query = query.Where(u => u.directory_specialties != null &&
                goalTerms.Any(term => EF.Functions.ILike(u.directory_specialties, $"%{term}%")));
        }

        if (filter.AvailableOnly == true)
            query = query.Where(u => _context.nutritionist_availability.Any(a => a.nutritionist_id == u.id && a.is_active));

        var profiles = await query
            .OrderBy(u => u.directory_city)
            .ThenBy(u => u.full_name)
             .Take(200)
            .Select(u => new DirectoryProfileDto
            {
                NutritionistId = u.id,
                Username = u.username,
                Slug = u.directory_slug ?? string.Empty,
                FullName = u.full_name ?? string.Empty,
                ClinicName = u.clinic_name ?? string.Empty,
                City = u.directory_city ?? string.Empty,
                Province = u.directory_province ?? string.Empty,
                ClinicLogo = u.clinic_logo ?? string.Empty,
                PublicBio = u.directory_bio ?? string.Empty,
                Specialties = u.directory_specialties ?? string.Empty,
                OnlineConsultations = u.online_consultations ?? false,
                OffersSportsTraining = u.directory_sports_training ?? false,
                IsVerified = u.directory_publication_status == "verified"
            }).ToListAsync();

        var ids = profiles.Select(p => p.NutritionistId).ToArray();
        var tenantByNutritionist = await _context.users.AsNoTracking()
            .Where(u => ids.Contains(u.id))
            .Select(u => new { u.id, u.tenant_id })
            .ToDictionaryAsync(u => u.id, u => u.tenant_id);
        var activeLicenseByTenant = new Dictionary<int, bool>();
        foreach (var tenantId in tenantByNutritionist.Values.Where(id => id.HasValue).Select(id => id!.Value).Distinct())
            activeLicenseByTenant[tenantId] = await HasActiveLicenseAsync(tenantId);

        var availabilityCounts = await _context.nutritionist_availability.AsNoTracking()
            .Where(a => ids.Contains(a.nutritionist_id) && a.is_active)
            .GroupBy(a => a.nutritionist_id)
            .Select(g => new { NutritionistId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.NutritionistId, x => x.Count);

        var reviewSummaries = ids.Length == 0
            ? new Dictionary<int, (double Average, int Count)>()
            : (await _context.Database.SqlQueryRaw<DirectoryReviewAggregate>(
                @"SELECT nutritionist_id AS ""NutritionistId"",
                         COALESCE(AVG(rating), 0)::double precision AS ""AverageRating"",
                         COUNT(*)::integer AS ""ReviewCount""
                  FROM public_directory_reviews
                  WHERE nutritionist_id = ANY({0})
                  GROUP BY nutritionist_id", ids).ToListAsync())
              .ToDictionary(x => x.NutritionistId, x => (x.AverageRating, x.ReviewCount));

        foreach (var profile in profiles)
        {
            var tenantId = tenantByNutritionist.GetValueOrDefault(profile.NutritionistId);
            profile.CanAcceptAppointments = tenantId.HasValue && activeLicenseByTenant.GetValueOrDefault(tenantId.Value);
            profile.SpecialtyList = SplitSpecialties(profile.Specialties);
            var review = reviewSummaries.GetValueOrDefault(profile.NutritionistId);
            profile.AverageRating = Math.Round(review.Item1, 1);
            profile.ReviewCount = review.Item2;
            profile.AvailableRuleCount = availabilityCounts.GetValueOrDefault(profile.NutritionistId);
            profile.ProfileCompleteness = CalculateProfileCompleteness(profile);
            profile.RankingScore = CalculateRankingScore(profile, filter);
        }

        var visibleProfiles = filter.AvailableOnly == true
            ? profiles.Where(p => p.CanAcceptAppointments && p.AvailableRuleCount > 0)
            : profiles;

        return Ok(visibleProfiles
            .OrderByDescending(p => p.RankingScore)
            .ThenBy(p => p.FullName)
            .ToList());
    }

    [AllowAnonymous]
    [HttpGet("professionals/{slug}")]
    public async Task<ActionResult<DirectoryProfileDto>> GetBySlug(string slug)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var profile = await _context.users.AsNoTracking()
            .Where(u => u.archived_at == null && u.role == "nutritionist" && u.directory_enabled == true && (u.directory_publication_status == "published" || u.directory_publication_status == "verified") && u.directory_slug == normalized)
            .Select(u => new DirectoryProfileDto
            {
                NutritionistId = u.id,
                Username = u.username,
                Slug = u.directory_slug ?? string.Empty,
                FullName = u.full_name ?? string.Empty,
                ClinicName = u.clinic_name ?? string.Empty,
                // La ficha pública no expone la dirección exacta de la consulta.
                City = u.directory_city ?? string.Empty,
                Province = u.directory_province ?? string.Empty,
                ClinicLogo = u.clinic_logo ?? string.Empty,
                PublicBio = u.directory_bio ?? string.Empty,
                Specialties = u.directory_specialties ?? string.Empty,
                OnlineConsultations = u.online_consultations ?? false,
                OffersSportsTraining = u.directory_sports_training ?? false,
                IsVerified = u.directory_publication_status == "verified"
            }).FirstOrDefaultAsync();

        if (profile != null)
        {
            var tenantId = await _context.users.AsNoTracking().Where(u => u.id == profile.NutritionistId).Select(u => u.tenant_id).FirstOrDefaultAsync();
            profile.CanAcceptAppointments = await HasActiveLicenseAsync(tenantId);
        }

        if (profile == null)
            return NotFound();

        profile.SpecialtyList = SplitSpecialties(profile.Specialties);
        profile.ProfileCompleteness = CalculateProfileCompleteness(profile);
        profile.AvailableRuleCount = await _context.nutritionist_availability.AsNoTracking().CountAsync(a => a.nutritionist_id == profile.NutritionistId && a.is_active);
        var reviewSummary = await _context.Database.SqlQueryRaw<DirectoryReviewSummaryRow>(
            @"SELECT COALESCE(AVG(rating), 0)::double precision AS ""AverageRating"",
                     COUNT(*)::integer AS ""ReviewCount""
              FROM public_directory_reviews
              WHERE nutritionist_id = {0}", profile.NutritionistId).SingleAsync();
        profile.AverageRating = Math.Round(reviewSummary.AverageRating, 1);
        profile.ReviewCount = reviewSummary.ReviewCount;
        profile.RankingScore = profile.ProfileCompleteness + Math.Min(profile.AvailableRuleCount * 2, 20)
            + Math.Min(profile.ReviewCount * 2, 10)
            + (profile.AverageRating >= 4.5 ? 20 : profile.AverageRating >= 4.0 ? 10 : 0);
        return Ok(profile);
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("professionals/{slug}/interest")]
    public async Task<IActionResult> SubmitInterest(string slug, [FromBody] PublicDirectoryInterestRequestDto request)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var fullName = request.FullName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var phone = request.Phone?.Trim();

        if (fullName.Length is < 2 or > 200)
            return BadRequest(new { message = "El nombre debe tener entre 2 y 200 caracteres." });
        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { message = "El email no es válido." });
        if (phone?.Length > 50)
            return BadRequest(new { message = "El teléfono no puede superar los 50 caracteres." });
        if (!request.ConsentToContact)
            return BadRequest(new { message = "Debes aceptar el tratamiento de tus datos para enviar la solicitud de interés." });

        var professional = await _context.users.AsNoTracking()
            .Where(u => u.archived_at == null && u.role == "nutritionist" &&
                        u.directory_enabled == true &&
                        (u.directory_publication_status == "published" || u.directory_publication_status == "verified") &&
                        u.directory_slug == normalized)
            .Select(u => new { u.id, u.tenant_id, u.email, u.full_name })
            .FirstOrDefaultAsync();

        if (professional == null || !professional.tenant_id.HasValue) return NotFound();
        if (await HasActiveLicenseAsync(professional.tenant_id))
            return Conflict(new { message = "Este profesional ya puede recibir solicitudes de cita. Actualiza el perfil e inténtalo de nuevo." });

        await _context.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO public_directory_interests
                (tenant_id, nutritionist_id, full_name, email, phone)
            VALUES
                ({professional.tenant_id.Value}, {professional.id}, {fullName}, {email}, {phone})");

        // El aviso informa de que existe interés, sin incluir datos personales mientras la licencia siga inactiva.
        if (!string.IsNullOrWhiteSpace(professional.email))
        {
            try
            {
                await _emailServ.SendEmailAsync(
                    professional.email,
                    "Nueva persona interesada en tu perfil de DietoExpress",
                    "<p>Una persona ha mostrado interés en tu perfil público de DietoExpress.</p>" +
                    "<p>Por protección de datos, sus datos de contacto permanecerán bloqueados mientras tu licencia no esté activa.</p>" +
                    "<p>Cuando reactives tu licencia podrás revisar y gestionar las solicitudes pendientes desde DietoExpress.</p>");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo enviar el aviso de nueva solicitud de interés al nutricionista {NutritionistId}.", professional.id);
            }
        }

        return Ok(new { received = true, message = "Hemos registrado tu interés. Este profesional no puede aceptar citas ahora mismo; no se ha reservado ningún horario." });
    }

    [Authorize(Policy = "Professional")]
    [HttpGet("my/interests")]
    public async Task<IActionResult> GetMyInterests()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var professional = await _context.users.AsNoTracking()
            .Where(u => u.id == userId && u.role == "nutritionist" && u.archived_at == null)
            .Select(u => new { u.id, u.tenant_id })
            .FirstOrDefaultAsync();
        if (professional == null || !professional.tenant_id.HasValue) return Forbid();

        var interestCount = await _context.Database.SqlQueryRaw<long>(
            @"SELECT COUNT(*) AS ""Value"" FROM public_directory_interests
              WHERE tenant_id = {0} AND nutritionist_id = {1}",
            professional.tenant_id.Value, professional.id).SingleAsync();

        if (!await HasActiveLicenseAsync(professional.tenant_id))
            return Ok(new
            {
                canViewDetails = false,
                count = interestCount,
                message = "Tienes personas interesadas en tu perfil. Sus datos personales seguirán protegidos hasta que tu licencia esté activa."
            });

        var interests = await _context.Database.SqlQueryRaw<PublicDirectoryInterestRow>(
            @"SELECT id AS ""Id"", full_name AS ""FullName"", email AS ""Email"",
                     phone AS ""Phone"", created_at AS ""CreatedAt"",
                     viewed_at AS ""ViewedAt""
              FROM public_directory_interests
              WHERE tenant_id = {0} AND nutritionist_id = {1}
              ORDER BY created_at DESC
              LIMIT 100", professional.tenant_id.Value, professional.id).ToListAsync();

        await _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE public_directory_interests SET viewed_at = NOW()
            WHERE tenant_id = {professional.tenant_id.Value} AND nutritionist_id = {professional.id}
              AND viewed_at IS NULL");

        return Ok(new { canViewDetails = true, count = interestCount, interests });
    }

    private async Task<bool> HasActiveLicenseAsync(int? tenantId)
    {
        if (!tenantId.HasValue) return false;
        var license = await _licenseService.GetLicenseAsync(tenantId);
        return license != null && string.Equals(license.Status, "active", StringComparison.OrdinalIgnoreCase)
            && (!license.ExpiresAt.HasValue || license.ExpiresAt.Value > DateTime.UtcNow);
    }

    private sealed class PublicDirectoryInterestRow
    {
        public long Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ViewedAt { get; set; }
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
        var requestedModality = request.Modality?.Trim().ToLowerInvariant() ?? "in_person";
        if (requestedModality is not ("in_person" or "online"))
            return BadRequest(new { message = "La modalidad de la cita no es válida." });

        var professional = await _context.users.AsNoTracking()
            .Where(u => u.archived_at == null &&
                        u.directory_enabled == true &&
                        (u.directory_publication_status == "published" || u.directory_publication_status == "verified") &&
                        u.directory_slug == normalized)
            .Select(u => new { u.id, u.tenant_id, u.full_name, u.online_consultations })
            .FirstOrDefaultAsync();

        if (professional == null || !professional.tenant_id.HasValue)
            return NotFound();
        if (!await HasActiveLicenseAsync(professional.tenant_id))
            return Conflict(new { message = "Este profesional no puede aceptar citas en este momento. Puedes enviar una solicitud de interés desde su perfil." });
        if (requestedModality == "online" && professional.online_consultations != true)
            return BadRequest(new { message = "Este profesional no tiene habilitadas las consultas online." });

        if (request.StartsAt.Kind != DateTimeKind.Utc)
            return BadRequest(new { message = "La fecha de la cita debe incluir zona horaria." });

        var startsUtc = request.StartsAt;
        var endsUtc = startsUtc.AddMinutes(request.DurationMinutes);
        if (startsUtc <= DateTime.UtcNow.AddMinutes(5))
            return BadRequest(new { message = "La cita debe ser futura." });

        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _appointmentConcurrency.LockAsync(professional.tenant_id.Value, professional.id);

        var professionalZone = await UserTimeZoneResolver.ForUserAsync(_context, professional.id);
        var local = TimeZoneInfo.ConvertTimeFromUtc(startsUtc, professionalZone);
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
                return BadRequest(new { message = licenseCheck.Reason ?? "No se puede crear el paciente con la licencia actual." });

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

        // Un cliente solo se considera establecido si ya tiene una cita marcada como completada.
        // Tener ficha o una reserva previa pendiente/cancelada no basta para saltarse la aceptación.
        var isEstablishedClient = await _context.patient_appointments.AsNoTracking().AnyAsync(a =>
            a.tenant_id == professional.tenant_id.Value &&
            a.client_id == client.id &&
            a.status == "completed");

        var appointment = new patient_appointments
        {
            tenant_id = professional.tenant_id.Value,
            client_id = client.id,
            nutritionist_id = professional.id,
            starts_at = startsUtc,
            ends_at = endsUtc,
            status = isEstablishedClient ? "confirmed" : "requested",
            modality = requestedModality,
            patient_notes = notes
        };

        // La confirmación automática debe tener las mismas garantías que una confirmación manual:
        // si es online, preparar la sala privada antes de confirmar la cita.
        if (isEstablishedClient && requestedModality == "online")
        {
            try
            {
                var videoRoom = await _videoMeetings.CreatePrivateRoomAsync(
                    $"appointment-{appointment.id}",
                    appointment.starts_at,
                    appointment.ends_at,
                    HttpContext.RequestAborted);
                appointment.video_provider = videoRoom.Provider;
                appointment.video_room_name = videoRoom.RoomName;
                appointment.video_room_url = videoRoom.RoomUrl;
                appointment.video_expires_at = videoRoom.ExpiresAtUtc;
            }
            catch (InvalidOperationException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Las consultas online no están disponibles en este momento.",
                    code = "VIDEO_PROVIDER_UNAVAILABLE",
                    reportable = true
                });
            }
            catch (HttpRequestException)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "No se ha podido preparar la sala de la consulta online.",
                    code = "VIDEO_PROVIDER_UNAVAILABLE",
                    reportable = true
                });
            }
        }

        _context.patient_appointments.Add(appointment);
        try
        {
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(appointment.video_room_name))
            {
                try
                {
                    await _videoMeetings.DeleteRoomAsync(appointment.video_room_name, CancellationToken.None);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "No se pudo eliminar la sala de vídeo huérfana de la cita pública {AppointmentId}.", appointment.id);
                }
            }
            throw;
        }

        try
        {
            await _funnelAnalytics.TrackAsync("booking_requested", normalized, appointment.id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo registrar la analítica de reserva pública {AppointmentId}.", appointment.id);
        }

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
                        "<p>Este enlace es personal, caduca en 24 horas y puedes utilizarlo tantas veces como necesites durante ese plazo.</p>");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo enviar el acceso inicial al portal para el paciente {ClientId}.", client.id);
            }
        }

        var appointmentEvent = isEstablishedClient ? "appointment.confirmed" : "appointment.requested";
        try
        {
            await _automationService.PublishEventAsync(
                appointment.tenant_id,
                appointmentEvent,
                "appointment",
                appointment.id.ToString(),
                new AutomationService.AppointmentStatusPayload(
                    appointment.id, appointment.client_id, appointment.nutritionist_id, appointment.starts_at, appointment.modality),
                $"appointment:{appointment.id}:{appointmentEvent}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo registrar la automatización {EventType} de reserva pública {AppointmentId}.", appointmentEvent, appointment.id);
        }

        return Ok(new PublicAppointmentConfirmationDto
        {
            StartsAt = appointment.starts_at,
            EndsAt = appointment.ends_at,
            Status = appointment.status,
            NutritionistName = professional.full_name ?? string.Empty,
            Modality = appointment.modality
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
                        (u.directory_publication_status == "published" || u.directory_publication_status == "verified") &&
                        u.directory_slug == normalized)
            .Select(u => new { u.id, u.tenant_id, u.full_name })
            .FirstOrDefaultAsync();

        if (professional == null || !professional.tenant_id.HasValue)
            return NotFound();
        if (!await HasActiveLicenseAsync(professional.tenant_id))
            return Ok(Array.Empty<AppointmentSlotDto>());

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

        var localZone = await UserTimeZoneResolver.ForUserAsync(_context, professional.id);
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
                    if (!UserTimeZoneResolver.TryConvertLocalToUtc(localDateTime, localZone, out var utc)) continue;
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

    [AllowAnonymous]
    [HttpGet("professionals/{slug}/reviews")]
    public async Task<ActionResult<DirectoryReviewSummaryDto>> GetReviews(string slug)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var professional = await _context.users.AsNoTracking()
            .Where(u => u.archived_at == null && u.role == "nutritionist" &&
                        u.directory_enabled == true && (u.directory_publication_status == "published" || u.directory_publication_status == "verified") &&
                        u.directory_slug == normalized)
            .Select(u => u.id)
            .FirstOrDefaultAsync();
        if (professional == 0) return NotFound();

        var rows = await _context.Database.SqlQueryRaw<DirectoryReviewRow>(
            @"SELECT rating AS ""Rating"", comment AS ""Comment"", created_at AS ""CreatedAt""
              FROM public_directory_reviews
              WHERE nutritionist_id = {0}
              ORDER BY created_at DESC
              LIMIT 20", professional).ToListAsync();

        var summary = await _context.Database.SqlQueryRaw<DirectoryReviewSummaryRow>(
            @"SELECT COALESCE(AVG(rating), 0)::double precision AS ""AverageRating"",
                     COUNT(*)::integer AS ""ReviewCount""
              FROM public_directory_reviews
              WHERE nutritionist_id = {0}", professional).SingleAsync();

        return Ok(new DirectoryReviewSummaryDto
        {
            AverageRating = Math.Round(summary.AverageRating, 1),
            ReviewCount = summary.ReviewCount,
            Reviews = rows.Select(r => new DirectoryReviewDto
            {
                Rating = r.Rating,
                Comment = r.Comment ?? string.Empty,
                CreatedAt = r.CreatedAt,
                Verified = true
            }).ToList()
        });
    }

    private sealed class DirectoryReviewAggregate
    {
        public int NutritionistId { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }

    private sealed class DirectoryReviewRow
    {
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class DirectoryReviewSummaryRow
    {
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }

    private static string[] SplitSpecialties(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static int CalculateProfileCompleteness(DirectoryProfileDto profile)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(profile.FullName)) score += 15;
        if (!string.IsNullOrWhiteSpace(profile.City)) score += 15;
        if (!string.IsNullOrWhiteSpace(profile.Province)) score += 5;
        if (!string.IsNullOrWhiteSpace(profile.PublicBio)) score += 25;
        if (profile.SpecialtyList.Length > 0) score += 20;
        if (!string.IsNullOrWhiteSpace(profile.ClinicName)) score += 5;
        if (!string.IsNullOrWhiteSpace(profile.ClinicLogo)) score += 5;
        if (profile.OnlineConsultations) score += 10;
        return score;
    }

    private static int CalculateRankingScore(DirectoryProfileDto profile, DirectorySearchDto filter)
    {
        var score = profile.ProfileCompleteness;
        var city = filter.City?.Trim();
        var speciality = filter.Speciality?.Trim();

        if (!string.IsNullOrWhiteSpace(city))
        {
            if (string.Equals(profile.City, city, StringComparison.OrdinalIgnoreCase)) score += 80;
            else if (profile.City.Contains(city, StringComparison.OrdinalIgnoreCase)) score += 35;
        }

        if (!string.IsNullOrWhiteSpace(speciality))
        {
            if (profile.SpecialtyList.Any(x => string.Equals(x, speciality, StringComparison.OrdinalIgnoreCase))) score += 70;
            else if (profile.SpecialtyList.Any(x => x.Contains(speciality, StringComparison.OrdinalIgnoreCase))) score += 30;
        }

        if (filter.Online == true && profile.OnlineConsultations) score += 35;
        if (!string.IsNullOrWhiteSpace(filter.Goal) && profile.SpecialtyList.Any(x => x.Contains(filter.Goal.Trim(), StringComparison.OrdinalIgnoreCase))) score += 60;
        if (filter.AvailableOnly == true && profile.AvailableRuleCount > 0) score += 40;
        score += Math.Min(profile.AvailableRuleCount * 2, 20);
        if (profile.IsVerified) score += 20;
        if (profile.ReviewCount > 0) score += Math.Min(profile.ReviewCount * 2, 10);
        if (profile.AverageRating >= 4.5) score += 20;
        else if (profile.AverageRating >= 4.0) score += 10;
        return score;
    }


}
