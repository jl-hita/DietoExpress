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
    private readonly ITenantContextService _tenantContext;
    private readonly GoogleCalendarService _googleCalendar;

    public ProfessionalConsultationActionsController(
        angulosodbContext db,
        ITenantContextService tenantContext,
        GoogleCalendarService googleCalendar)
    {
        _db = db;
        _tenantContext = tenantContext;
        _googleCalendar = googleCalendar;
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

        var zone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Madrid");
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
                    var utc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, zone);
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
        if (request.StartsAt.Kind != DateTimeKind.Utc || request.StartsAt <= DateTime.UtcNow.AddMinutes(5))
            return BadRequest(new { message = "La fecha debe ser futura y estar expresada en UTC." });

        var endsAt = request.StartsAt.AddMinutes(request.DurationMinutes);
        if (!await IsAvailableAsync(context, request.StartsAt, endsAt))
            return Conflict(new { message = "Ese hueco ya no está disponible. Actualiza los horarios." });
        if (await _googleCalendar.IsBlockedAsync(context.NutritionistId, context.TenantId, request.StartsAt, endsAt))
            return Conflict(new { message = "Ese horario está ocupado en el calendario externo." });

        var appointment = new patient_appointments
        {
            tenant_id = context.TenantId,
            client_id = request.ClientId,
            nutritionist_id = context.NutritionistId,
            starts_at = request.StartsAt,
            ends_at = endsAt,
            status = "confirmed",
            patient_notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        _db.patient_appointments.Add(appointment);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            id = appointment.id,
            startsAt = appointment.starts_at,
            endsAt = appointment.ends_at,
            status = appointment.status,
            clientId = appointment.client_id,
            nutritionistId = appointment.nutritionist_id
        });
    }

    private async Task<bool> IsAvailableAsync(ClientContext context, DateTime startsAt, DateTime endsAt)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Madrid");
        var local = TimeZoneInfo.ConvertTimeFromUtc(startsAt, zone);
        var day = await _db.nutritionist_availability.AsNoTracking()
            .FirstOrDefaultAsync(a => a.tenant_id == context.TenantId &&
                                      a.nutritionist_id == context.NutritionistId &&
                                      a.day_of_week == (int)local.DayOfWeek &&
                                      a.is_active &&
                                      a.start_time <= TimeOnly.FromDateTime(local) &&
                                      a.end_time >= TimeOnly.FromDateTime(local.AddMinutes((endsAt - startsAt).TotalMinutes)));
        if (day == null) return false;

        return !await _db.patient_appointments.AnyAsync(a =>
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

    private async Task<bool> CanAccessClientAsync(ClientContext context)
    {
        var userId = _tenantContext.UserId;
        return User.IsInRole("clinic_admin") ||
               (userId.HasValue && userId.Value == context.NutritionistId);
    }

    private sealed record ClientContext(int TenantId, int NutritionistId);

    public sealed class ConsultationAppointmentRequest
    {
        public int ClientId { get; set; }
        public DateTime StartsAt { get; set; }
        public int DurationMinutes { get; set; } = 30;
        public string? Notes { get; set; }
    }
}
