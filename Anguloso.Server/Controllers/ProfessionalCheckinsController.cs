using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/professional/check-ins")]
[Authorize(Policy = "Professional")]
public sealed class ProfessionalCheckinsController : ControllerBase
{
    private readonly angulosodbContext _db;
    private readonly AutomationService _automation;
    private readonly ITenantContextService _tenantContext;

    public ProfessionalCheckinsController(
        angulosodbContext db,
        AutomationService automation,
        ITenantContextService tenantContext)
    {
        _db = db;
        _automation = automation;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    // Un profesional solo puede consultar check-ins de pacientes que tiene asignados activamente dentro de su tenant.
    public async Task<ActionResult<IReadOnlyList<ProfessionalCheckinDto>>> Get(
        [FromQuery] bool pendingOnly = true,
        [FromQuery] int limit = 100)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.UserId.HasValue)
            return BadRequest(new { message = "La cuenta no tiene organización o usuario." });

        limit = Math.Clamp(limit, 1, 200);

        var query = _db.patient_checkins.AsNoTracking()
            .Where(c => c.tenant_id == _tenantContext.TenantId.Value &&
                        c.client.archived_at == null &&
                        _db.client_nutritionist_assignments.Any(a =>
                            a.client_id == c.client_id &&
                            a.nutritionist_id == _tenantContext.UserId.Value &&
                            a.is_active));

        if (pendingOnly)
            query = query.Where(c => c.reviewed_at == null);

        var result = await query
            .OrderByDescending(c => c.submitted_at)
            .Take(limit)
            .Select(c => new ProfessionalCheckinDto
            {
                Id = c.id,
                ClientId = c.client_id,
                ClientName = c.client.full_name,
                WeekStart = c.week_start,
                SubmittedAt = c.submitted_at,
                Weight = c.weight,
                Adherence = c.adherence,
                Hunger = c.hunger,
                Difficulties = c.difficulties,
                Notes = c.notes,
                ReviewedAt = c.reviewed_at,
                ReviewedByUserId = c.reviewed_by_user_id
            })
            .ToListAsync();

        return Ok(result);
    }

    [HttpPost("{id:int}/review")]
    // La revisión vuelve a comprobar tenant y asignación antes de delegar el cambio al servicio de automatización.
    public async Task<IActionResult> Review(int id)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.UserId.HasValue)
            return BadRequest(new { message = "La cuenta no tiene organización o usuario." });

        var checkin = await _db.patient_checkins.AsNoTracking()
            .Where(c => c.id == id &&
                        c.tenant_id == _tenantContext.TenantId.Value &&
                        c.client.archived_at == null &&
                        _db.client_nutritionist_assignments.Any(a =>
                            a.client_id == c.client_id &&
                            a.nutritionist_id == _tenantContext.UserId.Value &&
                            a.is_active))
            .Select(c => new { c.id, c.reviewed_at })
            .SingleOrDefaultAsync();

        if (checkin == null) return NotFound();
        if (checkin.reviewed_at.HasValue) return NoContent();

        // La revisión delega en el servicio de automatizaciones para que el cambio de estado y sus efectos derivados mantengan una única regla de negocio.
        // El servicio vuelve a comprobar el estado dentro de su transacción para resolver carreras entre dos revisiones simultáneas.
        var reviewed = await _automation.ReviewPatientCheckinAsync(
            _tenantContext.TenantId.Value,
            id,
            _tenantContext.UserId.Value);

        return reviewed ? NoContent() : Conflict(new { message = "El check-in ya fue revisado por otro profesional." });
    }

    public sealed class ProfessionalCheckinDto
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public string? ClientName { get; set; }
        public DateOnly WeekStart { get; set; }
        public DateTime SubmittedAt { get; set; }
        public double? Weight { get; set; }
        public int? Adherence { get; set; }
        public int? Hunger { get; set; }
        public string? Difficulties { get; set; }
        public string? Notes { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
    }
}
