using System.Security.Cryptography;
using System.Text;

namespace Anguloso.Server.Logica;

/// <summary>
/// Implementación de TOTP (RFC 6238, HMAC-SHA1, 6 dígitos, paso de 30 s), compatible con Google Authenticator,
/// Microsoft Authenticator, Authy, 1Password, etc. No tiene dependencias externas para que su comportamiento sea auditable.
/// </summary>
public static class TotpService
{
    public const int Digits = 6;
    public const int StepSeconds = 30;
    private const int SecretBytes = 20;
    // ±1 paso (30 s) tolera desfase de reloj del móvil sin ampliar demasiado la ventana de adivinación.
    private const int AllowedDriftSteps = 1;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Genera un secreto aleatorio de 160 bits.</summary>
    public static byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    public static long GetStep(DateTimeOffset moment) => moment.ToUnixTimeSeconds() / StepSeconds;

    public static string ComputeCode(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xFF); step >>= 8; }

        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Comprueba un código de 6 dígitos. <paramref name="lastAcceptedStep"/> impide reutilizar el mismo código
    /// (o uno anterior) aunque siga dentro de la ventana de validez.
    /// </summary>
    public static bool TryVerify(byte[] secret, string? code, DateTimeOffset now, long? lastAcceptedStep, out long matchedStep)
    {
        matchedStep = 0;
        var normalized = NormalizeCode(code);
        if (normalized is null) return false;

        var current = GetStep(now);
        var matched = false;
        // Se recorren siempre todos los pasos con comparación en tiempo constante para no filtrar cuál coincidió.
        for (var delta = -AllowedDriftSteps; delta <= AllowedDriftSteps; delta++)
        {
            var step = current + delta;
            var expected = ComputeCode(secret, step);
            var equal = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(normalized));
            if (!equal || matched) continue;
            if (lastAcceptedStep.HasValue && step <= lastAcceptedStep.Value) continue;
            matched = true;
            matchedStep = step;
        }
        return matched;
    }

    /// <summary>Acepta "123456" y "123 456"; devuelve null si no son exactamente 6 dígitos.</summary>
    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var compact = new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return compact.Length == Digits && compact.All(char.IsAsciiDigit) ? compact : null;
    }

    public static string ToBase32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] FromBase32(string value)
    {
        var output = new List<byte>(value.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var raw in value)
        {
            if (raw == '=' || char.IsWhiteSpace(raw)) continue;
            var index = Base32Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (index < 0) throw new FormatException("Carácter Base32 no válido.");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return output.ToArray();
    }

    /// <summary>URI estándar otpauth:// que leen las apps autenticadoras al escanear el QR.</summary>
    public static string BuildOtpAuthUri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={ToBase32(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}
