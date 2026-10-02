using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/messages")]
[Authorize]
public class PatientMessagesController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly NotificationService _notifications;
    private readonly EmailServ _emailServ;

    public PatientMessagesController(angulosodbContext context, NotificationService notifications, EmailServ emailServ)
    {
        _context = context;
        _notifications = notifications;
        _emailServ = emailServ;
    }

    [HttpGet("patient")]
    [Authorize(Roles = "patient")]
    public async Task<IActionResult> GetPatientMessages()
    {
        var clientId = await ResolvePatientIdAsync();
        if (clientId == null) return Unauthorized();
        var conversation = await GetOrCreateConversationAsync(clientId.Value);
        return Ok(await ReadMessagesAsync(conversation.Id, clientId.Value));
    }

    [HttpPost("patient")]
    [Authorize(Roles = "patient")]
    public async Task<IActionResult> SendPatientMessage([FromBody] SendMessageRequest request)
    {
        var clientId = await ResolvePatientIdAsync();
        if (clientId == null) return Unauthorized();
        return await SendAsync(clientId.Value, null, request);
    }

    [HttpGet("conversations")]
    [Authorize(Roles = "clinic_admin,nutritionist,user")]
    public async Task<IActionResult> GetConversations()
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!userId.HasValue || !tenantId.HasValue) return Unauthorized();
        return Ok(await GetProfessionalConversationsAsync(userId.Value, tenantId.Value));
    }

    [HttpGet("client/{clientId:int}")]
    [Authorize(Roles = "clinic_admin,nutritionist,user")]
    public async Task<IActionResult> GetProfessionalMessages(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!userId.HasValue || !tenantId.HasValue) return Unauthorized();

        if (!await CanProfessionalAccessAsync(userId.Value, tenantId.Value, clientId))
            return NotFound();

        var conversation = await GetOrCreateConversationAsync(clientId, User.IsInRole("patient") ? null : AuthHelpers.GetUserId(User));
        return Ok(await ReadMessagesAsync(conversation.Id, clientId));
    }

    [HttpPost("client/{clientId:int}")]
    [Authorize(Roles = "clinic_admin,nutritionist,user")]
    public async Task<IActionResult> SendProfessionalMessage(int clientId, [FromBody] SendMessageRequest request)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!userId.HasValue || !tenantId.HasValue) return Unauthorized();

        if (!await CanProfessionalAccessAsync(userId.Value, tenantId.Value, clientId))
            return NotFound();

        return await SendAsync(clientId, userId.Value, request);
    }

    [HttpPatch("client/{clientId:int}/read")]
    [Authorize(Roles = "clinic_admin,nutritionist,user")]
    public async Task<IActionResult> MarkProfessionalMessagesRead(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!userId.HasValue || !tenantId.HasValue || !await CanProfessionalAccessAsync(userId.Value, tenantId.Value, clientId))
            return NotFound();

        var conversation = await GetOrCreateConversationAsync(clientId);
        await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE patient_messages
SET read_at = COALESCE(read_at, NOW())
WHERE conversation_id = {conversation.Id}
  AND sender_client_id IS NOT NULL
  AND read_at IS NULL;");
        return NoContent();
    }

    [HttpPatch("patient/read")]
    [Authorize(Roles = "patient")]
    public async Task<IActionResult> MarkPatientMessagesRead()
    {
        var clientId = await ResolvePatientIdAsync();
        if (clientId == null) return Unauthorized();
        var conversation = await GetOrCreateConversationAsync(clientId.Value);
        await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE patient_messages
SET read_at = COALESCE(read_at, NOW())
WHERE conversation_id = {conversation.Id}
  AND sender_user_id IS NOT NULL
  AND read_at IS NULL;");
        return NoContent();
    }

    private async Task<IActionResult> SendAsync(int clientId, int? senderUserId, SendMessageRequest? request)
    {
        var body = request?.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body)) return BadRequest(new { message = "El mensaje no puede estar vacío." });
        if (body.Length > 5000) return BadRequest(new { message = "El mensaje no puede superar los 5000 caracteres." });

        var client = await _context.clients.AsNoTracking()
            .Where(c => c.id == clientId && c.archived_at == null)
            .Select(c => new { c.id, c.tenant_id, c.full_name })
            .FirstOrDefaultAsync();
        if (client?.tenant_id == null) return NotFound();

        var conversation = await GetOrCreateConversationAsync(clientId);
        long messageId;
        await using var connection = _context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
