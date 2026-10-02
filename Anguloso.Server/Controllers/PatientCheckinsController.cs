using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/portal/check-ins")]
[Authorize(Roles = "patient")]
public class PatientCheckinsController : ControllerBase
{
    private readonly angulosodbContext _db;

    public PatientCheckinsController(angulosodbContext db) => _db = db;

    [HttpGet("current")]
    public async Task<ActionResult<object>> GetCurrent()
    {
        if (!TryGetClientId(out var clientId)) return Unauthorized();
        var weekStart = GetWeekStart(DateOnly.FromDateTime(DateTime.UtcNow));
        var item = await _db.patient_checkins.AsNoTracking()
            .Where(x => x.client_id == clientId && x.week_start == weekStart)
            .Select(x => new { x.id, x.week_start, x.submitted_at, x.weight, x.adherence, x.hunger, x.difficulties, x.notes })
            .SingleOrDefaultAsync();
        return Ok(item);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetHistory()
    {
        if (!TryGetClientId(out var clientId)) return Unauthorized();
        var items = await _db.patient_checkins.AsNoTracking()
            .Where(x => x.client_id == clientId)
            .OrderByDescending(x => x.week_start)
            .Select(x => new { x.id, x.week_start, x.submitted_at, x.weight, x.adherence, x.hunger, x.difficulties, x.notes })
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<object>> Save([FromBody] PatientCheckinRequest request)
    {
        if (!TryGetClientId(out var clientId)) return Unauthorized();
        if (request == null) return BadRequest(new { message = "Datos no válidos." });
        if (request.adherence is < 0 or > 100) return BadRequest(new { message = "La adherencia debe estar entre 0 y 100." });
        if (request.hunger is < 0 or > 10) return BadRequest(new { message = "El nivel indicado debe estar entre 0 y 10." });
        if (request.weight is <= 0 or > 500) return BadRequest(new { message = "El valor de peso no es válido." });
        if (request.difficulties?.Length > 1000) return BadRequest(new { message = "Las dificultades no pueden superar los 1000 caracteres." });
        if (request.notes?.Length > 2000) return BadRequest(new { message = "El comentario no puede superar los 2000 caracteres." });

        var client = await _db.clients.AsNoTracking().Where(x => x.id == clientId && x.archived_at == null).Select(x => new { x.id, x.tenant_id, UserTenantId = x.user != null ? x.user.tenant_id : null }).SingleOrDefaultAsync();
        if (client == null) return NotFound(new { message = "Paciente no disponible." });
        var tenantId = client.tenant_id ?? client.UserTenantId;
        if (!tenantId.HasValue) return BadRequest(new { message = "El paciente no tiene una organización asociada." });

        var weekStart = GetWeekStart(DateOnly.FromDateTime(DateTime.UtcNow));
        var item = await _db.patient_checkins.SingleOrDefaultAsync(x => x.client_id == clientId && x.week_start == weekStart);
        if (item == null)
        {
            item = new patient_checkins { client_id = clientId, tenant_id = tenantId.Value, week_start = weekStart };
            _db.patient_checkins.Add(item);
        }
        item.submitted_at = DateTime.UtcNow;
        item.weight = request.weight;
        item.adherence = request.adherence;
        item.hunger = request.hunger;
        item.difficulties = request.difficulties?.Trim();
        item.notes = request.notes?.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { item.id, item.week_start, item.submitted_at, item.weight, item.adherence, item.hunger, item.difficulties, item.notes });
    }

    private bool TryGetClientId(out int clientId) => int.TryParse(User.FindFirstValue("clientId"), out clientId);

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-diff);
    }

    public sealed class PatientCheckinRequest
    {
        public double? weight { get; set; }
        public int? adherence { get; set; }
        public int? hunger { get; set; }
        public string? difficulties { get; set; }
        public string? notes { get; set; }
    }
}
