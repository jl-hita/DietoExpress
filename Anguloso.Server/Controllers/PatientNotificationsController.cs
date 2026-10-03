using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Authorize(Roles = "patient")]
[Route("api/portal/notifications")]
public class PatientNotificationsController : ControllerBase
{
    private readonly NotificationService _notifications;

    public PatientNotificationsController(NotificationService notifications) => _notifications = notifications;

    // El ClientId procede del claim del paciente y se valida contra la BD antes de consultar notificaciones.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PatientNotificationDto>>> Get()
    {
        var client = await ResolvePatientAsync();
        if (client == null) return Unauthorized();
        return Ok(await _notifications.GetForPatientAsync(client.Value.TenantId, client.Value.ClientId));
    }

    [HttpPatch("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id)
    {
        var client = await ResolvePatientAsync();
        if (client == null) return Unauthorized();
        return await _notifications.MarkAsReadAsync(client.Value.TenantId, client.Value.ClientId, id) ? NoContent() : NotFound();
    }

    [HttpGet("~/api/portal/communication-preferences")]
    public async Task<IActionResult> GetCommunicationPreferences()
    {
        var client = await ResolvePatientAsync();
        if (client == null) return Unauthorized();
        return Ok(await _notifications.GetCommunicationPreferencesAsync(client.Value.TenantId, client.Value.ClientId));
    }

    [HttpPut("~/api/portal/communication-preferences")]
    public async Task<IActionResult> SetCommunicationPreferences([FromBody] PatientCommunicationPreferences request)
    {
        var client = await ResolvePatientAsync();
        if (client == null) return Unauthorized();
        await _notifications.SetCommunicationPreferencesAsync(client.Value.TenantId, client.Value.ClientId, request);
        return NoContent();
    }

    [HttpGet("~/api/portal/push/vapid-public-key")]
    public IActionResult GetVapidPublicKey()
    {
        var key = _notifications.GetVapidPublicKey();
        return key == null ? NotFound(new { message = "Las notificaciones push no están configuradas." }) : Ok(new { publicKey = key });
    }

    [HttpPost("~/api/portal/push-subscriptions")]
    public async Task<IActionResult> RegisterPush([FromBody] PushSubscriptionDto request)
    {
        var client = await ResolvePatientAsync();
        if (client == null) return Unauthorized();
        try
        {
            await _notifications.RegisterPushSubscriptionAsync(client.Value.TenantId, client.Value.ClientId, request);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("~/api/portal/push-subscriptions")]
    public async Task<IActionResult> DeletePush([FromBody] PushSubscriptionDto request)
    {
        var client = await ResolvePatientAsync();
        if (client == null) return Unauthorized();
        await _notifications.RemovePushSubscriptionAsync(client.Value.TenantId, client.Value.ClientId, request.Endpoint);
        return NoContent();
    }

    private async Task<(int TenantId, int ClientId)?> ResolvePatientAsync()
    {
        var raw = User.FindFirst("clientId")?.Value;
        if (!User.IsInRole("patient") || !int.TryParse(raw, out var clientId)) return null;

        using var scope = HttpContext.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<angulosodbContext>();
        var client = await db.clients.AsNoTracking()
            .Where(c => c.id == clientId && c.archived_at == null)
            .Select(c => new { c.id, c.tenant_id })
            .FirstOrDefaultAsync();

        return client?.tenant_id is int tenantId ? (tenantId, client.id) : null;
    }
}
