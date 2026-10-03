using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/professional/tasks")]
[Authorize(Policy = "Professional")]
// Las tareas profesionales son operativas y tenant-scoped; las lecturas y cambios de estado deben respetar siempre ese contexto.
public sealed class ProfessionalTasksController : ControllerBase
{
    private readonly AutomationService _automation;
    private readonly ITenantContextService _tenantContext;

    public ProfessionalTasksController(AutomationService automation, ITenantContextService tenantContext)
    {
        _automation = automation;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    // Las consultas SQL usan tenant_id en el WHERE para que las tareas nunca crucen organizaciones.
    public async Task<ActionResult<IReadOnlyList<ProfessionalTaskDto>>> Get(
        [FromQuery] string? status = "open",
        [FromQuery] int limit = 100)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest(new { message = "La cuenta no tiene organización." });
        limit = Math.Clamp(limit, 1, 200);

        var statuses = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        var sql = statuses is null
            ? """
              SELECT id, tenant_id, client_id, assigned_user_id, title, description, due_at, priority, status, source, created_at, completed_at
              FROM professional_tasks WHERE tenant_id=@tenant
              ORDER BY CASE WHEN status='open' THEN 0 WHEN status='in_progress' THEN 1 ELSE 2 END,
                       due_at NULLS LAST, created_at DESC LIMIT @limit;
              """
            : """
              SELECT id, tenant_id, client_id, assigned_user_id, title, description, due_at, priority, status, source, created_at, completed_at
              FROM professional_tasks WHERE tenant_id=@tenant AND status=@status
              ORDER BY due_at NULLS LAST, created_at DESC LIMIT @limit;
              """;

        await using var connection = new NpgsqlConnection(HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant", _tenantContext.TenantId.Value);
        command.Parameters.AddWithValue("limit", limit);
        if (statuses is not null) command.Parameters.AddWithValue("status", statuses);

        var result = new List<ProfessionalTaskDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(ReadTask(reader));
        }
        return Ok(result);
    }

    [HttpPost]
    // Las tareas manuales solo pueden asignarse al usuario autenticado; las automatizadas se crean desde el servicio interno.
    public async Task<ActionResult<ProfessionalTaskDto>> Create([FromBody] ProfessionalTaskCreateRequest request)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest(new { message = "La cuenta no tiene organización." });
        if (request == null) return BadRequest(new { message = "Datos no válidos." });

        var currentUserId = _tenantContext.UserId;
        if (request.AssignedUserId.HasValue && request.AssignedUserId.Value != currentUserId)
            return Forbid();

        request.AssignedUserId ??= currentUserId;
        // Las tareas creadas por automatizaciones pasan por el mismo servicio para conservar tenant, idempotencia y trazabilidad en un único punto.
        var id = await _automation.CreateProfessionalTaskAsync(
            _tenantContext.TenantId.Value, request, "manual",
            $"manual:{_tenantContext.TenantId.Value}:{Guid.NewGuid():N}");

        return Ok(new { id });
    }

    [HttpPatch("{id:long}/status")]
    // El estado y completed_at se actualizan de forma atómica dentro de la misma sentencia SQL.
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] TaskStatusRequest request)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest(new { message = "La cuenta no tiene organización." });
        var status = request?.Status?.Trim().ToLowerInvariant();
        if (status is not ("open" or "in_progress" or "completed" or "cancelled"))
            return BadRequest(new { message = "Estado no válido." });

        await using var connection = new NpgsqlConnection(HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE professional_tasks
            SET status=@status,
                completed_at=CASE WHEN @status='completed' THEN COALESCE(completed_at,NOW()) ELSE NULL END,
                updated_at=NOW()
            WHERE id=@id AND tenant_id=@tenant;
            """, connection);
        command.Parameters.AddWithValue("status", status!);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", _tenantContext.TenantId.Value);
        var count = await command.ExecuteNonQueryAsync();
        return count == 0 ? NotFound() : NoContent();
    }

    private static ProfessionalTaskDto ReadTask(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        TenantId = reader.GetInt32(1),
        ClientId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
        AssignedUserId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
        Title = reader.GetString(4),
        Description = reader.IsDBNull(5) ? null : reader.GetString(5),
        DueAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
        Priority = reader.GetString(7),
        Status = reader.GetString(8),
        Source = reader.GetString(9),
        CreatedAt = reader.GetDateTime(10),
        CompletedAt = reader.IsDBNull(11) ? null : reader.GetDateTime(11)
    };

    public sealed class TaskStatusRequest { public string? Status { get; set; } }
}
