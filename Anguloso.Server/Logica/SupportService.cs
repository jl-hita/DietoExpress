using Npgsql;

namespace Anguloso.Server.Logica;

public sealed class SupportService
{
    private readonly string _connectionString;

    public SupportService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
    }

    public async Task<IReadOnlyList<SupportTicketSummaryDto>> GetTicketsAsync(
        int userId, int? tenantId, bool isSuperAdmin, string? status, string? category, string? priority,
        string? search, int? assignedToUserId, DateTime? from, DateTime? to, int? filterTenantId)
    {
        var result = new List<SupportTicketSummaryDto>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        var sql = "SELECT t.id,t.tenant_id,t.created_by_user_id,t.assigned_to_user_id,t.subject,t.category,t.priority,t.status,t.created_at,t.updated_at,t.closed_at,creator.full_name,assignee.full_name,(SELECT COUNT(*) FROM support_messages m WHERE m.ticket_id=t.id AND m.is_internal=FALSE) FROM support_tickets t JOIN users creator ON creator.id=t.created_by_user_id LEFT JOIN users assignee ON assignee.id=t.assigned_to_user_id WHERE 1=1 ";
        if (!isSuperAdmin) sql += " AND t.tenant_id=@tenant AND t.created_by_user_id=@user ";
        else if (filterTenantId.HasValue) sql += " AND t.tenant_id=@filterTenant ";
        if (!string.IsNullOrWhiteSpace(status)) sql += " AND t.status=@status ";
        if (!string.IsNullOrWhiteSpace(category)) sql += " AND t.category=@category ";
        if (!string.IsNullOrWhiteSpace(priority)) sql += " AND t.priority=@priority ";
        if (!string.IsNullOrWhiteSpace(search)) sql += " AND (t.subject ILIKE @search OR EXISTS (SELECT 1 FROM support_messages sm WHERE sm.ticket_id=t.id AND sm.is_internal=FALSE AND sm.body ILIKE @search)) ";
        if (assignedToUserId.HasValue) sql += " AND t.assigned_to_user_id=@assigned ";
        if (from.HasValue) sql += " AND t.created_at>=@from ";
        if (to.HasValue) sql += " AND t.created_at<@to ";
        sql += " ORDER BY t.updated_at DESC,t.id DESC LIMIT 200;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant",(object?)tenantId??DBNull.Value);
        command.Parameters.AddWithValue("user",userId);
        command.Parameters.AddWithValue("filterTenant",(object?)filterTenantId??DBNull.Value);
        command.Parameters.AddWithValue("status",(object?)status??DBNull.Value);
        command.Parameters.AddWithValue("category",(object?)category??DBNull.Value);
        command.Parameters.AddWithValue("priority",(object?)priority??DBNull.Value);
        command.Parameters.AddWithValue("search",(object?)(string.IsNullOrWhiteSpace(search)?null:"%"+search.Trim()+"%")??DBNull.Value);
        command.Parameters.AddWithValue("assigned",(object?)assignedToUserId??DBNull.Value);
        command.Parameters.AddWithValue("from",(object?)from??DBNull.Value);
        command.Parameters.AddWithValue("to",(object?)to??DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new SupportTicketSummaryDto {
            Id=reader.GetInt64(0),TenantId=reader.GetInt32(1),CreatedByUserId=reader.GetInt32(2),
            AssignedToUserId=reader.IsDBNull(3)?null:reader.GetInt32(3),Subject=reader.GetString(4),Category=reader.GetString(5),
            Priority=reader.GetString(6),Status=reader.GetString(7),CreatedAt=reader.GetDateTime(8),UpdatedAt=reader.GetDateTime(9),
            ClosedAt=reader.IsDBNull(10)?null:reader.GetDateTime(10),CreatedByName=reader.GetString(11),
            AssignedToName=reader.IsDBNull(12)?null:reader.GetString(12),MessageCount=reader.GetInt64(13)});
        return result;
    }

    public async Task<IReadOnlyList<SupportAssigneeDto>> GetAssigneesAsync()
    {
        var result=new List<SupportAssigneeDto>();
        await using var connection=new NpgsqlConnection(_connectionString); await connection.OpenAsync();
        await using var command=new NpgsqlCommand("SELECT id,full_name FROM users WHERE role='superadmin' AND archived_at IS NULL ORDER BY full_name,id;",connection);
        await using var reader=await command.ExecuteReaderAsync();
        while(await reader.ReadAsync()) result.Add(new SupportAssigneeDto{Id=reader.GetInt32(0),Name=reader.GetString(1)});
        return result;
    }

    public async Task<SupportTicketDto?> GetTicketAsync(long ticketId, int userId, int? tenantId, bool isSuperAdmin)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        const string ticketSql = """
SELECT t.id, t.tenant_id, t.created_by_user_id, t.assigned_to_user_id,
       t.subject, t.category, t.priority, t.status, t.created_at, t.updated_at, t.closed_at,
       creator.full_name, assignee.full_name
FROM support_tickets t
JOIN users creator ON creator.id=t.created_by_user_id
LEFT JOIN users assignee ON assignee.id=t.assigned_to_user_id
WHERE t.id=@id
  AND (@superadmin OR (t.tenant_id=@tenant AND t.created_by_user_id=@user));
""";
        await using var ticketCommand = new NpgsqlCommand(ticketSql, connection);
        ticketCommand.Parameters.AddWithValue("id", ticketId);
        ticketCommand.Parameters.AddWithValue("tenant", (object?)tenantId ?? DBNull.Value);
        ticketCommand.Parameters.AddWithValue("user", userId);
        ticketCommand.Parameters.AddWithValue("superadmin", isSuperAdmin);

        await using var reader = await ticketCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var ticket = new SupportTicketDto
        {
            Id = reader.GetInt64(0),
            TenantId = reader.GetInt32(1),
            CreatedByUserId = reader.GetInt32(2),
            AssignedToUserId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Subject = reader.GetString(4),
            Category = reader.GetString(5),
            Priority = reader.GetString(6),
            Status = reader.GetString(7),
            CreatedAt = reader.GetDateTime(8),
            UpdatedAt = reader.GetDateTime(9),
            ClosedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            CreatedByName = reader.GetString(11),
            AssignedToName = reader.IsDBNull(12) ? null : reader.GetString(12)
        };
        await reader.CloseAsync();

        const string messagesSql = """
SELECT m.id, m.author_user_id, u.full_name, m.body, m.created_at, m.is_internal
FROM support_messages m
JOIN users u ON u.id=m.author_user_id
WHERE m.ticket_id=@ticket
  AND (@superadmin OR m.is_internal=FALSE)
ORDER BY m.created_at, m.id;
""";
        await using var messageCommand = new NpgsqlCommand(messagesSql, connection);
        messageCommand.Parameters.AddWithValue("ticket", ticketId);
        messageCommand.Parameters.AddWithValue("superadmin", isSuperAdmin);
        await using var messages = await messageCommand.ExecuteReaderAsync();
        while (await messages.ReadAsync())
        {
            ticket.Messages.Add(new SupportMessageDto
            {
                Id = messages.GetInt64(0),
                AuthorUserId = messages.GetInt32(1),
                AuthorName = messages.GetString(2),
                Body = messages.GetString(3),
                CreatedAt = messages.GetDateTime(4),
                IsInternal = messages.GetBoolean(5)
            });
        }
        return ticket;
    }

    public async Task<long> CreateTicketAsync(int userId, int tenantId, CreateSupportTicketRequest request)
    {
        ValidateText(request.Subject, 200, "El asunto no es válido.");
        ValidateText(request.Body, 10000, "El mensaje no es válido.");
        ValidateEnum(request.Category, new[] { "problem", "billing", "account", "question", "suggestion", "other" }, "Categoría no válida.");
        ValidateEnum(request.Priority, new[] { "low", "normal", "high", "urgent" }, "Prioridad no válida.");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string ticketSql = """
INSERT INTO support_tickets(tenant_id, created_by_user_id, subject, category, priority)
VALUES(@tenant,@user,@subject,@category,@priority)
RETURNING id;
""";
        await using var ticketCommand = new NpgsqlCommand(ticketSql, connection, transaction);
        ticketCommand.Parameters.AddWithValue("tenant", tenantId);
        ticketCommand.Parameters.AddWithValue("user", userId);
        ticketCommand.Parameters.AddWithValue("subject", request.Subject.Trim());
        ticketCommand.Parameters.AddWithValue("category", request.Category);
        ticketCommand.Parameters.AddWithValue("priority", request.Priority);
        var ticketId = Convert.ToInt64(await ticketCommand.ExecuteScalarAsync());

        await AddMessageInternalAsync(connection, transaction, ticketId, userId, request.Body.Trim(), false);
        await transaction.CommitAsync();
        return ticketId;
    }

    public async Task<bool> AddMessageAsync(long ticketId, int userId, int tenantId, bool isSuperAdmin, string body, bool internalNote)
    {
        ValidateText(body, 10000, "El mensaje no es válido.");
        if (internalNote && !isSuperAdmin) return false;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        const string accessSql = """
SELECT tenant_id, created_by_user_id, status
FROM support_tickets
WHERE id=@id
  AND (@superadmin OR (tenant_id=@tenant AND created_by_user_id=@user));
""";
        await using var access = new NpgsqlCommand(accessSql, connection, transaction);
        access.Parameters.AddWithValue("id", ticketId);
        access.Parameters.AddWithValue("tenant", tenantId);
        access.Parameters.AddWithValue("user", userId);
        access.Parameters.AddWithValue("superadmin", isSuperAdmin);
        await using var reader = await access.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return false;
        var status = reader.GetString(2);
        await reader.CloseAsync();

        if (!isSuperAdmin && (status == "closed" || status == "resolved"))
            return false;

        await AddMessageInternalAsync(connection, transaction, ticketId, userId, body.Trim(), internalNote);

        var nextStatus = isSuperAdmin ? "waiting_user" : "open";
        if (internalNote) nextStatus = status;
        if (isSuperAdmin && status == "closed") nextStatus = "closed";

        await using var update = new NpgsqlCommand("""
UPDATE support_tickets
SET status=@status,
    updated_at=NOW(),
    closed_at=CASE WHEN @status='closed' THEN COALESCE(closed_at,NOW()) ELSE NULL END
WHERE id=@id;
""", connection, transaction);
        update.Parameters.AddWithValue("status", nextStatus);
        update.Parameters.AddWithValue("id", ticketId);
        await update.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<SupportTicketChangeResult?> UpdateTicketAsync(long ticketId,int userId,string? status,string? priority,int? assignedToUserId)
    {
        if(status!=null) ValidateEnum(status,new[]{"open","in_progress","waiting_user","resolved","closed"},"Estado no válido.");
        if(priority!=null) ValidateEnum(priority,new[]{"low","normal","high","urgent"},"Prioridad no válida.");
        await using var connection=new NpgsqlConnection(_connectionString); await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await using var current=new NpgsqlCommand("SELECT status,priority,assigned_to_user_id FROM support_tickets WHERE id=@id FOR UPDATE;",connection,transaction);
        current.Parameters.AddWithValue("id",ticketId);
        await using var reader=await current.ExecuteReaderAsync();
        if(!await reader.ReadAsync())return null;
        var oldStatus=reader.GetString(0);var oldPriority=reader.GetString(1);var oldAssigned=reader.IsDBNull(2)?(int?)null:reader.GetInt32(2);await reader.CloseAsync();
        if(assignedToUserId.HasValue){await using var valid=new NpgsqlCommand("SELECT 1 FROM users WHERE id=@id AND role='superadmin' AND archived_at IS NULL;",connection,transaction);valid.Parameters.AddWithValue("id",assignedToUserId.Value);if(await valid.ExecuteScalarAsync()==null)throw new ArgumentException("El usuario asignado no es un SuperAdmin activo.");}
        var newStatus=status??oldStatus;var newPriority=priority??oldPriority;
        await using var update=new NpgsqlCommand("UPDATE support_tickets SET status=@s,priority=@p,assigned_to_user_id=@a,updated_at=NOW(),closed_at=CASE WHEN @s='closed' THEN COALESCE(closed_at,NOW()) ELSE NULL END WHERE id=@id;",connection,transaction);
        update.Parameters.AddWithValue("s",newStatus);update.Parameters.AddWithValue("p",newPriority);update.Parameters.AddWithValue("a",(object?)assignedToUserId??DBNull.Value);update.Parameters.AddWithValue("id",ticketId);await update.ExecuteNonQueryAsync();
        if(oldStatus!=newStatus)await InsertAuditAsync(connection,transaction,ticketId,userId,"status_changed",oldStatus,newStatus);
        if(oldPriority!=newPriority)await InsertAuditAsync(connection,transaction,ticketId,userId,"priority_changed",oldPriority,newPriority);
        if(oldAssigned!=assignedToUserId)await InsertAuditAsync(connection,transaction,ticketId,userId,"assignment_changed",oldAssigned?.ToString(),assignedToUserId?.ToString());
        await transaction.CommitAsync();
        return new SupportTicketChangeResult{StatusChanged=oldStatus!=newStatus,PriorityChanged=oldPriority!=newPriority,AssignmentChanged=oldAssigned!=assignedToUserId,AssignedToUserId=assignedToUserId};
    }

    private static async Task InsertAuditAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,long ticketId,int actor,string action,string? oldValue,string? newValue)
    {
        await using var command=new NpgsqlCommand("INSERT INTO support_ticket_audit(ticket_id,actor_user_id,action,old_value,new_value) VALUES(@t,@u,@a,@o,@n);",connection,transaction);
        command.Parameters.AddWithValue("t",ticketId);command.Parameters.AddWithValue("u",actor);command.Parameters.AddWithValue("a",action);command.Parameters.AddWithValue("o",(object?)oldValue??DBNull.Value);command.Parameters.AddWithValue("n",(object?)newValue??DBNull.Value);await command.ExecuteNonQueryAsync();
    }

    private static async Task AddMessageInternalAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long ticketId, int authorUserId, string body, bool internalNote)
    {
        await using var command = new NpgsqlCommand("""
INSERT INTO support_messages(ticket_id, author_user_id, body, is_internal)
VALUES(@ticket,@author,@body,@internal);
""", connection, transaction);
        command.Parameters.AddWithValue("ticket", ticketId);
        command.Parameters.AddWithValue("author", authorUserId);
        command.Parameters.AddWithValue("body", body);
        command.Parameters.AddWithValue("internal", internalNote);
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateText(string value, int maxLength, string message)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new ArgumentException(message);
    }

    private static void ValidateEnum(string value, string[] allowed, string message)
    {
        if (!allowed.Contains(value, StringComparer.Ordinal))
            throw new ArgumentException(message);
    }
}

public sealed class CreateSupportTicketRequest
{
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Category { get; set; } = "question";
    public string Priority { get; set; } = "normal";
}

public sealed class AddSupportMessageRequest
{
    public string Body { get; set; } = "";
    public bool Internal { get; set; }
}

public sealed class UpdateSupportTicketRequest
{
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public int? AssignedToUserId { get; set; }
}

public class SupportTicketSummaryDto
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int CreatedByUserId { get; set; }
    public int? AssignedToUserId { get; set; }
    public string Subject { get; set; } = "";
    public string Category { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string CreatedByName { get; set; } = "";
    public string? AssignedToName { get; set; }
    public long MessageCount { get; set; }
}

public sealed class SupportTicketDto : SupportTicketSummaryDto
{
    public List<SupportMessageDto> Messages { get; } = new();
}

public sealed class SupportAssigneeDto{public int Id{get;set;}public string Name{get;set;}="";}
public sealed class SupportTicketChangeResult{public bool StatusChanged{get;set;}public bool PriorityChanged{get;set;}public bool AssignmentChanged{get;set;}public int? AssignedToUserId{get;set;}}

public sealed class SupportMessageDto
{
    public long Id { get; set; }
    public int AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsInternal { get; set; }
}
