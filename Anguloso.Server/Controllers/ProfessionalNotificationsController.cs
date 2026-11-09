using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Controllers;

/// <summary>Campana de avisos del profesional. Cada lectura/escritura queda limitada al usuario autenticado.</summary>
[ApiController]
[Route("api/professional/notifications")]
[Authorize(Policy = "Professional")]
public sealed class ProfessionalNotificationsController : ControllerBase
{
    private readonly ProfessionalNotificationService _notifications;

    public ProfessionalNotificationsController(ProfessionalNotificationService notifications) => _notifications = notifications;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 30, CancellationToken ct = default)
    {
        var userId = AuthHelpers.GetUserId(User);
        return userId.HasValue ? Ok(await _notifications.ListAsync(userId.Value, limit, ct)) : Unauthorized();
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        var userId = AuthHelpers.GetUserId(User);
        return userId.HasValue ? Ok(await _notifications.UnreadCountAsync(userId.Value, ct)) : Unauthorized();
    }

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> Read(long id, CancellationToken ct)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (!userId.HasValue) return Unauthorized();
        await _notifications.MarkReadAsync(userId.Value, id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (!userId.HasValue) return Unauthorized();
        await _notifications.MarkAllReadAsync(userId.Value, ct);
        return NoContent();
    }
}
