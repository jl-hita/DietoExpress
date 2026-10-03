using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/professional/automation")]
[Authorize(Policy = "Professional")]
public sealed class AutomationController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ITenantContextService _tenantContext;

    public AutomationController(IConfiguration configuration, ITenantContextService tenantContext)
    {
        _configuration = configuration;
        _tenantContext = tenantContext;
    }

    public sealed record AutomationRuleRequest(bool Enabled, int? DelayMinutes, string? RecipientScope, string[]? Channels);

    private static readonly (string Key, string RecipientScope, string[] Channels)[] SupportedRules =
    [
        ("client.created", "assigned_professional", ["in_app"]),
        ("patient.checkin.submitted", "assigned_professional", ["in_app"]),
        ("patient.checkin.reviewed", "patient", ["in_app"]),
        ("appointment.completed", "both", ["in_app"]),
        ("appointment.reminder.24h", "patient", ["in_app"]),
        ("appointment.reminder.2h", "patient", ["in_app"]),
        ("appointment.no_show", "assigned_professional", ["in_app"]),
        ("onboarding.info.reminder", "patient", ["in_app"]),
        ("onboarding.info.escalation", "assigned_professional", ["in_app"]),
        ("onboarding.first_appointment.reminder", "patient", ["in_app"]),
        ("onboarding.first_appointment.escalation", "assigned_professional", ["in_app"]),
        ("followup.checkin.reminder", "patient", ["in_app"]),
        ("followup.checkin.escalation", "assigned_professional", ["in_app"]),
        ("diet.expiry.reminder", "patient", ["in_app"]),
        ("diet.expired", "both", ["in_app"])
    ];

    [HttpGet("rules")]
    public async Task<IActionResult> GetRules()
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest();

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""
            SELECT rule_key, enabled, delay_minutes, recipient_scope, channels, updated_at
            FROM automation_rules
            WHERE tenant_id=@tenant;
            """, connection);
        command.Parameters.AddWithValue("tenant", _tenantContext.TenantId.Value);

        var configured = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            configured[reader.GetString(0)] = new
            {
                ruleKey = reader.GetString(0),
                enabled = reader.GetBoolean(1),
                delayMinutes = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                recipientScope = reader.GetString(3),
                channels = reader.IsDBNull(4) ? Array.Empty<string>() :
                    (System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? Array.Empty<string>()),
                updatedAt = reader.GetDateTime(5)
            };
        }

        var rows = SupportedRules.Select(rule =>
            configured.TryGetValue(rule.Key, out var value)
                ? value
                : new
                {
                    ruleKey = rule.Key,
                    enabled = true,
                    delayMinutes = (int?)null,
                    recipientScope = rule.RecipientScope,
                    channels = rule.Channels,
                    updatedAt = (DateTime?)null
                }).ToList();

        return Ok(rows);
    }

    [HttpPut("rules/{ruleKey}")]
    public async Task<IActionResult> UpdateRule(string ruleKey, [FromBody] AutomationRuleRequest request)
    {
        if (!_tenantContext.TenantId.HasValue || string.IsNullOrWhiteSpace(ruleKey)) return BadRequest();
        ruleKey = ruleKey.Trim().ToLowerInvariant();
        if (ruleKey.Length > 120 || request.DelayMinutes is < 0 or > 525600) return BadRequest();

        var recipient = string.IsNullOrWhiteSpace(request.RecipientScope)
            ? "assigned_professional"
            : request.RecipientScope.Trim().ToLowerInvariant();
        if (recipient is not ("assigned_professional" or "clinic_admin" or "patient" or "both")) return BadRequest();

        var channels = request.Channels is { Length: > 0 }
            ? request.Channels.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length <= 30).Take(10).ToArray()
            : ["in_app"];

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO automation_rules(tenant_id, rule_key, enabled, delay_minutes, recipient_scope, channels, updated_at)
            VALUES(@tenant,@rule,@enabled,@delay,@recipient,@channels::jsonb,NOW())
            ON CONFLICT (tenant_id, rule_key)
            DO UPDATE SET enabled=EXCLUDED.enabled, delay_minutes=EXCLUDED.delay_minutes,
                          recipient_scope=EXCLUDED.recipient_scope, channels=EXCLUDED.channels, updated_at=NOW();
            """, connection);
        command.Parameters.AddWithValue("tenant", _tenantContext.TenantId.Value);
        command.Parameters.AddWithValue("rule", ruleKey);
        command.Parameters.AddWithValue("enabled", request.Enabled);
        command.Parameters.AddWithValue("delay", (object?)request.DelayMinutes ?? DBNull.Value);
        command.Parameters.AddWithValue("recipient", recipient);
        command.Parameters.AddWithValue("channels", System.Text.Json.JsonSerializer.Serialize(channels));
        await command.ExecuteNonQueryAsync();
        return NoContent();
    }

    // Este endpoint es solo de observabilidad y control: el worker sigue siendo el único componente que ejecuta jobs.
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 100)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest();
        // El límite evita que la pantalla de observabilidad pueda convertir una consulta administrativa en una lectura masiva.
        limit = Math.Clamp(limit, 1, 200);

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        // Todas las lecturas se filtran por tenant en SQL, no después de materializar resultados, para que una
        // futura modificación del DTO no pueda convertir accidentalmente la consulta en una fuga cross-tenant.
        await using var command = new NpgsqlCommand("""
            SELECT id, event_id, action_type, scheduled_at, status, attempts, max_attempts,
                   last_error, created_at, updated_at, completed_at
            FROM automation_jobs
            WHERE tenant_id=@tenant
              AND (@status IS NULL OR status=@status)
            ORDER BY scheduled_at DESC, id DESC
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("tenant", _tenantContext.TenantId.Value);
        // El estado se normaliza antes de consultar, pero no se concatena en SQL: sigue siendo un parámetro y no puede alterar la consulta.
        command.Parameters.AddWithValue("status", (object?)status?.Trim().ToLowerInvariant() ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);

        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new
            {
                id = reader.GetInt64(0),
                eventId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1),
                actionType = reader.GetString(2),
                scheduledAt = reader.GetDateTime(3),
                status = reader.GetString(4),
                attempts = reader.GetInt32(5),
                maxAttempts = reader.GetInt32(6),
                lastError = reader.IsDBNull(7) ? null : reader.GetString(7),
                createdAt = reader.GetDateTime(8),
                updatedAt = reader.GetDateTime(9),
                completedAt = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10)
            });
        }
        return Ok(rows);
    }

    [HttpGet("jobs/{id:long}/executions")]
    public async Task<IActionResult> GetExecutions(long id)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest();

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();

        // La consulta de ejecuciones parte del job y vuelve a comprobar su tenant; conocer un job_id no basta
        // para consultar el historial de otra organización.
        await using var command = new NpgsqlCommand("""
            SELECT e.id, e.job_id, e.result, e.error, e.duration_ms, e.executed_at
            FROM automation_executions e
            JOIN automation_jobs j ON j.id=e.job_id
            WHERE e.job_id=@id AND j.tenant_id=@tenant
            ORDER BY e.executed_at DESC
            LIMIT 100;
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", _tenantContext.TenantId.Value);

        var rows = new List<AutomationExecutionDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AutomationExecutionDto
            {
                Id = reader.GetInt64(0),
                JobId = reader.GetInt64(1),
                Result = reader.GetString(2),
                Error = reader.IsDBNull(3) ? null : reader.GetString(3),
                DurationMs = reader.GetInt64(4),
                ExecutedAt = reader.GetDateTime(5)
            });
        }
        return Ok(rows);
    }

    [HttpPost("jobs/{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id, [FromBody] CancelAutomationRequest? request)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest();

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        // La transición pending -> cancelled se hace de forma condicional en SQL: si el worker ya reclamó el job,
        // este endpoint no pisa su ejecución ni altera su política de reintentos.
        await using var command = new NpgsqlCommand("""
            UPDATE automation_jobs
            SET status='cancelled',
                last_error=@reason,
                updated_at=NOW()
            WHERE id=@id AND tenant_id=@tenant AND status='pending';
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", _tenantContext.TenantId.Value);
        command.Parameters.AddWithValue("reason", (object?)request?.Reason ?? "Cancelado manualmente");
        var affected = await command.ExecuteNonQueryAsync();
        return affected == 0 ? NotFound() : NoContent();
    }
}
