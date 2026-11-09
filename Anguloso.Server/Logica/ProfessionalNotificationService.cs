using System.Net;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anguloso.Server.Logica;

public sealed class ProfessionalNotificationDto
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ActionUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public sealed class ProfessionalNotificationRecipient
{
    public int UserId { get; set; }
    public string? Email { get; set; }
    public int? TenantId { get; set; }
    public string? ClientName { get; set; }
}

/// <summary>
/// Avisos in-app (campana y dashboard) del profesional, con email y push como canales adicionales de mejor esfuerzo.
/// Un fallo de email o push nunca impide que el aviso interno exista.
/// </summary>
public sealed class ProfessionalNotificationService
{
    private readonly angulosodbContext _context;
    private readonly EmailServ _email;
    private readonly ProfessionalPushService _push;
    private readonly ILogger<ProfessionalNotificationService> _logger;

    public ProfessionalNotificationService(angulosodbContext context, EmailServ email,
        ProfessionalPushService push, ILogger<ProfessionalNotificationService> logger)
    {
        _context = context;
        _email = email;
        _push = push;
        _logger = logger;
    }

    /// <summary>Profesionales responsables del cliente: asignaciones activas y, si no hay, el propietario.</summary>
    public async Task<IReadOnlyList<ProfessionalNotificationRecipient>> ResolveClientProfessionalsAsync(int clientId, CancellationToken ct)
    {
        var rows = await _context.Database.SqlQueryRaw<ProfessionalNotificationRecipient>("""
            SELECT u.id AS "UserId", u.email AS "Email", COALESCE(c.tenant_id, u.tenant_id) AS "TenantId", c.full_name AS "ClientName"
            FROM clients c
            JOIN users u ON u.id IN (
                SELECT a.nutritionist_id FROM client_nutritionist_assignments a WHERE a.client_id = c.id AND a.is_active
                UNION SELECT c.user_id WHERE c.user_id IS NOT NULL)
            WHERE c.id = {0} AND c.archived_at IS NULL
            """, clientId).ToListAsync(ct);
        return rows.Where(r => r.TenantId.HasValue).ToList();
    }

    /// <summary>
    /// Entrega todos los avisos de una solicitud de revisión. Lo ejecuta el worker de la outbox transaccional, que
    /// reintenta con backoff y deja el error visible si se agotan los intentos (entrega al menos una vez).
    /// Campana y tarea son idempotentes; el email es el único canal que hace fallar (y reintentar) la entrega; el push va
    /// al final y es de mejor esfuerzo para no duplicarse en los reintentos.
    /// </summary>
    public async Task DeliverTrainingReviewRequestAsync(int clientId, long requestId, string planName, string? note, int? planId, CancellationToken ct)
    {
        var recipients = await ResolveClientProfessionalsAsync(clientId, ct);
        const string title = "Solicitud de revisión de rutina";
        var emailFailures = new List<string>();
        foreach (var recipient in recipients)
        {
            var clientName = string.IsNullOrWhiteSpace(recipient.ClientName) ? "Un cliente" : recipient.ClientName!;
            var message = $"{clientName} solicita la revisión manual de la rutina «{planName}». Revísala en Planificación deportiva.";
            var actionUrl = $"/clients/{clientId}/training?request={requestId}" + (planId.HasValue ? $"&plan={planId.Value}" : "");
            var key = $"training-review-request:{requestId}:{recipient.UserId}";
            var tenantId = recipient.TenantId!.Value;

            await _context.Database.ExecuteSqlRawAsync("""
                INSERT INTO professional_notifications(tenant_id, user_id, type, title, message, action_url, idempotency_key)
                VALUES ({0}, {1}, 'training_review_request', {2}, {3}, {4}, {5})
                ON CONFLICT (user_id, idempotency_key) WHERE idempotency_key IS NOT NULL DO NOTHING
                """, new object[] { tenantId, recipient.UserId, title, message, actionUrl, key }, ct);

            await _context.Database.ExecuteSqlRawAsync("""
                INSERT INTO professional_tasks(tenant_id, client_id, assigned_user_id, title, description, priority, status, source, idempotency_key)
                VALUES ({0}, {1}, {2}, {3}, {4}, 'high', 'open', 'training_review_request', {5})
                ON CONFLICT (tenant_id, idempotency_key) WHERE idempotency_key IS NOT NULL DO NOTHING
                """, new object[] { tenantId, clientId, recipient.UserId, title, message, key }, ct);

            if (string.IsNullOrWhiteSpace(recipient.Email)) continue;
            var safeNote = string.IsNullOrWhiteSpace(note) ? "" : $"<p><strong>Nota del cliente:</strong> {WebUtility.HtmlEncode(note)}</p>";
            var body = $"<p>{WebUtility.HtmlEncode(message)}</p>{safeNote}<p>La solicitud no modifica el plan por sí sola: hasta que la atiendas, la rutina permanece igual.</p>";
            var result = await _email.SendEmailAsync(recipient.Email!, title, body, key + ":email");
            if (!result.Exito) emailFailures.Add($"usuario {recipient.UserId}: {result.Mensaje}");
        }

        if (emailFailures.Count > 0)
            throw new InvalidOperationException("No se pudo enviar el email de la solicitud de revisión (" + string.Join("; ", emailFailures) + ").");

        foreach (var recipient in recipients)
        {
            try
            {
                var clientName = string.IsNullOrWhiteSpace(recipient.ClientName) ? "Un cliente" : recipient.ClientName!;
                await _push.SendNotificationAsync(recipient.TenantId!.Value, recipient.UserId, title,
                    $"{clientName} ha solicitado la revisión manual de su rutina.", $"/clients/{clientId}/training", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo enviar el push de la solicitud de revisión {RequestId}.", requestId);
            }
        }
    }

    public Task<List<ProfessionalNotificationDto>> ListAsync(int userId, int limit, CancellationToken ct) =>
        _context.Database.SqlQueryRaw<ProfessionalNotificationDto>("""
            SELECT id AS "Id", type AS "Type", title AS "Title", message AS "Message", action_url AS "ActionUrl",
                   created_at AS "CreatedAt", read_at AS "ReadAt"
            FROM professional_notifications WHERE user_id = {0}
            ORDER BY (read_at IS NULL) DESC, created_at DESC, id DESC LIMIT {1}
            """, userId, Math.Clamp(limit, 1, 100)).ToListAsync(ct);

    public Task<int> UnreadCountAsync(int userId, CancellationToken ct) =>
        _context.Database.SqlQueryRaw<int>(
            """SELECT COUNT(*)::int AS "Value" FROM professional_notifications WHERE user_id = {0} AND read_at IS NULL""", userId)
            .SingleAsync(ct);

    public Task<int> MarkReadAsync(int userId, long id, CancellationToken ct) =>
        _context.Database.ExecuteSqlRawAsync(
            "UPDATE professional_notifications SET read_at = NOW() WHERE id = {0} AND user_id = {1} AND read_at IS NULL", new object[] { id, userId }, ct);

    public Task<int> MarkAllReadAsync(int userId, CancellationToken ct) =>
        _context.Database.ExecuteSqlRawAsync(
            "UPDATE professional_notifications SET read_at = NOW() WHERE user_id = {0} AND read_at IS NULL", new object[] { userId }, ct);
}
