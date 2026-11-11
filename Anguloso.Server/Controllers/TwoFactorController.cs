using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Gestión de la verificación en dos pasos (TOTP) del profesional autenticado. El inicio de sesión con segundo factor
/// vive en <c>AuthController</c> (POST api/auth/login/2fa).
/// </summary>
[Route("api/auth/2fa")]
[ApiController]
[Authorize(Policy = "Professional")]
[EnableRateLimiting("auth")]
public class TwoFactorController : ControllerBase
{
    private const string Issuer = "DietoExpress";
    private readonly angulosodbContext _context;
    private readonly AccountSecurityService _security;
    private readonly TwoFactorCrypto _crypto;
    private readonly IAuditLogService _audit;
    private readonly EmailServ _email;

    public TwoFactorController(angulosodbContext context, AccountSecurityService security, TwoFactorCrypto crypto, IAuditLogService audit, EmailServ email)
    {
        _context = context;
        _security = security;
        _crypto = crypto;
        _audit = audit;
        _email = email;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();
        var remaining = user.totp_enabled ? await _security.CountRemainingRecoveryCodesAsync(user.id) : 0;
        return Ok(new { enabled = user.totp_enabled, enabledAt = user.totp_enabled_at, recoveryCodesRemaining = remaining, requiresPassword = RequiresPassword(user) });
    }

    /// <summary>Genera un secreto nuevo (aún inactivo) y devuelve el QR y la clave manual para la app autenticadora.</summary>
    [HttpPost("setup")]
    public async Task<IActionResult> Setup()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();
        if (user.totp_enabled) return Conflict("La verificación en dos pasos ya está activada. Desactívala antes de configurarla de nuevo.");

        var secret = TotpService.GenerateSecret();
        user.totp_secret_encrypted = _crypto.ProtectSecret(user.id, secret);
        user.totp_last_step = null;
        await _context.SaveChangesAsync();

        var uri = TotpService.BuildOtpAuthUri(Issuer, string.IsNullOrWhiteSpace(user.email) ? user.username : user.email, secret);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(5);

        return Ok(new { secret = TotpService.ToBase32(secret), otpauthUri = uri, qrSvg = svg });
    }

    public sealed class CodeRequest { public string? Code { get; set; } }

    public sealed class DisableRequest
    {
        public string? Password { get; set; }
        public string? Code { get; set; }
    }

    /// <summary>Confirma que la app autenticadora genera códigos válidos y activa el 2FA. Devuelve los códigos de recuperación una única vez.</summary>
    [HttpPost("enable")]
    public async Task<IActionResult> Enable([FromBody] CodeRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();
        if (user.totp_enabled) return Conflict("La verificación en dos pasos ya está activada.");

        var secret = _crypto.UnprotectSecret(user.id, user.totp_secret_encrypted);
        if (secret == null) return BadRequest("Primero inicia la configuración para generar el código QR.");
        if (!TotpService.TryVerify(secret, request?.Code, DateTimeOffset.UtcNow, null, out var step))
            return BadRequest("El código no es válido. Comprueba que la hora de tu móvil es correcta.");

        user.totp_enabled = true;
        user.totp_enabled_at = DateTime.UtcNow;
        user.totp_last_step = step;
        await _context.SaveChangesAsync();
        var codes = await _security.ReplaceRecoveryCodesAsync(user.id);

        await _audit.LogAccessAsync("ENABLE_2FA", "users", user.id.ToString(), null, "El usuario activó la verificación en dos pasos.");
        await NotifyAsync(user, "Verificación en dos pasos activada en DietoExpress",
            "Has activado la verificación en dos pasos en tu cuenta. Si no has sido tú, cambia tu contraseña y contacta con soporte.");
        return Ok(new { recoveryCodes = codes });
    }

    /// <summary>Desactiva el 2FA. Exige contraseña (salvo cuentas Google) y un código TOTP o de recuperación vigente.</summary>
    [HttpPost("disable")]
    public async Task<IActionResult> Disable([FromBody] DisableRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();
        if (!user.totp_enabled) return Conflict("La verificación en dos pasos no está activada.");
        if (AccountSecurityService.IsLocked(user, DateTime.UtcNow)) return BadRequest("Demasiados intentos. Inténtalo de nuevo más tarde.");

        var needsPassword = RequiresPassword(user);
        var passwordOk = !needsPassword ||
            (!string.IsNullOrEmpty(user.password_hash) && !string.IsNullOrEmpty(request?.Password) && request.Password.Length <= 256 &&
             BCrypt.Net.BCrypt.Verify(request.Password, user.password_hash));
        var factor = passwordOk ? await _security.VerifySecondFactorAsync(user, request?.Code, HttpContext.RequestAborted) : SecondFactorResult.Invalid;
        if (!passwordOk || factor == SecondFactorResult.Invalid)
        {
            await _security.RegisterFailureAsync(user.id);
            return BadRequest("La contraseña o el código no son correctos.");
        }

        await _security.DisableTwoFactorAsync(user.id, revokeSessions: false);
        await _security.ResetFailuresAsync(user.id);
        await _audit.LogAccessAsync("DISABLE_2FA", "users", user.id.ToString(), null, "El usuario desactivó la verificación en dos pasos.");
        await NotifyAsync(user, "Verificación en dos pasos desactivada en DietoExpress",
            "Se ha desactivado la verificación en dos pasos de tu cuenta. Si no has sido tú, cambia tu contraseña inmediatamente y contacta con soporte.");
        return NoContent();
    }

    /// <summary>Genera un juego nuevo de códigos de recuperación e invalida los anteriores.</summary>
    [HttpPost("recovery-codes")]
    public async Task<IActionResult> RegenerateRecoveryCodes([FromBody] CodeRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();
        if (!user.totp_enabled) return Conflict("La verificación en dos pasos no está activada.");
        if (AccountSecurityService.IsLocked(user, DateTime.UtcNow)) return BadRequest("Demasiados intentos. Inténtalo de nuevo más tarde.");

        // Para regenerar hay que demostrar posesión del dispositivo: un código de recuperación no vale.
        if (TotpService.NormalizeCode(request?.Code) == null ||
            await _security.VerifySecondFactorAsync(user, request?.Code, HttpContext.RequestAborted) != SecondFactorResult.Totp)
        {
            await _security.RegisterFailureAsync(user.id);
            return BadRequest("El código no es válido.");
        }

        var codes = await _security.ReplaceRecoveryCodesAsync(user.id);
        await _audit.LogAccessAsync("REGENERATE_2FA_RECOVERY_CODES", "users", user.id.ToString(), null, "El usuario regeneró sus códigos de recuperación.");
        return Ok(new { recoveryCodes = codes });
    }

    // Las cuentas creadas con Google no tienen contraseña local que comprobar.
    private static bool RequiresPassword(users user) => !string.Equals(user.provider, "google", StringComparison.OrdinalIgnoreCase);

    private async Task<users?> GetCurrentUserAsync()
    {
        var userId = AuthHelpers.GetUserId(User);
        return userId == null ? null : await _context.users.FirstOrDefaultAsync(u => u.id == userId.Value && u.archived_at == null);
    }

    private async Task NotifyAsync(users user, string subject, string message)
    {
        if (string.IsNullOrWhiteSpace(user.email)) return;
        try
        {
            await _email.SendEmailAsync(user.email, subject, $"<p>Hola {System.Net.WebUtility.HtmlEncode(user.username)},</p><p>{message}</p>");
        }
        catch
        {
            // La notificación es informativa: un fallo de correo no debe revertir el cambio de seguridad.
        }
    }
}
