using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/commercial-communications")]
public sealed class CommercialCommunicationController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly CommercialCommunicationService _service;

    public CommercialCommunicationController(IConfiguration configuration, CommercialCommunicationService service)
    {
        _configuration = configuration;
        _service = service;
    }

    public sealed record PreferenceRequest(bool EmailEnabled);

    [HttpGet("preferences")]
    [Authorize]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        var clientId = User.FindFirst("clientId")?.Value;
        if (!int.TryParse(clientId, out var parsedClientId) || !userId.HasValue || !User.IsInRole("patient"))
            return Forbid();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT c.id, c.tenant_id, COALESCE(p.email_enabled,false)
            FROM clients c
            LEFT JOIN patient_commercial_communication_preferences p ON p.client_id=c.id
            WHERE c.id=@client AND c.user_id=@user AND c.archived_at IS NULL
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("client", parsedClientId);
        command.Parameters.AddWithValue("user", userId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();
        return Ok(new { emailEnabled = reader.GetBoolean(2) });
    }

    [HttpPut("preferences")]
    [Authorize]
    public async Task<IActionResult> UpdatePreferences([FromBody] PreferenceRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        var clientId = User.FindFirst("clientId")?.Value;
        if (!int.TryParse(clientId, out var parsedClientId) || !userId.HasValue || !User.IsInRole("patient"))
            return Forbid();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT tenant_id
            FROM clients
            WHERE id=@client AND user_id=@user AND archived_at IS NULL
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("client", parsedClientId);
        command.Parameters.AddWithValue("user", userId.Value);
        var tenant = await command.ExecuteScalarAsync(cancellationToken);
        if (tenant is null) return NotFound();

        var version = await _service.GetCurrentConsentVersionAsync(cancellationToken);
        await _service.SetEmailPreferenceAsync(parsedClientId, Convert.ToInt32(tenant), request.EmailEnabled, "patient_portal", version, cancellationToken);
        return NoContent();
    }

    [HttpGet("unsubscribe")]
    [AllowAnonymous]
    public async Task<IActionResult> Unsubscribe([FromQuery] string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
            return BadRequest("Enlace de baja no válido.");

        var changed = await _service.UnsubscribeAsync(token, cancellationToken);
        var message = changed
            ? "La dirección ha quedado excluida de las comunicaciones comerciales por email."
            : "El enlace de baja no es válido o ya ha sido utilizado.";

        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Cache-Control"] = "no-store";
        return Content($"""
            <!doctype html>
            <html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Darse de baja</title></head>
            <body style="font-family:Arial,sans-serif;max-width:680px;margin:60px auto;padding:24px;color:#243447">
              <h1>Darse de baja</h1>
              <p>{System.Net.WebUtility.HtmlEncode(message)}</p>
              <p>Las comunicaciones asistenciales y necesarias para la prestación del servicio no se ven afectadas por esta baja.</p>
            </body></html>
            """, "text/html; charset=utf-8");
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
}
