using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

/// <summary>Gestión global de cuentas de pacientes. Solo el superadministrador puede acceder.</summary>
[ApiController]
[Route("api/admin/patients")]
[Authorize(Roles = "superadmin")]
public sealed class AdminPatientsController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly IAuditLogService _audit;

    public AdminPatientsController(angulosodbContext context, IAuditLogService audit)
    {
        _context = context;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? search = null,
        [FromQuery] string status = "active",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        search = search?.Trim();
        if (search?.Length > 120) return BadRequest("La búsqueda no puede superar 120 caracteres.");
        if (status is not ("active" or "archived" or "all")) return BadRequest("Filtro de estado no válido.");

        var query = _context.clients.AsNoTracking().AsQueryable();
        if (status == "active") query = query.Where(c => c.archived_at == null);
        if (status == "archived") query = query.Where(c => c.archived_at != null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(c => c.full_name.ToLower().Contains(term) ||
                                     c.email.ToLower().Contains(term) ||
                                     c.phone.ToLower().Contains(term) ||
                                     c.id.ToString().Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.created_at).ThenBy(c => c.id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new AdminPatientDto(c.id, c.full_name, c.email, c.phone, c.tenant_id,
                c.created_at, c.archived_at, c.last_portal_access, c.portal_token_version))
            .ToListAsync(ct);

        return Ok(new { items, totalCount = total, page, pageSize });
    }

    /// <summary>Archivado reversible: bloquea el acceso y retira tokens push sin borrar el expediente.</summary>
    [HttpPost("{id:int}/archive")]
    public async Task<IActionResult> Archive(int id, CancellationToken ct)
    {
        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == id, ct);
        if (client == null) return NotFound();
        if (client.archived_at == null)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            client.archived_at = DateTime.UtcNow;
            client.portal_token_version = checked(client.portal_token_version + 1);
            client.access_token = null;
            client.access_token_expires_at = null;
            client.passcode_hash = null;
            await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM patient_push_tokens WHERE client_id = {id}", ct);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await _audit.LogAccessAsync("ARCHIVE_PATIENT_GLOBAL", "clients", id.ToString(), id, "Archivado por superadmin; acceso revocado y tokens push eliminados.");
        }
        return Ok(new { message = "Paciente archivado y acceso revocado.", id, archivedAt = client.archived_at });
    }

    /// <summary>
    /// Revoca el acceso y anonimiza los identificadores directos de la cuenta. No elimina automáticamente
    /// expedientes clínicos ni mensajes, cuya conservación/supresión requiere revisar la base jurídica aplicable.
    /// </summary>
    [HttpPost("{id:int}/erase")]
    public async Task<IActionResult> Erase(int id, [FromBody] ConfirmPatientErasureRequest request, CancellationToken ct)
    {
        if (request?.ConfirmId != id || request.ConfirmText != "ELIMINAR")
            return BadRequest("Para confirmar, envía el ID exacto del paciente y ConfirmText='ELIMINAR'.");

        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == id, ct);
        if (client == null) return NotFound();
        if (client.archived_at == null && client.full_name == "Cuenta eliminada")
            return Conflict("La cuenta ya está anonimizada.");

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        var suffix = $"{id}-{Guid.NewGuid():N}";
        client.full_name = "Cuenta eliminada";
        client.email = $"eliminado-{suffix}@privacy.invalid";
        client.phone = string.Empty;
        client.address = string.Empty;
        client.postal_code = string.Empty;
        client.city = string.Empty;
        client.province = string.Empty;
        client.country = string.Empty;
        client.latitude = null;
        client.longitude = null;
        client.birth_date = null;
        client.gender = string.Empty;
        client.notes = string.Empty;
        client.access_token = null;
        client.access_token_expires_at = null;
        client.passcode_hash = null;
        client.last_portal_access = null;
        client.archived_at ??= DateTime.UtcNow;
        client.portal_token_version = checked(client.portal_token_version + 1);
        await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM patient_push_tokens WHERE client_id = {id}", ct);
        await _context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _audit.LogAccessAsync("ANONYMIZE_PATIENT_ACCOUNT", "clients", id.ToString(), id,
            "Solicitud de supresión: identificadores directos y acceso anonimizados/revocados; revisar por separado datos clínicos, mensajes, documentos, copias y obligaciones de conservación.");
        return Ok(new
        {
            id,
            status = "account_anonymized",
            message = "Se han anonimizado los identificadores directos de la ficha, revocado el acceso y eliminado los tokens push. La operación no declara suprimidos los datos clínicos, mensajes, documentos, registros de auditoría ni copias de seguridad: deben revisarse según la base jurídica y el plazo de conservación aplicables."
        });
    }
}

public sealed record AdminPatientDto(
    int Id, string FullName, string Email, string Phone, int? TenantId,
    DateTime? CreatedAt, DateTime? ArchivedAt, DateTime? LastPortalAccess, int PortalTokenVersion);

public sealed class ConfirmPatientErasureRequest
{
    public int ConfirmId { get; set; }
    public string ConfirmText { get; set; } = string.Empty;
}
