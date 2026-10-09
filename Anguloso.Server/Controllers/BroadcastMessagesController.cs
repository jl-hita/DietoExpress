using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Comunicaciones masivas independientes de los chats privados: cada destinatario recibe una entrada propia.
/// </summary>
[ApiController]
[Route("api/broadcast-messages")]
[Authorize]
public sealed class BroadcastMessagesController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly ILogger<BroadcastMessagesController> _logger;

    public BroadcastMessagesController(angulosodbContext context, ILogger<BroadcastMessagesController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "superadmin")]
    public async Task<IActionResult> Send([FromBody] BroadcastMessageRequest request, CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        var body = request.Body?.Trim();
        var audience = request.Audience?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            return BadRequest(new { message = "El asunto es obligatorio y no puede superar 200 caracteres." });
        if (string.IsNullOrWhiteSpace(body) || body.Length > 10000)
            return BadRequest(new { message = "El mensaje es obligatorio y no puede superar 10.000 caracteres." });
        if (audience is not ("nutritionists" or "clinics" or "professionals" or "clients"))
            return BadRequest(new { message = "Selecciona un público válido." });

        // Crear cada objeto en un comando independiente evita incompatibilidades de Npgsql con comandos SQL múltiples.
        await EnsureSchemaAsync(cancellationToken);

        var senderId = AuthHelpers.GetUserId(User);
        if (!senderId.HasValue) return Unauthorized();

        var recipients = new List<(string Type, int Id)>();
        if (audience is "nutritionists" or "professionals")
        {
            var ids = await _context.users.AsNoTracking()
                .Where(u => u.archived_at == null && u.role == "nutritionist")
                .Select(u => u.id).ToListAsync(cancellationToken);
            recipients.AddRange(ids.Select(id => ("user", id)));
        }
        if (audience is "clinics" or "professionals")
        {
            var ids = await _context.users.AsNoTracking()
                .Where(u => u.archived_at == null && u.role == "clinic_admin")
                .Select(u => u.id).ToListAsync(cancellationToken);
            recipients.AddRange(ids.Select(id => ("user", id)));
        }
        if (audience == "clients")
        {
            var ids = await _context.clients.AsNoTracking()
                .Where(c => c.archived_at == null)
                .Select(c => c.id).ToListAsync(cancellationToken);
            recipients.AddRange(ids.Select(id => ("client", id)));
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var messageId = await _context.Database.SqlQueryRaw<long>(@"
INSERT INTO broadcast_messages(title, body, audience, created_by_user_id)
VALUES ({0}, {1}, {2}, {3})
RETURNING id AS ""Value"";", title, body, audience, senderId.Value)
            .SingleAsync(cancellationToken);

        foreach (var recipient in recipients.Distinct())
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO broadcast_message_recipients(message_id, recipient_type, recipient_id)
VALUES ({messageId}, {recipient.Type}, {recipient.Id})
ON CONFLICT (message_id, recipient_type, recipient_id) DO NOTHING;", cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Superadmin broadcast {MessageId} delivered to {RecipientCount} recipients in audience {Audience}.",
            messageId, recipients.Distinct().Count(), audience);
        return Ok(new { id = messageId, recipientCount = recipients.Distinct().Count(), audience });
    }

    [HttpGet("inbox")]
    [Authorize(Roles = "superadmin,nutritionist,clinic_admin,patient")]
    public async Task<IActionResult> Inbox(CancellationToken cancellationToken)
    {
        var recipientType = User.IsInRole("patient") ? "client" : "user";
        int? recipientId = recipientType == "client"
            ? (int.TryParse(User.FindFirst("clientId")?.Value, out var clientId) ? clientId : null)
            : AuthHelpers.GetUserId(User);
        if (!recipientId.HasValue || recipientId.Value <= 0) return Unauthorized();

        await EnsureSchemaAsync(cancellationToken);
        var messages = await _context.Database.SqlQueryRaw<BroadcastInboxRow>(@"
SELECT m.id AS ""Id"", m.title AS ""Title"", m.body AS ""Body"",
       m.audience AS ""Audience"", m.created_at AS ""CreatedAt"", r.read_at AS ""ReadAt""
FROM broadcast_message_recipients r
JOIN broadcast_messages m ON m.id = r.message_id
WHERE r.recipient_type = {0} AND r.recipient_id = {1}
ORDER BY m.created_at DESC, m.id DESC
LIMIT 100;", recipientType, recipientId.Value).ToListAsync(cancellationToken);
        return Ok(messages);
    }

    [HttpPatch("{id:long}/read")]
    [Authorize(Roles = "superadmin,nutritionist,clinic_admin,patient")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken cancellationToken)
    {
        var recipientType = User.IsInRole("patient") ? "client" : "user";
        int? recipientId = recipientType == "client"
            ? (int.TryParse(User.FindFirst("clientId")?.Value, out var clientId) ? clientId : null)
            : AuthHelpers.GetUserId(User);
        if (!recipientId.HasValue || recipientId.Value <= 0) return Unauthorized();

        await EnsureSchemaAsync(cancellationToken);
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE broadcast_message_recipients SET read_at = COALESCE(read_at, NOW())
WHERE message_id = {id} AND recipient_type = {recipientType} AND recipient_id = {recipientId.Value};", cancellationToken);
        return affected == 0 ? NotFound() : NoContent();
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS broadcast_messages (
    id BIGSERIAL PRIMARY KEY,
    title VARCHAR(200) NOT NULL,
    body TEXT NOT NULL,
    audience VARCHAR(32) NOT NULL,
    created_by_user_id INTEGER NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);", cancellationToken);

        await _context.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS broadcast_message_recipients (
    id BIGSERIAL PRIMARY KEY,
    message_id BIGINT NOT NULL REFERENCES broadcast_messages(id) ON DELETE CASCADE,
    recipient_type VARCHAR(16) NOT NULL CHECK (recipient_type IN ('user', 'client')),
    recipient_id INTEGER NOT NULL,
    read_at TIMESTAMPTZ NULL,
    UNIQUE(message_id, recipient_type, recipient_id)
);", cancellationToken);

        await _context.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS ix_broadcast_recipients_inbox
    ON broadcast_message_recipients(recipient_type, recipient_id, read_at);", cancellationToken);
    }
}

public sealed class BroadcastMessageRequest
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? Audience { get; set; }
}

public sealed class BroadcastInboxRow
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Audience { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
