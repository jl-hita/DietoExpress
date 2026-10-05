using System.Security.Cryptography;
using System.Text;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Genera enlaces mágicos de acceso al portal del paciente.
/// Centraliza la emisión y el hash del token para que los flujos de alta pública
/// y recuperación de acceso utilicen exactamente el mismo mecanismo.
/// </summary>
public sealed class PatientPortalAccessService
{
    private readonly angulosodbContext _context;
    private readonly ConfigServ _configServ;

    public PatientPortalAccessService(angulosodbContext context, ConfigServ configServ)
    {
        _context = context;
        _configServ = configServ;
    }

    public async Task<string?> CreateAccessLinkAsync(int clientId, CancellationToken cancellationToken = default)
    {
        var client = await _context.clients
            .Where(c => c.id == clientId && c.archived_at == null && c.email != null)
            .SingleOrDefaultAsync(cancellationToken);

        if (client == null)
            return null;

        var rawToken = GenerateUrlSafeToken();
        client.access_token = HashAccessToken(rawToken);
        client.access_token_expires_at = DateTime.UtcNow.AddHours(24);
        client.portal_token_version++;

        await _context.SaveChangesAsync(cancellationToken);

        var frontendUrl = _configServ.GetConfigString("frontendUrl", "https://localhost:4200")
            ?? "https://localhost:4200";
        return $"{frontendUrl.TrimEnd('/')}/patient?token={Uri.EscapeDataString(rawToken)}";
    }

    public static string HashAccessToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static string GenerateUrlSafeToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }
}
