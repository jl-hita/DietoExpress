using System.Security.Cryptography;
using System.Text;

namespace Anguloso.Server.Logica;

/// <summary>
/// Criptografía del acceso biométrico del paciente. La app genera en el Keystore del móvil un par EC P-256 cuya clave
/// privada solo se puede usar tras una autenticación biométrica y nunca sale del dispositivo. El servidor guarda la clave
/// pública y, para entrar, emite un desafío de un solo uso que la app firma. No se almacena ningún secreto reutilizable.
/// </summary>
public sealed class PatientBiometricCrypto
{
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(2);
    private const string P256Oid = "1.2.840.10045.3.1.7";
    private const string SignaturePrefix = "dietoexpress-biometric-v1";
    private readonly byte[] _challengeKey;

    public PatientBiometricCrypto(string masterKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey) || Encoding.UTF8.GetByteCount(masterKey) < 32)
            throw new InvalidOperationException("La clave del acceso biométrico debe tener al menos 32 bytes.");
        _challengeKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(masterKey), 32,
            info: Encoding.UTF8.GetBytes("dietoexpress/patient-biometric/challenge/v1"));
    }

    public static PatientBiometricCrypto FromConfiguration(IConfiguration configuration) =>
        new(configuration["Security:BiometricKey"] is { Length: > 0 } dedicated ? dedicated : configuration["Jwt:Key"] ?? string.Empty);

    /// <summary>Desafío firmado con HMAC, ligado a un dispositivo y a su instante de emisión (segundos Unix).</summary>
    public string CreateChallenge(string deviceId, DateTimeOffset now, out long issuedAt)
    {
        issuedAt = now.ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes($"{deviceId}.{issuedAt}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}");
        return $"{Base64Url(payload)}.{Base64Url(HMACSHA256.HashData(_challengeKey, payload))}";
    }

    public bool TryReadChallenge(string? challenge, DateTimeOffset now, out string deviceId, out long issuedAt)
    {
        deviceId = string.Empty;
        issuedAt = 0;
        if (string.IsNullOrWhiteSpace(challenge) || challenge.Length > 400) return false;
        var parts = challenge.Split('.');
        if (parts.Length != 2) return false;
        try
        {
            var payload = FromBase64Url(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(FromBase64Url(parts[1]), HMACSHA256.HashData(_challengeKey, payload))) return false;
            var fields = Encoding.UTF8.GetString(payload).Split('.');
            if (fields.Length != 3 || !long.TryParse(fields[1], out var issued)) return false;
            var age = now.ToUnixTimeSeconds() - issued;
            if (age < -5 || age > ChallengeLifetime.TotalSeconds) return false;
            deviceId = fields[0];
            issuedAt = issued;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Acepta solo claves públicas EC P-256 en formato X.509 SubjectPublicKeyInfo (lo que exporta el Keystore de Android).</summary>
    public static bool TryParsePublicKey(string? base64, out byte[] spki)
    {
        spki = [];
        if (string.IsNullOrWhiteSpace(base64) || base64.Length > 400) return false;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(bytes, out var read);
            if (read != bytes.Length) return false;
            if (ecdsa.ExportParameters(false).Curve.Oid?.Value != P256Oid) return false;
            spki = bytes;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>El texto firmado por la app incluye el dispositivo, para que una firma no sirva para otro.</summary>
    public static byte[] BuildSignedMessage(string deviceId, string challenge) =>
        Encoding.UTF8.GetBytes($"{SignaturePrefix}|{deviceId}|{challenge}");

    public static bool VerifySignature(string publicKeyBase64, string deviceId, string challenge, string? signatureBase64)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64) || signatureBase64.Length > 200) return false;
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            // SHA256withECDSA de Android produce una firma ASN.1 DER (RFC 3279).
            return ecdsa.VerifyData(BuildSignedMessage(deviceId, challenge), Convert.FromBase64String(signatureBase64),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }
}
