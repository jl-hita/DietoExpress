using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/professional/followup-goals")]
[Authorize(Policy = "Professional")]
public sealed class ProfessionalFollowupGoalsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ITenantContextService _tenantContext;

    public ProfessionalFollowupGoalsController(IConfiguration configuration, ITenantContextService tenantContext)
    {
        _configuration = configuration;
        _tenantContext = tenantContext;
    }

    [HttpGet("{clientId:int}")]
    public async Task<IActionResult> Get(int clientId)
    {
        if (!TryGetContext(out var tenantId, out var userId)) return BadRequest(new { message = "La cuenta no tiene organización o usuario." });
        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        if (!await CanAccessClientAsync(connection, tenantId, userId, clientId)) return NotFound();

        await using var command = new NpgsqlCommand("""
            SELECT id, title, metric_key, target_value, unit, phase,
                   start_date::text, target_date::text, status, notes, created_at, updated_at
            FROM professional_followup_goals
            WHERE tenant_id=@tenant AND client_id=@client AND professional_user_id=@user
            ORDER BY CASE status WHEN 'active' THEN 0 WHEN 'paused' THEN 1 ELSE 2 END,
                     target_date NULLS LAST, created_at DESC;
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("user", userId);
        var goals = new List<FollowupGoalDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            goals.Add(new FollowupGoalDto
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                MetricKey = reader.GetString(2),
                TargetValue = reader.GetDecimal(3),
                Unit = reader.GetString(4),
                Phase = reader.GetString(5),
                StartDate = reader.GetString(6),
                TargetDate = reader.IsDBNull(7) ? null : reader.GetString(7),
                Status = reader.GetString(8),
                Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                CreatedAt = reader.GetDateTime(10),
                UpdatedAt = reader.GetDateTime(11)
            });
        }
        return Ok(goals);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFollowupGoalRequest request)
    {
        if (!TryGetContext(out var tenantId, out var userId)) return BadRequest(new { message = "La cuenta no tiene organización o usuario." });
        if (request == null || request.ClientId <= 0 || string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 160 || request.Notes?.Length > 1000 ||
            !TryMetric(request.MetricKey, request.TargetValue, out var unit) ||
            !TryPhase(request.Phase, out var phase) || request.TargetDate.HasValue && request.TargetDate.Value < request.StartDate)
            return BadRequest(new { message = "Revisa el título, la métrica, el objetivo, la fase y las fechas." });

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        if (!await CanAccessClientAsync(connection, tenantId, userId, request.ClientId)) return NotFound();
        await using var transaction = await connection.BeginTransactionAsync();

        await using var command = new NpgsqlCommand("""
            INSERT INTO professional_followup_goals
                (tenant_id, client_id, professional_user_id, title, metric_key, target_value, unit,
                 phase, start_date, target_date, status, notes)
            VALUES (@tenant,@client,@user,@title,@metric,@target,@unit,@phase,@start,@end,'active',@notes)
            RETURNING id;
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", request.ClientId);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("title", request.Title.Trim());
        command.Parameters.AddWithValue("metric", request.MetricKey.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("target", request.TargetValue);
        command.Parameters.AddWithValue("unit", unit);
        command.Parameters.AddWithValue("phase", phase);
        command.Parameters.AddWithValue("start", request.StartDate);
        command.Parameters.Add(new NpgsqlParameter("end", NpgsqlTypes.NpgsqlDbType.Date)
        {
            Value = request.TargetDate.HasValue ? request.TargetDate.Value : DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("notes", NpgsqlTypes.NpgsqlDbType.Varchar)
        {
            Value = string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim()
        });
        var id = (int)(await command.ExecuteScalarAsync() ?? 0);
        await InsertEventAsync(connection, transaction, id, tenantId, request.ClientId, userId, "created", null,
            $"{request.Title.Trim()} · objetivo {request.TargetValue}{unit} · fase {phase} · estado active");
        await transaction.CommitAsync();
        return CreatedAtAction(nameof(Get), new { clientId = request.ClientId }, new { id });
    }

    [HttpGet("{clientId:int}/{goalId:int}/history")]
    public async Task<IActionResult> GetHistory(int clientId, int goalId)
    {
        if (!TryGetContext(out var tenantId, out var userId)) return BadRequest(new { message = "La cuenta no tiene organización o usuario." });
        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        if (!await CanAccessClientAsync(connection, tenantId, userId, clientId)) return NotFound();

        await using var command = new NpgsqlCommand("""
            SELECT e.id, e.event_type, e.previous_value, e.new_value, e.occurred_at
            FROM professional_followup_goal_events e
            JOIN professional_followup_goals g ON g.id=e.goal_id
            WHERE e.goal_id=@goal AND e.client_id=@client AND e.tenant_id=@tenant
              AND e.professional_user_id=@user AND g.tenant_id=@tenant
              AND g.client_id=@client AND g.professional_user_id=@user
            ORDER BY e.occurred_at DESC, e.id DESC;
            """, connection);
        command.Parameters.AddWithValue("goal", goalId);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("user", userId);
        var events = new List<FollowupGoalEventDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new FollowupGoalEventDto
            {
                Id = reader.GetInt64(0),
                EventType = reader.GetString(1),
                PreviousValue = reader.IsDBNull(2) ? null : reader.GetString(2),
                NewValue = reader.IsDBNull(3) ? null : reader.GetString(3),
                OccurredAt = reader.GetDateTime(4)
            });
        }
        return Ok(events);
    }

    [HttpPut("{id:int}/phase")]
    public async Task<IActionResult> UpdatePhase(int id, [FromBody] UpdateFollowupGoalPhaseRequest request)
    {
        if (!TryGetContext(out var tenantId, out var userId)) return BadRequest(new { message = "La cuenta no tiene organización o usuario." });
        if (request == null || !TryPhase(request.Phase, out var phase))
            return BadRequest(new { message = "Selecciona una fase válida." });

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        string? previousPhase;
        int clientId;
        await using (var select = new NpgsqlCommand("""
            SELECT g.phase, g.client_id
            FROM professional_followup_goals g
            WHERE g.id=@id AND g.tenant_id=@tenant AND g.professional_user_id=@user
              AND EXISTS (
                SELECT 1 FROM clients c WHERE c.id=g.client_id AND c.tenant_id=@tenant AND c.archived_at IS NULL
                  AND EXISTS (SELECT 1 FROM client_nutritionist_assignments a
                    WHERE a.client_id=c.id AND a.nutritionist_id=@user AND a.is_active)
              )
            FOR UPDATE;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("id", id);
            select.Parameters.AddWithValue("tenant", tenantId);
            select.Parameters.AddWithValue("user", userId);
            await using var reader = await select.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return NotFound();
            previousPhase = reader.GetString(0);
            clientId = reader.GetInt32(1);
        }

        if (string.Equals(previousPhase, phase, StringComparison.Ordinal))
        {
            await transaction.CommitAsync();
            return NoContent();
        }
        await using (var update = new NpgsqlCommand("""
            UPDATE professional_followup_goals SET phase=@phase, updated_at=NOW()
            WHERE id=@id AND tenant_id=@tenant AND professional_user_id=@user;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("phase", phase);
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("tenant", tenantId);
            update.Parameters.AddWithValue("user", userId);
            await update.ExecuteNonQueryAsync();
        }
        await InsertEventAsync(connection, transaction, id, tenantId, clientId, userId, "phase_changed", previousPhase, phase);
        await transaction.CommitAsync();
        return NoContent();
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateFollowupGoalStatusRequest request)
    {
        if (!TryGetContext(out var tenantId, out var userId)) return BadRequest(new { message = "La cuenta no tiene organización o usuario." });
        if (request == null || request.Status is not ("active" or "paused" or "completed"))
            return BadRequest(new { message = "El estado debe ser activo, pausado o completado." });

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        string? previousStatus;
        int clientId;
        await using (var select = new NpgsqlCommand("""
            SELECT g.status, g.client_id
            FROM professional_followup_goals g
            WHERE g.id=@id AND g.tenant_id=@tenant AND g.professional_user_id=@user
              AND EXISTS (
                SELECT 1 FROM clients c WHERE c.id=g.client_id AND c.tenant_id=@tenant AND c.archived_at IS NULL
                  AND EXISTS (SELECT 1 FROM client_nutritionist_assignments a
                    WHERE a.client_id=c.id AND a.nutritionist_id=@user AND a.is_active)
              )
            FOR UPDATE;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("id", id);
            select.Parameters.AddWithValue("tenant", tenantId);
            select.Parameters.AddWithValue("user", userId);
            await using var reader = await select.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return NotFound();
            previousStatus = reader.GetString(0);
            clientId = reader.GetInt32(1);
        }

        if (string.Equals(previousStatus, request.Status, StringComparison.Ordinal))
        {
            await transaction.CommitAsync();
            return NoContent();
        }
        await using (var update = new NpgsqlCommand("""
            UPDATE professional_followup_goals SET status=@status, updated_at=NOW()
            WHERE id=@id AND tenant_id=@tenant AND professional_user_id=@user;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("status", request.Status);
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("tenant", tenantId);
            update.Parameters.AddWithValue("user", userId);
            await update.ExecuteNonQueryAsync();
        }
        await InsertEventAsync(connection, transaction, id, tenantId, clientId, userId, "status_changed", previousStatus, request.Status);
        await transaction.CommitAsync();
        return NoContent();
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int goalId, int tenantId, int clientId,
        int userId, string eventType, string? previousValue, string? newValue)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO professional_followup_goal_events
                (goal_id, tenant_id, client_id, professional_user_id, event_type, previous_value, new_value)
            VALUES (@goal,@tenant,@client,@user,@event,@previous,@next);
            """, connection, transaction);
        command.Parameters.AddWithValue("goal", goalId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("event", eventType);
        command.Parameters.Add(new NpgsqlParameter("previous", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)previousValue ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("next", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)newValue ?? DBNull.Value });
        await command.ExecuteNonQueryAsync();
    }

    private bool TryGetContext(out int tenantId, out int userId)
    {
        tenantId = _tenantContext.TenantId ?? 0;
        userId = _tenantContext.UserId ?? 0;
        return tenantId > 0 && userId > 0;
    }

    private static async Task<bool> CanAccessClientAsync(NpgsqlConnection connection, int tenantId, int userId, int clientId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM clients c
                WHERE c.id=@client AND c.tenant_id=@tenant AND c.archived_at IS NULL
                  AND EXISTS (
                    SELECT 1 FROM client_nutritionist_assignments a
                    WHERE a.client_id=c.id AND a.nutritionist_id=@user AND a.is_active
                  )
            );
            """, connection);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("user", userId);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static bool TryMetric(string metric, decimal target, out string unit)
    {
        unit = metric?.Trim().ToLowerInvariant() switch
        {
            "adherence" => "%",
            "hunger" or "energy" or "sleep_quality" or "training" => "/10",
            "sleep_hours" => "h",
            "weight" => "kg",
            _ => ""
        };
        if (unit.Length == 0) return false;
        return metric.Trim().ToLowerInvariant() switch
        {
            "adherence" => target is >= 0 and <= 100,
            "hunger" or "energy" or "sleep_quality" or "training" => target is >= 0 and <= 10,
            "sleep_hours" => target is >= 0 and <= 24,
            "weight" => target is > 0 and <= 500,
            _ => false
        };
    }

    private static bool TryPhase(string? value, out string phase)
    {
        phase = (value ?? "").Trim().ToLowerInvariant();
        return phase is "baseline" or "building" or "maintenance" or "review";
    }

    public sealed class CreateFollowupGoalRequest
    {
        public int ClientId { get; set; }
        public string Title { get; set; } = "";
        public string MetricKey { get; set; } = "";
        public decimal TargetValue { get; set; }
        public string Phase { get; set; } = "building";
        public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
        public DateOnly? TargetDate { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class UpdateFollowupGoalPhaseRequest
    {
        public string Phase { get; set; } = "";
    }

    public sealed class FollowupGoalEventDto
    {
        public long Id { get; set; }
        public string EventType { get; set; } = "";
        public string? PreviousValue { get; set; }
        public string? NewValue { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    public sealed class UpdateFollowupGoalStatusRequest
    {
        public string Status { get; set; } = "";
    }

    public sealed class FollowupGoalDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string MetricKey { get; set; } = "";
        public decimal TargetValue { get; set; }
        public string Unit { get; set; } = "";
        public string Phase { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string? TargetDate { get; set; }
        public string Status { get; set; } = "";
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
