using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Añade un enlace mágico al portal en los emails dirigidos al paciente que hablan de su portal.
/// Si el envío falla, restaura el token anterior para no invalidar un enlace que el paciente ya recibió.
/// </summary>
public sealed class PatientPortalEmailService
{
    private readonly angulosodbContext _context;
    private readonly PatientPortalAccessService _portalAccess;
    private readonly EmailServ _email;

    public PatientPortalEmailService(
        angulosodbContext context,
        PatientPortalAccessService portalAccess,
        EmailServ email)
    {
        _context = context;
        _portalAccess = portalAccess;
        _email = email;
    }

    public async Task<BoolMensaje> SendAsync(
        int clientId,
        string subject,
        string htmlBody,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default,
        DateTime? expiresAtUtc = null)
    {
        var client = await _context.clients
            .SingleOrDefaultAsync(c => c.id == clientId && c.archived_at == null && c.email != null, cancellationToken);
        if (client == null || string.IsNullOrWhiteSpace(client.email))
            return new BoolMensaje { Exito = false, Mensaje = "El paciente no tiene un email válido." };

        var previousToken = client.access_token;
        var previousExpiry = client.access_token_expires_at;
        var previousTokenVersion = client.portal_token_version;

        var effectiveExpiry = expiresAtUtc ?? DateTime.UtcNow.AddHours(24);
        var accessLink = await _portalAccess.CreateAccessLinkAsync(
            clientId,
            cancellationToken,
            effectiveExpiry);
        if (string.IsNullOrWhiteSpace(accessLink))
            return new BoolMensaje { Exito = false, Mensaje = "No se ha podido generar el enlace de acceso al portal." };

        var safeLink = System.Net.WebUtility.HtmlEncode(accessLink);
        var bodyWithPortalLink = htmlBody +
            $"<p><a href='{safeLink}'>Acceder al portal de paciente</a></p>" +
            "<p>Este enlace es personal y de un solo uso.</p>";

        try
        {
            var result = await _email.SendEmailAsync(client.email, subject, bodyWithPortalLink, idempotencyKey);
            if (result.Exito)
                return result;

            await RestorePreviousAccessAsync(client, previousToken, previousExpiry, previousTokenVersion);
            return result;
        }
        catch
        {
            await RestorePreviousAccessAsync(client, previousToken, previousExpiry, previousTokenVersion);
            throw;
        }
    }

    private async Task RestorePreviousAccessAsync(
        Anguloso.Server.Models.clients client,
        string? previousToken,
        DateTime? previousExpiry,
        int previousTokenVersion)
    {
        client.access_token = previousToken;
        client.access_token_expires_at = previousExpiry;
        client.portal_token_version = previousTokenVersion;
        await _context.SaveChangesAsync(CancellationToken.None);
    }
}
