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

        await using var command = new NpgsqlCommand("""
            INSERT INTO professional_followup_goals
                (tenant_id, client_id, professional_user_id, title, metric_key, target_value, unit,
                 phase, start_date, target_date, status, notes)
            VALUES (@tenant,@client,@user,@title,@metric,@target,@unit,@phase,@start,@end,'active',@notes)
            RETURNING id;
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", request.ClientId);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("title", request.Title.Trim());
        command.Parameters.AddWithValue("metric", request.MetricKey.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("target", request.TargetValue);
        command.Parameters.AddWithValue("unit", unit);
        command.Parameters.AddWithValue("phase", phase);
        command.Parameters.AddWithValue("start", request.StartDate);
        command.Parameters.AddWithValue("end", (object?)request.TargetDate ?? DBNull.Value);
        command.Parameters.AddWithValue("notes", (object?)request.Notes?.Trim() ?? DBNull.Value);
        var id = (int)(await command.ExecuteScalarAsync() ?? 0);
        return CreatedAtAction(nameof(Get), new { clientId = request.ClientId }, new { id });
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateFollowupGoalStatusRequest request)
    {
        if (!TryGetContext(out var tenantId, out var userId)) return BadRequest(new { message = "La cuenta no tiene organización o usuario." });
        if (request == null || request.Status is not ("active" or "paused" or "completed"))
            return BadRequest(new { message = "El estado debe ser activo, pausado o completado." });

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE professional_followup_goals g
            SET status=@status, updated_at=NOW()
            WHERE g.id=@id AND g.tenant_id=@tenant AND g.professional_user_id=@user
              AND EXISTS (
                SELECT 1 FROM clients c
                WHERE c.id=g.client_id AND c.tenant_id=@tenant AND c.archived_at IS NULL
                  AND EXISTS (
                    SELECT 1 FROM client_nutritionist_assignments a
                    WHERE a.client_id=c.id AND a.nutritionist_id=@user AND a.is_active
                  )
              );
            """, connection);
        command.Parameters.AddWithValue("status", request.Status);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("user", userId);
        var changed = await command.ExecuteNonQueryAsync();
        return changed == 0 ? NotFound() : NoContent();
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
