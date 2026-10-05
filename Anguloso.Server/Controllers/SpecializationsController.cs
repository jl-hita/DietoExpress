using System.Data;
using System.Data.Common;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Catálogo y asignación de especializaciones nutricionales.
/// El catálogo es global; la activación pertenece al tenant y la selección clínica al paciente.
/// </summary>
[Route("api/specializations")]
[ApiController]
[Authorize(Policy = "Professional")]
public sealed class SpecializationsController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly IAuditLogService _auditLogService;

    public SpecializationsController(angulosodbContext context, IAuditLogService auditLogService)
    {
        _context = context;
        _auditLogService = auditLogService;
    }

    /// <summary>Devuelve el catálogo activo junto con el estado configurado para el tenant actual.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SpecializationDto>>> GetCatalog()
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("La cuenta no tiene tenant profesional.");

        var rows = new List<SpecializationDto>();
        await using var command = CreateCommand(@"
            SELECT s.id, s.code, s.name, s.category, s.description,
                   COALESCE(ts.enabled, TRUE) AS enabled
            FROM specializations s
            LEFT JOIN tenant_specializations ts
              ON ts.specialization_id = s.id AND ts.tenant_id = @tenant
            WHERE s.active = TRUE
            ORDER BY s.category, s.name;");
        Add(command, "tenant", tenantId.Value);
        await OpenConnectionAsync(command.Connection!);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new SpecializationDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetBoolean(5)));
        }

        return Ok(rows);
    }

    /// <summary>
    /// Activa o desactiva una especialización para el tenant.
    /// En clínicas con varios nutricionistas la configuración global queda reservada a clinic_admin.
    /// </summary>
    [HttpPut("{specializationId:int}/tenant")]
    public async Task<IActionResult> SetTenantSpecialization(int specializationId, [FromBody] TenantSpecializationUpdateDto dto)
    {
        if (dto == null) return BadRequest();
        var tenantId = AuthHelpers.GetTenantId(User);
        var userId = AuthHelpers.GetUserId(User);
        if (!tenantId.HasValue || !userId.HasValue) return Unauthorized();

        if (!await CanConfigureTenantAsync(tenantId.Value, userId.Value))
            return Forbid();

        await using var command = CreateCommand(@"
            INSERT INTO tenant_specializations(tenant_id, specialization_id, enabled, updated_at)
            SELECT @tenant, id, @enabled, NOW()
            FROM specializations
            WHERE id=@specialization AND active=TRUE
            ON CONFLICT (tenant_id, specialization_id)
            DO UPDATE SET enabled=EXCLUDED.enabled, updated_at=NOW()
            RETURNING specialization_id;");
        Add(command, "tenant", tenantId.Value);
        Add(command, "specialization", specializationId);
        Add(command, "enabled", dto.Enabled);
        await OpenConnectionAsync(command.Connection!);

        var result = await command.ExecuteScalarAsync();
        if (result == null) return NotFound("La especialización no existe o está inactiva.");

        await _auditLogService.LogAccessAsync(
            "UPDATE_SPECIALIZATION_CONFIGURATION",
            "specializations",
            specializationId.ToString(),
            null,
            $"enabled={dto.Enabled}");

        return NoContent();
    }

    /// <summary>Obtiene las especializaciones asignadas a un paciente autorizado.</summary>
    [HttpGet("clients/{clientId:int}")]
    public async Task<ActionResult<IReadOnlyList<ClientSpecializationDto>>> GetClientSpecializations(int clientId)
    {
        var access = await ResolveClientAccessAsync(clientId);
        if (!access.Allowed) return access.NotFound ? NotFound() : Forbid();

        var rows = new List<ClientSpecializationDto>();
        await using var command = CreateCommand(@"
            SELECT cs.specialization_id, s.code, s.name, s.category, s.description, cs.notes
            FROM client_specializations cs
            JOIN specializations s ON s.id=cs.specialization_id
            WHERE cs.tenant_id=@tenant AND cs.client_id=@client AND s.active=TRUE
            ORDER BY s.category, s.name;");
        Add(command, "tenant", access.TenantId);
        Add(command, "client", clientId);
        await OpenConnectionAsync(command.Connection!);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new ClientSpecializationDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return Ok(rows);
    }

    /// <summary>
    /// Sustituye atómicamente las especializaciones del paciente.
    /// Solo se admiten especializaciones activas y habilitadas por el tenant.
    /// </summary>
    [HttpPut("clients/{clientId:int}")]
    public async Task<IActionResult> SetClientSpecializations(int clientId, [FromBody] ClientSpecializationUpdateDto dto)
    {
        if (dto == null || dto.Items == null) return BadRequest();
        if (dto.Items.Count > 50) return BadRequest("No se pueden asignar más de 50 especializaciones.");

        var access = await ResolveClientAccessAsync(clientId);
        if (!access.Allowed) return access.NotFound ? NotFound() : Forbid();

        var distinctItems = dto.Items
            .GroupBy(x => x.SpecializationId)
            .Select(g => g.First())
            .ToList();

        if (distinctItems.Any(x => x.Notes?.Length > 2000))
            return BadRequest("Las notas de especialización no pueden superar los 2000 caracteres.");

        var connection = _context.Database.GetDbConnection();
        await OpenConnectionAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await using (var delete = CreateCommand(@"
                DELETE FROM client_specializations
                WHERE tenant_id=@tenant AND client_id=@client;"))
            {
                Add(delete, "tenant", access.TenantId);
                Add(delete, "client", clientId);
                await delete.ExecuteNonQueryAsync();
            }

            if (distinctItems.Count > 0)
            {
                var validIds = new HashSet<int>();
                await using (var validate = CreateCommand(@"
                    SELECT s.id
                    FROM specializations s
                    JOIN tenant_specializations ts
                      ON ts.specialization_id=s.id AND ts.tenant_id=@tenant AND ts.enabled=TRUE
                    WHERE s.active=TRUE AND s.id = ANY(@ids);"))
                {
                    validate.Transaction = transaction;
                    Add(validate, "tenant", access.TenantId);
                    AddArray(validate, "ids", distinctItems.Select(x => x.SpecializationId).ToArray());
                    await using var reader = await validate.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) validIds.Add(reader.GetInt32(0));
                }

                if (validIds.Count != distinctItems.Count)
                {
                    await transaction.RollbackAsync();
                    return BadRequest("Una o varias especializaciones no están habilitadas para esta clínica.");
                }

                foreach (var item in distinctItems)
                {
                    await using var insert = CreateCommand(@"
                        INSERT INTO client_specializations
                            (tenant_id, client_id, specialization_id, notes, created_by_user_id, updated_at)
                        VALUES (@tenant,@client,@specialization,@notes,@user,NOW());");
                    insert.Transaction = transaction;
                    Add(insert, "tenant", access.TenantId);
                    Add(insert, "client", clientId);
                    Add(insert, "specialization", item.SpecializationId);
                    Add(insert, "notes", (object?)item.Notes ?? DBNull.Value);
                    Add(insert, "user", AuthHelpers.GetUserId(User)!.Value);
                    await insert.ExecuteNonQueryAsync();
                }
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        await _auditLogService.LogAccessAsync(
            "UPDATE_CLIENT_SPECIALIZATIONS",
            "clients",
            clientId.ToString(),
            clientId,
            $"count={distinctItems.Count}");

        return NoContent();
    }

    private async Task<bool> CanConfigureTenantAsync(int tenantId, int userId)
    {
        if (User.IsInRole("clinic_admin")) return true;
        if (!User.IsInRole("nutritionist")) return false;

        // En una clínica multi-nutricionista, un profesional no puede cambiar la configuración
        // global que afecta al resto del equipo. En una cuenta profesional individual sí puede.
        await using var command = CreateCommand(@"
            SELECT COUNT(*)
            FROM users
            WHERE tenant_id=@tenant AND role='nutritionist' AND archived_at IS NULL;");
        Add(command, "tenant", tenantId);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        return count <= 1;
    }

    private async Task<ClientAccess> ResolveClientAccessAsync(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!userId.HasValue || !tenantId.HasValue)
            return ClientAccess.Denied;

        await using var command = CreateCommand(@"
            SELECT c.id,
                   c.tenant_id,
                   c.user_id,
                   EXISTS (
                       SELECT 1 FROM client_nutritionist_assignments a
                       WHERE a.client_id=c.id AND a.nutritionist_id=@user AND a.is_active=TRUE
                   ) AS assigned
            FROM clients c
            WHERE c.id=@client AND c.tenant_id=@tenant AND c.archived_at IS NULL;");
        Add(command, "client", clientId);
        Add(command, "tenant", tenantId.Value);
        Add(command, "user", userId.Value);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new ClientAccess(false, false, tenantId.Value);

        var owner = reader.GetInt32(2) == userId.Value;
        var assigned = reader.GetBoolean(3);
        var allowed = User.IsInRole("clinic_admin") || owner || assigned;
        return new ClientAccess(allowed, false, tenantId.Value);
    }

    private static async Task OpenConnectionAsync(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open) await connection.OpenAsync();
    }

    private DbCommand CreateCommand(string sql)
    {
        var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void AddArray(DbCommand command, string name, int[] values)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = values;
        command.Parameters.Add(parameter);
    }

    private sealed record ClientAccess(bool Allowed, bool NotFound, int TenantId)
    {
        public static ClientAccess Denied => new(false, false, 0);
    }
}

public sealed record SpecializationDto(
    int Id,
    string Code,
    string Name,
    string Category,
    string? Description,
    bool Enabled);

public sealed record TenantSpecializationUpdateDto(bool Enabled);

public sealed record ClientSpecializationDto(
    int SpecializationId,
    string Code,
    string Name,
    string Category,
    string? Description,
    string? Notes);

public sealed record ClientSpecializationUpdateDto(IReadOnlyList<ClientSpecializationItemDto> Items);

public sealed record ClientSpecializationItemDto(int SpecializationId, string? Notes);
