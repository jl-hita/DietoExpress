using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/google-calendar")]
public sealed class GoogleCalendarController : ControllerBase
{
    private readonly angulosodbContext _db;
    private readonly GoogleCalendarService _calendar;
    private readonly ILogger<GoogleCalendarController> _logger;

    public GoogleCalendarController(angulosodbContext db, GoogleCalendarService calendar, ILogger<GoogleCalendarController> logger)
    {
        _db = db;
        _calendar = calendar;
        _logger = logger;
    }

    [Authorize(Roles = "clinic_admin,nutritionist")]
    [HttpGet("status")]
    public async Task<ActionResult<GoogleCalendarService.GoogleCalendarConnectionDto>> Status(CancellationToken cancellationToken)
    {
        var (userId, tenantId) = Identity();
        if (userId == null || tenantId == null) return Unauthorized();
        return Ok(await _calendar.GetConnectionAsync(userId.Value, tenantId.Value, cancellationToken) ?? new GoogleCalendarService.GoogleCalendarConnectionDto(false, "", "primary", null));
    }

    [Authorize(Roles = "clinic_admin,nutritionist")]
    [HttpGet("connect")]
    public async Task<IActionResult> Connect(CancellationToken cancellationToken)
    {
        if (!_calendar.IsConfigured) return Problem("Google Calendar no está configurado en el servidor.", statusCode: 503);
        var (userId, tenantId) = Identity();
        if (userId == null || tenantId == null) return Unauthorized();
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        _db.google_calendar_oauth_states.Add(new google_calendar_oauth_states { user_id = userId.Value, state_hash = hash, expires_at = DateTime.UtcNow.AddMinutes(10) });
        await _db.SaveChangesAsync(cancellationToken);
        return Redirect(_calendar.BuildAuthorizationUrl(userId.Value, tenantId.Value, raw));
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            _logger.LogWarning("Google Calendar OAuth rechazado por Google: {Error}", error);
            return Redirect("/appointments?calendar=error");
        }
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return BadRequest();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        var oauth = await _db.google_calendar_oauth_states.FirstOrDefaultAsync(x => x.state_hash == hash && x.expires_at > DateTime.UtcNow, cancellationToken);
        if (oauth == null) return BadRequest("OAuth state no válido o caducado.");
        _db.google_calendar_oauth_states.Remove(oauth);
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            await _calendar.CompleteAuthorizationAsync(code, oauth, cancellationToken);
            return Redirect("/appointments?calendar=connected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completando la autorización OAuth de Google Calendar para el usuario {UserId}.", oauth.user_id);
            return Redirect("/appointments?calendar=error");
        }
    }

    [Authorize(Roles = "clinic_admin,nutritionist")]
    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        var (userId, tenantId) = Identity();
        if (userId == null || tenantId == null) return Unauthorized();
        await _calendar.DisconnectAsync(userId.Value, tenantId.Value, cancellationToken);
        return NoContent();
    }

    [Authorize(Roles = "clinic_admin,nutritionist")]
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        var (userId, tenantId) = Identity();
        if (userId == null || tenantId == null) return Unauthorized();
        await _calendar.SyncUserAsync(userId.Value, cancellationToken);
        return NoContent();
    }

    private (int?, int?) Identity()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return (null, null);
        var rawTenant = User.FindFirstValue("tenantId");
        return int.TryParse(rawTenant, out var tenantId) ? (userId, tenantId) : (userId, null);
    }
}
