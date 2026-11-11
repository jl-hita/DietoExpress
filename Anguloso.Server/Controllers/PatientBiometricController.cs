using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Acceso biométrico del paciente en la app móvil. El paciente registra una clave pública de su dispositivo estando
/// autenticado; después puede iniciar sesión firmando un desafío de un solo uso con esa clave, que el móvil solo
/// libera tras huella o rostro. El PIN y el enlace de email siguen siendo los métodos de acceso de respaldo.
/// </summary>
[Route("api/portal/biometric")]
[ApiController]
public class PatientBiometricController : ControllerBase
{
    private const int MaxActiveDevicesPerPatient = 5;
    private const string LoginFailure = "No se ha podido verificar el acceso biométrico. Entra con tu PIN o con un enlace de acceso.";

    private readonly angulosodbContext _context;
    private readonly PatientBiometricCrypto _crypto;
    private readonly PatientSessionService _sessions;
    private readonly ILicenseService _licenseService;
    private readonly EmailServ _email;

    public PatientBiometricController(angulosodbContext context, PatientBiometricCrypto crypto, PatientSessionService sessions, ILicenseService licenseService, EmailServ email)
    {
        _context = context;
        _crypto = crypto;
        _sessions = sessions;
        _licenseService = licenseService;
        _email = email;
    }

    public sealed class RegisterRequest
    {
        public string? PublicKey { get; set; }
        public string? DeviceName { get; set; }
    }

    public sealed class ChallengeRequest { public string? DeviceId { get; set; } }

    public sealed class LoginRequest
    {
        public string? DeviceId { get; set; }
        public string? Challenge { get; set; }
        public string? Signature { get; set; }
    }

    public sealed class DeviceRow
    {
        public string Id { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
    }

    public sealed class DeviceKeyRow
    {
        public int ClientId { get; set; }
        public string PublicKey { get; set; } = string.Empty;
    }

    /// <summary>Registra la clave pública del dispositivo para la sesión de paciente actual.</summary>
    [HttpPost("register")]
    [Authorize(Roles = "patient")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var clientId = AuthHelpers.GetClientId(User);
        if (!clientId.HasValue) return Unauthorized();
        if (!PatientBiometricCrypto.TryParsePublicKey(request?.PublicKey, out _))
            return BadRequest("La clave del dispositivo no es válida.");

        var client = await _context.clients.AsNoTracking().FirstOrDefaultAsync(c => c.id == clientId.Value && c.archived_at == null);
        if (client == null) return Unauthorized();

        var name = string.IsNullOrWhiteSpace(request!.DeviceName) ? "Dispositivo móvil" : request.DeviceName.Trim();
        if (name.Length > 100) name = name[..100];

        var active = await _context.Database.SqlQuery<int>(
            $"SELECT COUNT(*)::int AS \"Value\" FROM patient_biometric_devices WHERE client_id = {client.id} AND revoked_at IS NULL").SingleAsync();
        if (active >= MaxActiveDevicesPerPatient)
            return Conflict($"Has alcanzado el máximo de {MaxActiveDevicesPerPatient} dispositivos. Quita alguno antes de añadir otro.");

        var deviceId = Guid.NewGuid().ToString("N");
        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO patient_biometric_devices (id, client_id, public_key, device_name)
            VALUES ({deviceId}, {client.id}, {request.PublicKey}, {name})
            """);

        // Quien tenga una sesión robada podría registrar su propio dispositivo: el paciente debe enterarse.
        if (!string.IsNullOrWhiteSpace(client.email))
        {
            try
            {
                await _email.SendEmailAsync(client.email, "Acceso biométrico activado en DietoExpress",
                    $"<p>Hola {System.Net.WebUtility.HtmlEncode(client.full_name ?? "Paciente")},</p><p>Se ha activado el acceso con huella o rostro en un dispositivo ({System.Net.WebUtility.HtmlEncode(name)}). Si no has sido tú, ábrelo en la app, ve a Seguridad y quita el dispositivo, o contacta con tu profesional.</p>");
            }
            catch
            {
                // La notificación es informativa: no debe impedir el registro.
            }
        }

        return Ok(new { deviceId });
    }

    [HttpGet("devices")]
    [Authorize(Roles = "patient")]
    public async Task<IActionResult> ListDevices()
    {
        var clientId = AuthHelpers.GetClientId(User);
        if (!clientId.HasValue) return Unauthorized();
        var devices = await _context.Database.SqlQuery<DeviceRow>($"""
            SELECT id AS "Id", device_name AS "DeviceName", created_at AS "CreatedAt", last_used_at AS "LastUsedAt"
            FROM patient_biometric_devices
            WHERE client_id = {clientId.Value} AND revoked_at IS NULL
            ORDER BY created_at DESC
            """).ToListAsync();
        return Ok(devices);
    }

    [HttpDelete("devices/{deviceId}")]
    [Authorize(Roles = "patient")]
    public async Task<IActionResult> RevokeDevice(string deviceId)
    {
        var clientId = AuthHelpers.GetClientId(User);
        if (!clientId.HasValue) return Unauthorized();
        if (deviceId.Length > 64) return NotFound();
        var revoked = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE patient_biometric_devices SET revoked_at = NOW()
            WHERE id = {deviceId} AND client_id = {clientId.Value} AND revoked_at IS NULL
            """);
        return revoked == 1 ? NoContent() : NotFound();
    }