INSERT INTO patient_messages
(conversation_id, tenant_id, client_id, sender_user_id, sender_client_id, body, created_at)
VALUES (@conversation, @tenant, @client, @senderUser, @senderClient, @body, NOW())
RETURNING id;";
            Add(command, "conversation", conversation.Id);
            Add(command, "tenant", client.tenant_id.Value);
            Add(command, "client", clientId);
            Add(command, "senderUser", senderUserId.HasValue ? senderUserId.Value : DBNull.Value);
            Add(command, "senderClient", senderUserId.HasValue ? DBNull.Value : clientId);
            Add(command, "body", body);
            messageId = Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        await _context.Database.ExecuteSqlInterpolatedAsync($@"UPDATE patient_conversations SET updated_at = NOW() WHERE id = {conversation.Id};");

        if (senderUserId.HasValue)
        {
            await _notifications.CreateForPatientAsync(
                client.tenant_id.Value, clientId, "message",
                "Nuevo mensaje de tu nutricionista",
                body.Length > 180 ? body[..180] + "…" : body,
                "/patient?tab=messages");
        }
        else
        {
            var assignment = await _context.client_nutritionist_assignments.AsNoTracking()
                .Where(a => a.client_id == clientId && a.is_active && a.nutritionist.tenant_id == client.tenant_id.Value)
                .Select(a => new { a.nutritionist_id, a.nutritionist.email, a.nutritionist.full_name })
                .FirstOrDefaultAsync();

            if (assignment?.email != null)
            {
                try
                {
                    var safePatient = System.Net.WebUtility.HtmlEncode(client.full_name ?? "Paciente");
                    var safeBody = System.Net.WebUtility.HtmlEncode(body);
                    await _emailServ.SendEmailAsync(
                        assignment.email,
                        $"Nuevo mensaje de {client.full_name ?? "tu paciente"}",
                        $"<h2>Nuevo mensaje de {safePatient}</h2><p>{safeBody}</p><p>Entra en DietoExpress para responder al paciente.</p>");
                }
                catch (Exception ex)
                {
                    HttpContext.RequestServices.GetRequiredService<ILogger<PatientMessagesController>>()
                        .LogWarning(ex, "No se pudo enviar el aviso de nuevo mensaje del paciente {ClientId}.", clientId);
                }
            }
        }

        return Ok(new { id = messageId });
    }

    private async Task<bool> CanProfessionalAccessAsync(int userId, int tenantId, int clientId)
    {
        var client = await _context.clients.AsNoTracking()
            .Where(c => c.id == clientId && c.tenant_id == tenantId && c.archived_at == null)
            .Select(c => c.id)
            .FirstOrDefaultAsync();
        if (client == 0) return false;
        if (User.IsInRole("clinic_admin")) return true;

        return await _context.client_nutritionist_assignments.AnyAsync(a =>
            a.client_id == clientId &&
            a.nutritionist_id == userId &&
            a.is_active &&
            a.nutritionist.tenant_id == tenantId);
    }

    private async Task<IReadOnlyList<ConversationSummaryDto>> GetProfessionalConversationsAsync(int userId, int tenantId)
    {
        var rows = await _context.Database.SqlQueryRaw<ConversationSummaryRow>($@"
SELECT c.id AS ""ConversationId"", c.client_id AS ""ClientId"", cl.full_name AS ""ClientName"",
       c.updated_at AS ""UpdatedAt"",
       COALESCE((SELECT body FROM patient_messages m WHERE m.conversation_id = c.id ORDER BY m.created_at DESC, m.id DESC LIMIT 1), '') AS ""LastMessage"",
       COALESCE((SELECT COUNT(*)::int FROM patient_messages m WHERE m.conversation_id = c.id AND m.sender_client_id IS NOT NULL AND m.read_at IS NULL), 0) AS ""UnreadCount""
FROM patient_conversations c
JOIN clients cl ON cl.id = c.client_id
WHERE c.tenant_id = {tenantId} AND cl.archived_at IS NULL
  AND EXISTS (SELECT 1 FROM client_nutritionist_assignments a WHERE a.client_id = c.client_id AND a.nutritionist_id = {userId} AND a.is_active)
ORDER BY c.updated_at DESC;").ToListAsync();

        return rows.Select(x => new ConversationSummaryDto {
            ConversationId = x.ConversationId, ClientId = x.ClientId, ClientName = x.ClientName,
            UpdatedAt = x.UpdatedAt, LastMessage = x.LastMessage, UnreadCount = x.UnreadCount
        }).ToList();
    }

    private async Task<long> ResolvePatientIdAsync()
    {
        var raw = User.FindFirst("clientId")?.Value;
        return int.TryParse(raw, out var clientId) ? clientId : 0;
    }

    private async Task<ConversationRef> GetOrCreateConversationAsync(int clientId, int? nutritionistId = null)
    {
        var client = await _context.clients.AsNoTracking()
            .Where(c => c.id == clientId && c.archived_at == null)
            .Select(c => new { c.id, c.tenant_id })
            .FirstOrDefaultAsync();
        if (client?.tenant_id == null) throw new KeyNotFoundException("Paciente no encontrado.");

        var existing = await _context.Database.SqlQueryRaw<long>(
            nutritionistId.HasValue
                ? $@"SELECT id AS ""Value"" FROM patient_conversations WHERE client_id = {clientId} AND assigned_nutritionist_id = {nutritionistId.Value} AND closed_at IS NULL LIMIT 1;"
                : $@"SELECT id AS ""Value"" FROM patient_conversations WHERE client_id = {clientId} AND closed_at IS NULL LIMIT 1;").FirstOrDefaultAsync();
        if (existing != 0) return new ConversationRef(existing, client.tenant_id.Value);

        try
        {
            var id = await _context.Database.SqlQueryRaw<long>(
                nutritionistId.HasValue
                    ? $@"INSERT INTO patient_conversations(tenant_id, client_id, assigned_nutritionist_id) VALUES ({client.tenant_id.Value}, {clientId}, {nutritionistId.Value}) RETURNING id;"
                    : $@"INSERT INTO patient_conversations(tenant_id, client_id) VALUES ({client.tenant_id.Value}, {clientId}) RETURNING id;").FirstAsync();
            return new ConversationRef(id, client.tenant_id.Value);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            var id = await _context.Database.SqlQueryRaw<long>(
                $@"SELECT id AS ""Value"" FROM patient_conversations WHERE client_id = {clientId} LIMIT 1;").FirstAsync();
            return new ConversationRef(id, client.tenant_id.Value);
        }
    }

    private async Task<IReadOnlyList<MessageDto>> ReadMessagesAsync(long conversationId, int clientId)
    {
        var rows = await _context.Database.SqlQueryRaw<MessageRow>($@"
SELECT id AS ""Id"", sender_user_id AS ""SenderUserId"", sender_client_id AS ""SenderClientId"",
       body AS ""Body"", created_at AS ""CreatedAt"", read_at AS ""ReadAt""
FROM patient_messages
WHERE conversation_id = {conversationId} AND client_id = {clientId}
ORDER BY created_at ASC, id ASC
LIMIT 500;").ToListAsync();

        return rows.Select(x => new MessageDto
        {
            Id = x.Id,
            SenderType = x.SenderClientId.HasValue ? "patient" : "professional",
            SenderId = x.SenderClientId ?? x.SenderUserId,
            Body = x.Body,
            CreatedAt = x.CreatedAt,
            ReadAt = x.ReadAt
        }).ToList();
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record ConversationRef(long Id, int TenantId);
    private sealed class MessageRow
    {
        public long Id { get; set; }
        public int? SenderUserId { get; set; }
        public int? SenderClientId { get; set; }
        public string Body { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }
}

public sealed class SendMessageRequest { public string? Body { get; set; } }
public sealed class MessageDto
{
    public long Id { get; set; }
    public string SenderType { get; set; } = "";
    public int? SenderId { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public sealed class ConversationSummaryDto
{
    public long ConversationId { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public string LastMessage { get; set; } = "";
    public int UnreadCount { get; set; }
}

public sealed class ConversationSummaryRow
{
    public long ConversationId { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public string LastMessage { get; set; } = "";
    public int UnreadCount { get; set; }
}
