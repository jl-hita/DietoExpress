using System.Security.Cryptography;
using System.Text;

namespace Anguloso.Server.Logica;

/// <summary>
/// Primitivas criptográficas del segundo factor: cifrado del secreto TOTP en reposo, desafío firmado de corta
/// duración entre el paso de contraseña y el de código, y códigos de recuperación.
/// Las claves se derivan con HKDF a partir de <c>Security:TwoFactorKey</c> (o de <c>Jwt:Key</c> si no existe), de modo
/// que un volcado de la base de datos por sí solo no permite recuperar los secretos y no dependen del anillo de
/// claves de DataProtection, que puede perderse al restaurar el servidor.
/// </summary>
public sealed class TwoFactorCrypto
{
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly byte[] _secretKey;
    private readonly byte[] _challengeKey;

    public TwoFactorCrypto(string masterKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey) || Encoding.UTF8.GetByteCount(masterKey) < 32)
            throw new InvalidOperationException("La clave del segundo factor debe tener al menos 32 bytes.");
        var master = Encoding.UTF8.GetBytes(masterKey);
        _secretKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: Encoding.UTF8.GetBytes("dietoexpress/2fa/secret/v1"));
        _challengeKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: Encoding.UTF8.GetBytes("dietoexpress/2fa/challenge/v1"));
    }

    public static TwoFactorCrypto FromConfiguration(IConfiguration configuration) =>
        new(configuration["Security:TwoFactorKey"] is { Length: > 0 } dedicated ? dedicated : configuration["Jwt:Key"] ?? string.Empty);

    /// <summary>Cifra el secreto con AES-GCM ligándolo al usuario para que no pueda copiarse a otra fila.</summary>
    public string ProtectSecret(int userId, byte[] secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[secret.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_secretKey, TagSize);
        aes.Encrypt(nonce, secret, cipher, tag, UserBinding(userId));
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public byte[]? UnprotectSecret(int userId, string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return null;
        try
        {
            var raw = Convert.FromBase64String(protectedValue);
            if (raw.Length <= NonceSize + TagSize) return null;
            var nonce = raw.AsSpan(0, NonceSize);
            var tag = raw.AsSpan(NonceSize, TagSize);
            var cipher = raw.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(_secretKey, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, UserBinding(userId));
            return plain;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Desafío que acredita que la contraseña (o Google) ya se validó. Está firmado, caduca a los 5 minutos y queda
    /// ligado a la versión de sesión del usuario, así que un cambio de contraseña lo invalida.
    /// </summary>
    public string CreateChallenge(int userId, int tokenVersion, DateTimeOffset now)
    {
        var expires = now.Add(ChallengeLifetime).ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var payload = $"{userId}.{tokenVersion}.{expires}.{nonce}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var mac = HMACSHA256.HashData(_challengeKey, payloadBytes);
        return $"{Base64Url(payloadBytes)}.{Base64Url(mac)}";
    }

    public bool TryReadChallenge(string? token, DateTimeOffset now, out int userId, out int tokenVersion)
    {
        userId = 0;
        tokenVersion = 0;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512) return false;
        var parts = token.Split('.');
        if (parts.Length != 2) return false;
        try
        {
            var payloadBytes = FromBase64Url(parts[0]);
            var mac = FromBase64Url(parts[1]);
            var expected = HMACSHA256.HashData(_challengeKey, payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(mac, expected)) return false;

            var fields = Encoding.UTF8.GetString(payloadBytes).Split('.');
            if (fields.Length != 4) return false;
            if (!int.TryParse(fields[0], out var id) || !int.TryParse(fields[1], out var version) || !long.TryParse(fields[2], out var expires)) return false;
            if (now.ToUnixTimeSeconds() > expires) return false;
            userId = id;
            tokenVersion = version;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Genera códigos de un solo uso con formato XXXXX-XXXXX (50 bits de entropía cada uno).</summary>
    public static IReadOnlyList<string> GenerateRecoveryCodes(int count = 10)
    {
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var chars = new char[10];
            for (var j = 0; j < chars.Length; j++) chars[j] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
            codes.Add($"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}");
        }
        return codes;
    }

    /// <summary>Devuelve la forma canónica de un código de recuperación, o null si no tiene el formato esperado.</summary>
    public static string? NormalizeRecoveryCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var compact = new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return compact.Length == 10 && compact.All(RecoveryAlphabet.Contains) ? compact : null;
    }

    /// <summary>Los códigos son aleatorios y de alta entropía, por lo que basta SHA-256 (sin sal) para almacenarlos.</summary>
    public static string HashRecoveryCode(string normalizedCode) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("dietoexpress-recovery:" + normalizedCode)));

    private static byte[] UserBinding(int userId) => Encoding.UTF8.GetBytes($"user:{userId}");

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
