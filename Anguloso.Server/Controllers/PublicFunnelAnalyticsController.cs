using System;
using System.Threading;
using System.Threading.Tasks;
using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/public-funnel")]
public sealed class PublicFunnelAnalyticsController : ControllerBase
{
    private readonly IPublicFunnelAnalyticsService _analytics;

    public PublicFunnelAnalyticsController(IPublicFunnelAnalyticsService analytics) => _analytics = analytics;

    [AllowAnonymous]
    [HttpPost("events")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Track([FromBody] PublicFunnelEventRequest request, CancellationToken cancellationToken)
    {
        if (request.EventName is not ("directory_view" or "profile_view" or "booking_started"))
            return BadRequest(new { message = "Evento no válido." });

        await _analytics.TrackAsync(request.EventName, request.ProfessionalSlug, cancellationToken: cancellationToken);
        return NoContent();
    }

    public sealed record PublicFunnelEventRequest(string EventName, string? ProfessionalSlug);
}

[ApiController]
[Route("api/admin/public-funnel")]
[Authorize(Roles = "superadmin")]
public sealed class AdminPublicFunnelAnalyticsController : ControllerBase
{
    private readonly angulosodbContext _context;

    public AdminPublicFunnelAnalyticsController(angulosodbContext context) => _context = context;

    [HttpGet]
    public async Task<ActionResult<PublicFunnelAnalyticsDto>> Get([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? end.AddDays(-29);
        if (end < start || end.DayNumber - start.DayNumber > 365)
            return BadRequest(new { message = "El periodo no es válido." });

        var fromUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var events = await _context.public_funnel_events.AsNoTracking()
            .Where(e => e.created_at >= fromUtc && e.created_at < toUtc)
            .ToListAsync();

        var result = new PublicFunnelAnalyticsDto
        {
            From = start,
            To = end,
            DirectoryVisits = events.Count(e => e.event_name == "directory_view"),
            ProfileViews = events.Count(e => e.event_name == "profile_view"),
            BookingStarts = events.Count(e => e.event_name == "booking_started"),
            BookingRequests = events.Count(e => e.event_name == "booking_requested")
        };

        var requestedAppointmentIds = events
            .Where(e => e.event_name == "booking_requested" && e.appointment_id.HasValue)
            .Select(e => e.appointment_id!.Value)
            .Distinct()
            .ToList();

        result.CompletedConsultations = requestedAppointmentIds.Count == 0
            ? 0
            : await _context.patient_appointments.CountAsync(
                a => requestedAppointmentIds.Contains(a.id) && a.status == "completed",
                cancellationToken: default);

        return Ok(result);
    }
}

public sealed class PublicFunnelAnalyticsDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int DirectoryVisits { get; set; }
    public int ProfileViews { get; set; }
    public int BookingStarts { get; set; }
    public int BookingRequests { get; set; }
    public int CompletedConsultations { get; set; }
}
