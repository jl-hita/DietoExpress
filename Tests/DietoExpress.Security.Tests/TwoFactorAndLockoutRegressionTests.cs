using System.Text;
using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Xunit;

namespace DietoExpress.Security.Tests;

public class TwoFactorAndLockoutRegressionTests
{
    private const string MasterKey = "0123456789abcdef0123456789abcdef-test-key";
    private static readonly byte[] Rfc6238Secret = Encoding.ASCII.GetBytes("12345678901234567890");

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    private static string ReadServer(params string[] path) => File.ReadAllText(Path.Combine([RepoRoot, "Anguloso.Server", .. path]));

    // ---- TOTP (RFC 6238, apéndice B, SHA-1; los 6 últimos dígitos de los vectores de 8 dígitos) ----

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Totp_MatchesRfc6238TestVectors(long unixSeconds, string expected)
    {
        Assert.Equal(expected, TotpService.ComputeCode(Rfc6238Secret, unixSeconds / TotpService.StepSeconds));
    }

    [Fact]
    public void Totp_AcceptsOneStepOfClockDriftButNotMore()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = TotpService.GetStep(now);

        Assert.True(TotpService.TryVerify(Rfc6238Secret, TotpService.ComputeCode(Rfc6238Secret, step - 1), now, null, out _));
        Assert.True(TotpService.TryVerify(Rfc6238Secret, TotpService.ComputeCode(Rfc6238Secret, step + 1), now, null, out _));
        Assert.False(TotpService.TryVerify(Rfc6238Secret, TotpService.ComputeCode(Rfc6238Secret, step - 2), now, null, out _));
        Assert.False(TotpService.TryVerify(Rfc6238Secret, TotpService.ComputeCode(Rfc6238Secret, step + 2), now, null, out _));
    }

    [Fact]
    public void Totp_RejectsReplayOfAnAlreadyAcceptedStep()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var code = TotpService.ComputeCode(Rfc6238Secret, TotpService.GetStep(now));

        Assert.True(TotpService.TryVerify(Rfc6238Secret, code, now, null, out var accepted));
        Assert.False(TotpService.TryVerify(Rfc6238Secret, code, now, accepted, out _));
        // Un código de un paso anterior al último aceptado tampoco vale.
        var older = TotpService.ComputeCode(Rfc6238Secret, accepted - 1);
        Assert.False(TotpService.TryVerify(Rfc6238Secret, older, now, accepted, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Totp_RejectsMalformedCodes(string? code)
    {
        Assert.False(TotpService.TryVerify(Rfc6238Secret, code, DateTimeOffset.UtcNow, null, out _));
    }

    [Fact]
    public void Totp_AcceptsSpacedCodesAsTypedByAuthenticatorApps()
    {
        Assert.Equal("123456", TotpService.NormalizeCode("123 456"));
    }

    [Fact]
    public void Base32_RoundTripsAndMatchesKnownVector()
    {
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", TotpService.ToBase32(Rfc6238Secret));
        Assert.Equal(Rfc6238Secret, TotpService.FromBase32("gezd gnbv gy3t qojq gezd gnbv gy3t qojq"));
        var random = TotpService.GenerateSecret();
        Assert.Equal(random, TotpService.FromBase32(TotpService.ToBase32(random)));
    }

    [Fact]
    public void OtpAuthUri_IncludesIssuerSecretAndParameters()
    {
        var uri = TotpService.BuildOtpAuthUri("DietoExpress", "ana@example.com", Rfc6238Secret);
        Assert.StartsWith("otpauth://totp/DietoExpress:ana%40example.com?", uri);
        Assert.Contains("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", uri);
        Assert.Contains("issuer=DietoExpress", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    // ---- Cifrado del secreto y desafío ----

    [Fact]
    public void TwoFactorCrypto_EncryptsSecretBoundToUser()
    {
        var crypto = new TwoFactorCrypto(MasterKey);
        var protectedValue = crypto.ProtectSecret(7, Rfc6238Secret);

        Assert.DoesNotContain(Convert.ToBase64String(Rfc6238Secret), protectedValue);
        Assert.Equal(Rfc6238Secret, crypto.UnprotectSecret(7, protectedValue));
        // Copiar el valor cifrado a otra cuenta no debe permitir usarlo.
        Assert.Null(crypto.UnprotectSecret(8, protectedValue));
        Assert.Null(new TwoFactorCrypto(MasterKey + "-otra").UnprotectSecret(7, protectedValue));
        Assert.Null(crypto.UnprotectSecret(7, "no-es-base64!"));
        Assert.Null(crypto.UnprotectSecret(7, null));
    }

    [Fact]
    public void TwoFactorCrypto_RejectsWeakKeys()
    {
        Assert.Throws<InvalidOperationException>(() => new TwoFactorCrypto("corta"));
        Assert.Throws<InvalidOperationException>(() => new TwoFactorCrypto(""));
    }

    [Fact]
    public void Challenge_IsSignedExpiresAndCarriesSessionVersion()
    {
        var crypto = new TwoFactorCrypto(MasterKey);
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var token = crypto.CreateChallenge(42, 3, now);

        Assert.True(crypto.TryReadChallenge(token, now.AddMinutes(4), out var userId, out var version));
        Assert.Equal(42, userId);
        Assert.Equal(3, version);
        Assert.False(crypto.TryReadChallenge(token, now.AddMinutes(6), out _, out _));
        Assert.False(new TwoFactorCrypto(MasterKey + "-otra").TryReadChallenge(token, now, out _, out _));
    }

    [Fact]
    public void Challenge_RejectsTamperingAndGarbage()
    {
        var crypto = new TwoFactorCrypto(MasterKey);
        var now = DateTimeOffset.UtcNow;
        var token = crypto.CreateChallenge(42, 3, now);
        var parts = token.Split('.');
        // Cambiar el identificador del usuario dentro del payload invalida la firma.
        var forgedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("1.3.9999999999.AAAA")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.False(crypto.TryReadChallenge(forgedPayload + "." + parts[1], now, out _, out _));
        Assert.False(crypto.TryReadChallenge("abc", now, out _, out _));
        Assert.False(crypto.TryReadChallenge(null, now, out _, out _));
        Assert.False(crypto.TryReadChallenge(new string('a', 600), now, out _, out _));
    }

    // ---- Códigos de recuperación ----

    [Fact]
    public void RecoveryCodes_AreUniqueWellFormedAndNormalizeConsistently()
    {
        var codes = TwoFactorCrypto.GenerateRecoveryCodes();
        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
        foreach (var code in codes)
        {
            Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", code);
            var normalized = TwoFactorCrypto.NormalizeRecoveryCode(code.ToLowerInvariant().Replace("-", " "));
            Assert.NotNull(normalized);
            Assert.Equal(TwoFactorCrypto.HashRecoveryCode(TwoFactorCrypto.NormalizeRecoveryCode(code)!), TwoFactorCrypto.HashRecoveryCode(normalized!));
        }
        Assert.Null(TwoFactorCrypto.NormalizeRecoveryCode("123456"));
        Assert.Null(TwoFactorCrypto.NormalizeRecoveryCode("0O1I0-O1I0O"));
    }

    // ---- Bloqueo ----

    [Fact]
    public void IsLocked_OnlyWhileLockedUntilIsInTheFuture()
    {
        var now = DateTime.UtcNow;
        Assert.False(AccountSecurityService.IsLocked(new users(), now));
        Assert.False(AccountSecurityService.IsLocked(new users { locked_until = now.AddSeconds(-1) }, now));
        Assert.True(AccountSecurityService.IsLocked(new users { locked_until = now.AddMinutes(1) }, now));
    }

    [Fact]
    public void LockoutPolicy_IsProgressiveAndCapped()
    {
        Assert.Equal(5, AccountSecurityService.FreeAttempts);
        Assert.Equal(15, AccountSecurityService.MaxLockMinutes);

        var source = ReadServer("Logica", "AccountSecurityService.cs");
        // El contador y el bloqueo se actualizan en una sola sentencia atómica, no leyendo y escribiendo la entidad.
        Assert.Contains("failed_login_count = failed_login_count + 1", source);
        Assert.Contains("make_interval(mins =>", source);
        Assert.Contains("totp_last_step < ", source);
    }

    // ---- Controladores ----

    [Fact]
    public void Login_EqualizesTimingAndUsesOneGenericMessage()
    {
        var source = ReadServer("Controllers", "AuthController.cs");
        var start = source.IndexOf("public async Task<IActionResult> Login(", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<IActionResult> LoginTwoFactor(", start, StringComparison.Ordinal);
        var login = source[start..end];

        Assert.Contains("AccountSecurityService.DummyPasswordHash", login);
        Assert.Contains("BCrypt.Net.BCrypt.Verify(login.Password, storedHash)", login);
        Assert.Contains("IsLocked(user", login);
        Assert.Contains("RegisterFailureAsync", login);
        // El usuario inexistente, la cuenta bloqueada y la contraseña errónea devuelven exactamente el mismo mensaje.
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(login, "Unauthorized\\(GenericLoginFailure\\)").Count);
        Assert.DoesNotContain("Credenciales inválidas.", login);
        // La sesión de un usuario con 2FA no se emite hasta completar el segundo paso.
        Assert.True(login.IndexOf("totp_enabled", StringComparison.Ordinal) < login.IndexOf("FinishLoginAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void SessionCookie_IsOnlyIssuedFromFinishLoginAfterTheSecondFactor()
    {
        var source = ReadServer("Controllers", "AuthController.cs");
        var twoFactorStart = source.IndexOf("public async Task<IActionResult> LoginTwoFactor(", StringComparison.Ordinal);
        var finishStart = source.IndexOf("private async Task<IActionResult> FinishLoginAsync(", twoFactorStart, StringComparison.Ordinal);
        var twoFactor = source[twoFactorStart..finishStart];

        Assert.Contains("TryReadChallenge", twoFactor);
        Assert.Contains("user.token_version != tokenVersion", twoFactor);
        Assert.Contains("VerifySecondFactorAsync", twoFactor);
        Assert.Contains("RegisterFailureAsync", twoFactor);
        Assert.DoesNotContain("SetProfessionalSessionCookie", twoFactor);
    }

    [Fact]
    public void GoogleLogin_RequiresTheSecondFactorBeforeAnySessionIsIssued()
    {
        var source = ReadServer("Controllers", "AuthController.cs");
        var start = source.IndexOf("public async Task<IActionResult> LoginGoogle(", StringComparison.Ordinal);
        var firstSession = source.IndexOf("SetProfessionalSessionCookie(", start, StringComparison.Ordinal);
        var gate = source.IndexOf("twoFactorCandidate is { totp_enabled: true }", start, StringComparison.Ordinal);

        Assert.True(gate > start && gate < firstSession, "El desafío 2FA debe resolverse antes de emitir cualquier cookie en el login con Google.");
    }

    [Fact]
    public void PasswordResets_ClearTheLockout()
    {
        Assert.Contains("user.locked_until = null;", ReadServer("Controllers", "AuthController.cs"));
        Assert.Contains("user.locked_until = null;", ReadServer("Controllers", "AdminUsersController.cs"));
    }

    [Fact]
    public void TwoFactorManagement_RequiresProfessionalSessionAndProofOfPossession()
    {
        var source = ReadServer("Controllers", "TwoFactorController.cs");

        Assert.Contains("[Authorize(Policy = \"Professional\")]", source);
        Assert.Contains("[EnableRateLimiting(\"auth\")]", source);
        Assert.Contains("BCrypt.Net.BCrypt.Verify(request.Password, user.password_hash)", source);
        Assert.Contains("!= SecondFactorResult.Totp", source);
        Assert.DoesNotContain("totp_secret_encrypted =", source.Replace("user.totp_secret_encrypted = _crypto.ProtectSecret", string.Empty));
    }

    [Fact]
    public void AdminCanResetTwoFactorOnlyForNonSuperadminsAndRevokesSessions()
    {
        var source = ReadServer("Controllers", "AdminUsersController.cs");
        var start = source.IndexOf("public async Task<IActionResult> ResetUserTwoFactor(", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<IActionResult> ResetUserPassword(", start, StringComparison.Ordinal);
        var action = source[start..end];

        Assert.Contains("user.role == \"superadmin\"", action);
        Assert.Contains("revokeSessions: true", action);
        Assert.Contains("RESET_USER_2FA", action);
    }

    [Fact]
    public void Schema_CreatesLockoutAndTwoFactorStructures()
    {
        var source = ReadServer("Logica", "DatabaseBootstrap.cs");
        Assert.Contains("UpgradeAccountSecuritySchemaV1(context, logger);", source);
        Assert.Contains("ADD COLUMN IF NOT EXISTS failed_login_count", source);
        Assert.Contains("ADD COLUMN IF NOT EXISTS totp_secret_encrypted", source);
        Assert.Contains("CREATE TABLE IF NOT EXISTS user_two_factor_recovery_codes", source);
    }

    [Fact]
    public void SecretsAreNeverReturnedFromAnyControllerResponse()
    {
        foreach (var file in new[] { "AuthController.cs", "TwoFactorController.cs", "AdminUsersController.cs" })
        {
            var source = ReadServer("Controllers", file);
            Assert.DoesNotContain("totp_secret_encrypted = user", source);
            Assert.DoesNotContain("totpSecretEncrypted", source, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public class PatientPortalPinRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    private static string ReadServer(params string[] path) => File.ReadAllText(Path.Combine([RepoRoot, "Anguloso.Server", .. path]));

    [Theory]
    [InlineData("000000")]
    [InlineData("111111")]
    [InlineData("123456")]
    [InlineData("654321")]
    [InlineData("234567")]
    [InlineData("789012")]
    [InlineData("121212")]
    [InlineData("112233")]
    [InlineData("123123")]
    [InlineData("482284")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData(null)]
    public void PortalPin_RejectsGuessablePatterns(string? pin)
    {
        Assert.True(PortalPinPolicy.IsWeak(pin));
    }

    [Theory]
    [InlineData("482915")]
    [InlineData("907314")]
    [InlineData("135792")]
    [InlineData("260871")]
    public void PortalPin_AcceptsNonObviousPins(string pin)
    {
        Assert.False(PortalPinPolicy.IsWeak(pin));
    }

    [Fact]
    public void PortalLockout_IsHarsherThanProfessionalLockoutAndNeverBlocksTheEmailLink()
    {
        Assert.Equal(5, AccountSecurityService.PortalFreeAttempts);
        Assert.Equal(60, AccountSecurityService.PortalMaxLockMinutes);
        Assert.True(AccountSecurityService.PortalMaxLockMinutes > AccountSecurityService.MaxLockMinutes);
        Assert.True(AccountSecurityService.IsPortalLocked(new Anguloso.Server.Models.clients { portal_locked_until = DateTime.UtcNow.AddMinutes(5) }, DateTime.UtcNow));
        Assert.False(AccountSecurityService.IsPortalLocked(new Anguloso.Server.Models.clients(), DateTime.UtcNow));

        var source = ReadServer("Controllers", "PatientPortalController.cs");
        var start = source.IndexOf("public async Task<ActionResult<PatientAuthResponseDto>> Authenticate(", StringComparison.Ordinal);
        var end = source.IndexOf("[Authorize]", start, StringComparison.Ordinal);
        var auth = source[start..end];

        var tokenBranch = auth[..auth.IndexOf("else if (!string.IsNullOrWhiteSpace(request.EmailOrPhone)", StringComparison.Ordinal)];
        Assert.DoesNotContain("IsPortalLocked", tokenBranch);
    }

    [Fact]
    public void PortalLogin_EqualizesTimingAndGivesOneGenericAnswerForPinFailures()
    {
        var source = ReadServer("Controllers", "PatientPortalController.cs");
        var start = source.IndexOf("public async Task<ActionResult<PatientAuthResponseDto>> Authenticate(", StringComparison.Ordinal);
        var end = source.IndexOf("[Authorize]", start, StringComparison.Ordinal);
        var auth = source[start..end];

        Assert.Contains("AccountSecurityService.DummyPasswordHash", auth);
        Assert.Contains("RegisterPortalFailureAsync", auth);
        Assert.Contains("IsPortalLocked(client", auth);
        // Email desconocido o ambiguo, bloqueo y PIN erróneo comparten respuesta.
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(auth, System.Text.RegularExpressions.Regex.Escape("Unauthorized(GenericCredentialsFailure)")).Count);
        Assert.DoesNotContain("candidates.Count != 1) return Unauthorized", auth);
        // El acceso correcto limpia el contador.
        Assert.Contains("client.portal_failed_attempts = 0;", auth);
    }

    [Fact]
    public void SettingPin_RejectsWeakPinsAndClearsTheLockout()
    {
        var source = ReadServer("Controllers", "PatientPortalController.cs");
        var start = source.IndexOf("public async Task<IActionResult> SetPasscode(", StringComparison.Ordinal);
        var action = source[start..(start + 1800)];

        Assert.Contains("PortalPinPolicy.IsWeak(dto.Passcode)", action);
        Assert.Contains("client.portal_locked_until = null;", action);
    }

    [Fact]
    public void Schema_AddsPortalLockoutColumns()
    {
        var source = ReadServer("Logica", "DatabaseBootstrap.cs");
        Assert.Contains("ALTER TABLE clients ADD COLUMN IF NOT EXISTS portal_failed_attempts", source);
        Assert.Contains("ALTER TABLE clients ADD COLUMN IF NOT EXISTS portal_locked_until", source);
    }
}
