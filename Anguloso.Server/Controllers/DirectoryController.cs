using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/directory")]
public class DirectoryController : ControllerBase
{
    private readonly angulosodbContext _context;

    public DirectoryController(angulosodbContext context) => _context = context;

    // Solo los profesionales que activan expresamente la visibilidad salen al directorio público.
    [AllowAnonymous]
    [HttpGet("professionals")]
    public async Task<ActionResult<IEnumerable<DirectoryProfileDto>>> Search([FromQuery] DirectorySearchDto filter)
    {
        var query = _context.users.AsNoTracking()
            .Where(u => u.archived_at == null && u.role != "admin" && u.role != "superadmin" && u.directory_enabled == true);

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
