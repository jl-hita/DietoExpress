using System;
using System.Threading;
using System.Threading.Tasks;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

public interface IPublicFunnelAnalyticsService
{
    Task TrackAsync(string eventName, string? slug = null, int? appointmentId = null, CancellationToken cancellationToken = default);
}

public sealed class PublicFunnelAnalyticsService : IPublicFunnelAnalyticsService
{
    private readonly angulosodbContext _context;

    public PublicFunnelAnalyticsService(angulosodbContext context) => _context = context;

    public async Task TrackAsync(string eventName, string? slug = null, int? appointmentId = null, CancellationToken cancellationToken = default)
    {
        if (eventName is not ("directory_view" or "profile_view" or "booking_started" or "booking_requested"))
            return;

        if (slug?.Length > 120)
            slug = slug[..120];

        _context.public_funnel_events.Add(new public_funnel_events
        {
            event_name = eventName,
            professional_slug = string.IsNullOrWhiteSpace(slug) ? null : slug.Trim().ToLowerInvariant(),
            appointment_id = appointmentId,
            created_at = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}
