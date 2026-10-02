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

    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 100)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest();
        limit = Math.Clamp(limit, 1, 200);

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
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
