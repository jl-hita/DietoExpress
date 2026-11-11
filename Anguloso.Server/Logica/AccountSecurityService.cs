using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Operaciones atómicas de seguridad de cuenta: bloqueo progresivo, verificación del segundo factor y códigos de recuperación.
/// Todas las mutaciones se hacen con una única sentencia SQL para que peticiones paralelas no puedan multiplicar los intentos.
/// </summary>
public sealed class AccountSecurityService
{
    /// <summary>Fallos consecutivos tolerados antes de empezar a bloquear.</summary>
    public const int FreeAttempts = 5;
    /// <summary>Bloqueo máximo: limita el daño de un bloqueo malicioso de una cuenta ajena.</summary>
    public const int MaxLockMinutes = 15;

    private readonly angulosodbContext _db;
    private readonly TwoFactorCrypto _crypto;

    public AccountSecurityService(angulosodbContext db, TwoFactorCrypto crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    public static bool IsLocked(users user, DateTime nowUtc) => user.locked_until.HasValue && user.locked_until.Value > nowUtc;

    /// <summary>Hash válido de BCrypt contra el que se verifica cuando la cuenta no existe, para igualar los tiempos de respuesta.</summary>
    public static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

    /// <summary>A partir del 5.º fallo seguido bloquea 1, 2, 4, 8 y 15 minutos.</summary>
    public async Task RegisterFailureAsync(int userId, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE users
            SET failed_login_count = failed_login_count + 1,
                locked_until = CASE WHEN failed_login_count + 1 >= {FreeAttempts}
                    THEN NOW() + make_interval(mins => LEAST({MaxLockMinutes}, 1 << LEAST(failed_login_count + 1 - {FreeAttempts}, 4)))
                    ELSE locked_until END
            WHERE id = {userId}
            """, ct);
    }

    /// <summary>Fallos de PIN tolerados antes de bloquear el acceso con PIN de un paciente.</summary>
    public const int PortalFreeAttempts = 5;
    /// <summary>
    /// Un PIN de 6 dígitos tiene solo un millón de combinaciones, así que el bloqueo es más duro que el de profesionales:
    /// 1, 2, 4, 8, 16, 32 y 60 minutos. A ese ritmo adivinarlo por fuerza bruta lleva años aunque se rote de IP.
    /// El paciente siempre puede entrar con el enlace de acceso que recibe por email, que no queda bloqueado.
    /// </summary>
    public const int PortalMaxLockMinutes = 60;

    public static bool IsPortalLocked(clients client, DateTime nowUtc) => client.portal_locked_until.HasValue && client.portal_locked_until.Value > nowUtc;

    public async Task RegisterPortalFailureAsync(int clientId, CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE clients
            SET portal_failed_attempts = portal_failed_attempts + 1,
                portal_locked_until = CASE WHEN portal_failed_attempts + 1 >= {PortalFreeAttempts}
                    THEN NOW() + make_interval(mins => LEAST({PortalMaxLockMinutes}, 1 << LEAST(portal_failed_attempts + 1 - {PortalFreeAttempts}, 6)))
                    ELSE portal_locked_until END
            WHERE id = {clientId}
            """, ct);
    }

    public Task ResetPortalFailuresAsync(int clientId, CancellationToken ct = default) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE clients SET portal_failed_attempts = 0, portal_locked_until = NULL WHERE id = {clientId}", ct);

    public Task ResetFailuresAsync(int userId, CancellationToken ct = default) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET failed_login_count = 0, locked_until = NULL WHERE id = {userId}", ct);

    /// <summary>
    /// Valida un código TOTP de 6 dígitos o un código de recuperación. El TOTP solo se acepta una vez por paso de tiempo
    /// y un código de recuperación se consume al usarse.
    /// </summary>
    public async Task<SecondFactorResult> VerifySecondFactorAsync(users user, string? code, CancellationToken ct = default)
    {
        if (!user.totp_enabled) return SecondFactorResult.Invalid;

        if (TotpService.NormalizeCode(code) is { } totpCode)
        {
            var secret = _crypto.UnprotectSecret(user.id, user.totp_secret_encrypted);
            if (secret is null) return SecondFactorResult.Invalid;
            if (!TotpService.TryVerify(secret, totpCode, DateTimeOffset.UtcNow, user.totp_last_step, out var step)) return SecondFactorResult.Invalid;

            // El UPDATE condicionado hace de "compare-and-set": solo una petición puede consumir este paso.
            var accepted = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE users SET totp_last_step = {step}
                WHERE id = {user.id} AND (totp_last_step IS NULL OR totp_last_step < {step})
                """, ct);
            return accepted == 1 ? SecondFactorResult.Totp : SecondFactorResult.Invalid;
        }

        if (TwoFactorCrypto.NormalizeRecoveryCode(code) is { } recovery)
        {
            var hash = TwoFactorCrypto.HashRecoveryCode(recovery);
            var consumed = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE user_two_factor_recovery_codes SET used_at = NOW()
                WHERE user_id = {user.id} AND code_hash = {hash} AND used_at IS NULL
                """, ct);
            return consumed == 1 ? SecondFactorResult.RecoveryCode : SecondFactorResult.Invalid;
        }

        return SecondFactorResult.Invalid;
    }

    /// <summary>Sustituye todos los códigos de recuperación y devuelve los nuevos en claro (única vez que se muestran).</summary>
    public async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(int userId, CancellationToken ct = default)
    {
        var codes = TwoFactorCrypto.GenerateRecoveryCodes();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM user_two_factor_recovery_codes WHERE user_id = {userId}", ct);
        foreach (var code in codes)
        {
            var hash = TwoFactorCrypto.HashRecoveryCode(TwoFactorCrypto.NormalizeRecoveryCode(code)!);
            await _db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO user_two_factor_recovery_codes (user_id, code_hash) VALUES ({userId}, {hash})", ct);
        }
        await tx.CommitAsync(ct);
        return codes;
    }

    public Task<int> CountRemainingRecoveryCodesAsync(int userId, CancellationToken ct = default) =>
        _db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM user_two_factor_recovery_codes WHERE user_id = {userId} AND used_at IS NULL").SingleAsync(ct);

    /// <summary>Desactiva el 2FA. El soporte de superadmin revoca además las sesiones existentes; el propio usuario conserva la actual.</summary>
    public async Task DisableTwoFactorAsync(int userId, bool revokeSessions, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE users
            SET totp_enabled = FALSE, totp_secret_encrypted = NULL, totp_enabled_at = NULL, totp_last_step = NULL,
                token_version = token_version + {(revokeSessions ? 1 : 0)}
            WHERE id = {userId}
            """, ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM user_two_factor_recovery_codes WHERE user_id = {userId}", ct);
        await tx.CommitAsync(ct);
    }
}

public enum SecondFactorResult
{
    Invalid,
    Totp,
    RecoveryCode
}
