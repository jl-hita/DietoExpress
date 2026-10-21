using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Claims;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/portal/check-ins")]
[Authorize(Roles = "patient")]
public class PatientCheckinsController : ControllerBase
{
    private readonly angulosodbContext _db;
    private readonly AutomationService _automationService;
    private static readonly HashSet<string> AllowedBarriers = new(StringComparer.Ordinal)
    {
        "time", "hunger", "social", "cooking", "budget", "planning", "cravings", "emotions", "travel", "other"
    };

    public PatientCheckinsController(angulosodbContext db, AutomationService automationService)
    {
        _db = db;
        _automationService = automationService;
    }

    [HttpGet("eligibility")]
    public async Task<IActionResult> GetEligibility()
    {
        if (!TryGetClientId(out var clientId)) return Unauthorized();
        var eligible = await _db.patient_appointments.AsNoTracking()
            .AnyAsync(a => a.client_id == clientId && a.status == "completed");
        return Ok(new
        {
            eligible,
            message = eligible ? null : "La revisión semanal estará disponible después de tu primera consulta."
        });
    }

    [HttpGet("current")]
    public async Task<ActionResult<object>> GetCurrent()
    {
        if (!TryGetClientId(out var clientId)) return Unauthorized();
        var weekStart = GetWeekStart(DateOnly.FromDateTime(DateTime.UtcNow));
        var item = await _db.patient_checkins.AsNoTracking()
            .Where(x => x.client_id == clientId && x.week_start == weekStart)
            .Select(x => new { x.id, x.week_start, x.submitted_at, x.weight, x.adherence, x.hunger, x.energy, x.sleep_quality, x.sleep_hours, x.training, x.difficulties, x.barriers, x.notes })
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
    // Cada paciente puede enviar una sola revisión semanal; los duplicados se rechazan en API y base de datos.
    public async Task<ActionResult<object>> Save([FromBody] PatientCheckinRequest request)
    {
        if (!TryGetClientId(out var clientId)) return Unauthorized();
        if (request == null) return BadRequest(new { message = "Datos no válidos." });
        if (request.adherence is < 0 or > 100) return BadRequest(new { message = "La adherencia debe estar entre 0 y 100." });
        if (request.hunger is < 0 or > 10) return BadRequest(new { message = "El nivel de hambre debe estar entre 0 y 10." });
        if (request.energy is < 0 or > 10) return BadRequest(new { message = "El nivel de energía debe estar entre 0 y 10." });
        if (request.sleep_quality is < 0 or > 10) return BadRequest(new { message = "La calidad del sueño debe estar entre 0 y 10." });
        if (request.sleep_hours is < 0 or > 24) return BadRequest(new { message = "Las horas de sueño deben estar entre 0 y 24." });
        if (request.training is < 0 or > 10) return BadRequest(new { message = "El nivel de entrenamiento debe estar entre 0 y 10." });
        if (request.weight is <= 0 or > 500) return BadRequest(new { message = "El valor de peso no es válido." });
        if (request.difficulties?.Length > 1000) return BadRequest(new { message = "Las dificultades no pueden superar los 1000 caracteres." });
        if (request.barriers?.Length > 10 || request.barriers?.Any(x => !AllowedBarriers.Contains(x)) == true)
            return BadRequest(new { message = "Selecciona únicamente barreras de seguimiento válidas (máximo 10)." });
        if (request.notes?.Length > 2000) return BadRequest(new { message = "El comentario no puede superar los 2000 caracteres." });

        var client = await _db.clients.AsNoTracking().Where(x => x.id == clientId && x.archived_at == null).Select(x => new { x.id, x.tenant_id, UserTenantId = x.user != null ? x.user.tenant_id : null }).SingleOrDefaultAsync();
        if (client == null) return NotFound(new { message = "Paciente no disponible." });
        var tenantId = client.tenant_id ?? client.UserTenantId;
        if (!tenantId.HasValue) return BadRequest(new { message = "El paciente no tiene una organización asociada." });

        var hasCompletedConsultation = await _db.patient_appointments.AsNoTracking()
            .AnyAsync(a => a.client_id == clientId && a.tenant_id == tenantId.Value && a.status == "completed");
        if (!hasCompletedConsultation)
            return Conflict(new { message = "La revisión semanal estará disponible después de tu primera consulta." });

        var weekStart = GetWeekStart(DateOnly.FromDateTime(DateTime.UtcNow));
        var alreadySubmitted = await _db.patient_checkins.AsNoTracking()
            .AnyAsync(x => x.client_id == clientId && x.tenant_id == tenantId.Value && x.week_start == weekStart);
        if (alreadySubmitted)
            return Conflict(new { message = "Ya has enviado tu revisión semanal. Podrás rellenarla de nuevo la próxima semana." });

        var item = new patient_checkins { client_id = clientId, tenant_id = tenantId.Value, week_start = weekStart };
        item.submitted_at = DateTime.UtcNow;
        item.weight = request.weight;
        item.adherence = request.adherence;
        item.hunger = request.hunger;
        item.energy = request.energy;
        item.sleep_quality = request.sleep_quality;
        item.sleep_hours = request.sleep_hours;
        item.training = request.training;
        item.difficulties = request.difficulties?.Trim();
        item.barriers = request.barriers?.Distinct().ToArray() ?? Array.Empty<string>();
        item.notes = request.notes?.Trim();
        _db.patient_checkins.Add(item);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // La restricción única (cliente, semana) protege también frente a dos envíos simultáneos.
            _db.Entry(item).State = EntityState.Detached;
            return Conflict(new { message = "Ya has enviado tu revisión semanal. Podrás rellenarla de nuevo la próxima semana." });
        }

        var nutritionistId = await _db.client_nutritionist_assignments.AsNoTracking()
            .Where(a => a.client_id == clientId && a.is_active && a.nutritionist.tenant_id == tenantId.Value)
            .OrderByDescending(a => a.assigned_at)
            .Select(a => (int?)a.nutritionist_id)
            .FirstOrDefaultAsync();

        // El evento de automatización se publica después de guardar el check-in. Un fallo del motor no invalida el dato clínico.
        // La clave incluye el momento de envío porque un mismo check-in puede actualizarse durante la semana y cada envío
        // debe poder disparar la revisión correspondiente sin crear duplicados del mismo envío.
        try
        {
            await _automationService.PublishEventAsync(
                tenantId.Value,
                "patient.checkin.submitted",
                "patient_checkin",
                item.id.ToString(),
                new AutomationService.CheckinSubmittedPayload(clientId, nutritionistId),
                $"checkin:{item.id}:submitted:{item.submitted_at.Ticks}");
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<PatientCheckinsController>>()
                .LogError(ex, "No se pudo registrar la automatización del check-in {CheckinId}.", item.id);
        }

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
        public int? energy { get; set; }
        public int? sleep_quality { get; set; }
        public double? sleep_hours { get; set; }
        public int? training { get; set; }
        public string? difficulties { get; set; }
        public string[]? barriers { get; set; }
        public string? notes { get; set; }
    }
}
