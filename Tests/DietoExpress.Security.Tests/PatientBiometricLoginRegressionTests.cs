using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public class PatientBiometricLoginRegressionTests
{
    private const string MasterKey = "0123456789abcdef0123456789abcdef-test-key";

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

    private static (ECDsa Key, string PublicKey) NewDeviceKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }

    private static string Sign(ECDsa key, string deviceId, string challenge) =>
        Convert.ToBase64String(key.SignData(PatientBiometricCrypto.BuildSignedMessage(deviceId, challenge),
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    [Fact]
    public void Signature_FromTheDeviceKeyVerifiesAndOnlyForThatDeviceAndChallenge()
    {
        var crypto = new PatientBiometricCrypto(MasterKey);
        var (key, publicKey) = NewDeviceKey();
        var challenge = crypto.CreateChallenge("dev1", DateTimeOffset.UtcNow, out _);
        var signature = Sign(key, "dev1", challenge);

        Assert.True(PatientBiometricCrypto.VerifySignature(publicKey, "dev1", challenge, signature));
        // La firma no sirve para otro dispositivo, otro desafío ni otra clave.
        Assert.False(PatientBiometricCrypto.VerifySignature(publicKey, "dev2", challenge, signature));
        Assert.False(PatientBiometricCrypto.VerifySignature(publicKey, "dev1", challenge + "x", signature));
        var (_, otherPublicKey) = NewDeviceKey();
        Assert.False(PatientBiometricCrypto.VerifySignature(otherPublicKey, "dev1", challenge, signature));
        Assert.False(PatientBiometricCrypto.VerifySignature(publicKey, "dev1", challenge, null));
        Assert.False(PatientBiometricCrypto.VerifySignature(publicKey, "dev1", challenge, "no-base64!"));
        Assert.False(PatientBiometricCrypto.VerifySignature("clave-rota", "dev1", challenge, signature));
    }

    [Fact]
    public void PublicKey_OnlyP256InSpkiFormatIsAccepted()
    {
        var (_, p256) = NewDeviceKey();
        Assert.True(PatientBiometricCrypto.TryParsePublicKey(p256, out _));

        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.False(PatientBiometricCrypto.TryParsePublicKey(Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo()), out _));
        using var rsa = RSA.Create(2048);
        Assert.False(PatientBiometricCrypto.TryParsePublicKey(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), out _));
        Assert.False(PatientBiometricCrypto.TryParsePublicKey("", out _));
        Assert.False(PatientBiometricCrypto.TryParsePublicKey("AAAA", out _));
        Assert.False(PatientBiometricCrypto.TryParsePublicKey(new string('A', 500), out _));
    }

    [Fact]
    public void Challenge_IsBoundToTheDeviceExpiresAndCannotBeForged()
    {
        var crypto = new PatientBiometricCrypto(MasterKey);
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var challenge = crypto.CreateChallenge("dev1", now, out var issued);

        Assert.True(crypto.TryReadChallenge(challenge, now.AddSeconds(90), out var device, out var issuedBack));
        Assert.Equal("dev1", device);
        Assert.Equal(issued, issuedBack);
        Assert.False(crypto.TryReadChallenge(challenge, now.AddMinutes(3), out _, out _));
        Assert.False(new PatientBiometricCrypto(MasterKey + "-otra").TryReadChallenge(challenge, now, out _, out _));
        Assert.False(crypto.TryReadChallenge("basura", now, out _, out _));
        Assert.False(crypto.TryReadChallenge(null, now, out _, out _));
        // Cambiar el dispositivo dentro del payload invalida el HMAC.
        var forgedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"dev9.{issued}.AAAA")).TrimEnd('=');
        Assert.False(crypto.TryReadChallenge(forgedPayload + "." + challenge.Split('.')[1], now, out _, out _));
    }

    [Fact]
    public void Controller_ConsumesEachChallengeOnceAfterVerifyingTheSignature()
    {
        var source = ReadServer("Controllers", "PatientBiometricController.cs");
        var start = source.IndexOf("public async Task<IActionResult> Login(", StringComparison.Ordinal);
        var login = source[start..];

        Assert.Contains("TryReadChallenge", login);
        Assert.Contains("VerifySignature", login);
        Assert.Contains("last_challenge_issued_at < {issuedAt}", login);
        Assert.Contains("revoked_at IS NULL", login);
        Assert.Contains("CanUseFeatureAsync(tenantId, \"CLIENT_PORTAL\")", login);
        Assert.Contains("c.archived_at == null", login);
        Assert.True(login.IndexOf("VerifySignature", StringComparison.Ordinal) < login.IndexOf("last_challenge_issued_at", StringComparison.Ordinal));
        Assert.True(login.IndexOf("last_challenge_issued_at", StringComparison.Ordinal) < login.IndexOf("_sessions.Issue", StringComparison.Ordinal));
    }

    [Fact]
    public void Controller_ProtectsManagementEndpointsAndRateLimitsTheOpenOnes()
    {
        var source = ReadServer("Controllers", "PatientBiometricController.cs");
        foreach (var action in new[] { "Register(", "ListDevices(", "RevokeDevice(" })
        {
            var at = source.IndexOf("public async Task<IActionResult> " + action, StringComparison.Ordinal);
            Assert.Contains("[Authorize(Roles = \"patient\")]", source[Math.Max(0, at - 250)..at]);
        }
        Assert.Equal(3, Regex.Matches(source, Regex.Escape("[EnableRateLimiting(\"auth\")]")).Count);
        // El dispositivo nuevo se notifica por email y hay un tope de dispositivos por paciente.
        Assert.Contains("MaxActiveDevicesPerPatient = 5", source);
        Assert.Contains("Acceso biométrico activado", source);
    }

    [Fact]
    public void ChangingThePin_RevokesBiometricDevices()
    {
        var source = ReadServer("Controllers", "PatientPortalController.cs");
        var start = source.IndexOf("public async Task<IActionResult> SetPasscode(", StringComparison.Ordinal);
        Assert.Contains("UPDATE patient_biometric_devices SET revoked_at = NOW()", source[start..Math.Min(source.Length, start + 2500)]);
    }

    [Fact]
    public void AllPatientLoginMethodsShareTheSameSessionIssuer()
    {
        var portal = ReadServer("Controllers", "PatientPortalController.cs");
        Assert.Contains("_sessions.Issue(Response, client);", portal);
        Assert.DoesNotContain("GeneratePatientJwt", portal);
        var issuer = ReadServer("Logica", "PatientSessionService.cs");
        Assert.Contains("HttpOnly = true", issuer);
        Assert.Contains("SameSite = SameSiteMode.Strict", issuer);
        Assert.Contains("portalTokenVersion", issuer);
    }

    [Fact]
    public void Schema_CreatesTheDeviceTable()
    {
        var source = ReadServer("Logica", "DatabaseBootstrap.cs");
        Assert.Contains("CREATE TABLE IF NOT EXISTS patient_biometric_devices", source);
        Assert.Contains("last_challenge_issued_at BIGINT NOT NULL DEFAULT 0", source);
        Assert.Contains("'patient_biometric_devices'", source);
    }
}
