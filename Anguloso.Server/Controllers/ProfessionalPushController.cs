using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/professional/push")]
[Authorize(Roles = "nutritionist,clinic_admin")]
public sealed class ProfessionalPushController : ControllerBase
{
    private readonly ProfessionalPushService _push;
    private readonly angulosodbContext _db;

    public ProfessionalPushController(ProfessionalPushService push, angulosodbContext db) { _push = push; _db = db; }

    [HttpGet("vapid-public-key")]
    public IActionResult GetPublicKey()
    {
        var key = _push.GetVapidPublicKey();
        return key is null ? NotFound(new { message = "Las notificaciones push no están configuradas." }) : Ok(new { publicKey = key });
    }

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Register([FromBody] PushSubscriptionDto request, CancellationToken ct)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        var userId = AuthHelpers.GetUserId(User);
        if (!tenantId.HasValue || !userId.HasValue) return Unauthorized();
        var exists = await _db.users.AsNoTracking().AnyAsync(u => u.id == userId.Value && u.tenant_id == tenantId.Value && (u.role == "nutritionist" || u.role == "clinic_admin"), ct);
        if (!exists) return Forbid();
        try { await _push.RegisterAsync(tenantId.Value, userId.Value, request, ct); return NoContent(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("subscriptions")]
    public async Task<IActionResult> Delete([FromBody] PushSubscriptionDto request, CancellationToken ct)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        var userId = AuthHelpers.GetUserId(User);
        if (!tenantId.HasValue || !userId.HasValue) return Unauthorized();
        await _push.RemoveAsync(tenantId.Value, userId.Value, request.Endpoint, ct);
        return NoContent();
    }
}