    /// <summary>
    /// Primer paso del acceso: devuelve un desafío. Responde igual para dispositivos desconocidos para no revelar
    /// qué identificadores existen.
    /// </summary>
    [HttpPost("challenge")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public IActionResult Challenge([FromBody] ChallengeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.DeviceId) || request.DeviceId.Length > 64 || request.DeviceId.Contains('.'))
            return BadRequest("Dispositivo no válido.");
        return Ok(new { challenge = _crypto.CreateChallenge(request.DeviceId, DateTimeOffset.UtcNow, out _) });
    }

    /// <summary>Segundo paso: comprueba la firma del desafío y emite la misma sesión que el acceso con PIN.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 64)
            return Unauthorized(LoginFailure);
        if (!_crypto.TryReadChallenge(request.Challenge, DateTimeOffset.UtcNow, out var challengeDevice, out var issuedAt) ||
            !string.Equals(challengeDevice, request.DeviceId, StringComparison.Ordinal))
            return Unauthorized(LoginFailure);

        var device = await _context.Database.SqlQuery<DeviceKeyRow>($"""
            SELECT client_id AS "ClientId", public_key AS "PublicKey"
            FROM patient_biometric_devices
            WHERE id = {request.DeviceId} AND revoked_at IS NULL
            """).FirstOrDefaultAsync();
        if (device == null || !PatientBiometricCrypto.VerifySignature(device.PublicKey, request.DeviceId, request.Challenge!, request.Signature))
            return Unauthorized(LoginFailure);

        // Cada desafío sirve una sola vez: solo se acepta uno emitido después del último usado por este dispositivo.
        var consumed = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE patient_biometric_devices
            SET last_challenge_issued_at = {issuedAt}, last_used_at = NOW()
            WHERE id = {request.DeviceId} AND revoked_at IS NULL AND last_challenge_issued_at < {issuedAt}
            """);
        if (consumed != 1) return Unauthorized(LoginFailure);

        var client = await _context.clients.Include(c => c.user).FirstOrDefaultAsync(c => c.id == device.ClientId && c.archived_at == null);
        if (client == null) return Unauthorized(LoginFailure);

        var tenantId = client.tenant_id ?? client.user?.tenant_id;
        if (!await _licenseService.CanUseFeatureAsync(tenantId, "CLIENT_PORTAL")) return Forbid();

        client.last_portal_access = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        _sessions.Issue(Response, client);

        return Ok(new PatientAuthResponseDto
        {
            Token = null,
            ClientId = client.id,
            FullName = client.full_name,
            ClinicName = client.user?.clinic_name,
            ClinicLogo = client.user?.clinic_logo
        });
    }
}
