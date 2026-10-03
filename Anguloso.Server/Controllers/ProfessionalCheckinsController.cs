using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/professional/check-ins")]
[Authorize(Policy = "Professional")]
public sealed class ProfessionalCheckinsController : ControllerBase
{
    private readonly angulosodbContext _db;
    private readonly AutomationService _automation;
    private readonly ITenantContextService _tenantContext;
    private readonly IConfiguration _configuration;

    public ProfessionalCheckinsController(
        angulosodbContext db,
        AutomationService automation,
        ITenantContextService tenantContext,
        IConfiguration configuration)
    {
        _db = db;
        _automation = automation;
        _tenantContext = tenantContext;
        _configuration = configuration;
    }

    [HttpGet]
    // Un profesional solo puede consultar check-ins de pacientes que tiene asignados activamente dentro de su tenant.
    public async Task<ActionResult<IReadOnlyList<ProfessionalCheckinDto>>> Get(
        [FromQuery] bool pendingOnly = true,
        [FromQuery] int limit = 100,
        [FromQuery] int? clientId = null)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.UserId.HasValue)
            return BadRequest(new { message = "La cuenta no tiene organización o usuario." });

        limit = Math.Clamp(limit, 1, 200);

        var query = _db.patient_checkins.AsNoTracking()
            .Where(c => c.tenant_id == _tenantContext.TenantId.Value &&
                        (!clientId.HasValue || c.client_id == clientId.Value) &&
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
                Energy = c.energy,
                SleepQuality = c.sleep_quality,
                SleepHours = c.sleep_hours,
                Training = c.training,
                Difficulties = c.difficulties,
                Notes = c.notes,
                ReviewedAt = c.reviewed_at,
                ReviewedByUserId = c.reviewed_by_user_id
            })
            .ToListAsync();

        return Ok(result);
    }

    [HttpPost("{id:int}/follow-up-task")]
    // Convierte las señales detectadas del check-in en una tarea operativa, manteniendo tenant y asignación del paciente en servidor.
    public async Task<ActionResult<object>> CreateFollowUpTask(int id)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.UserId.HasValue)
            return BadRequest(new { message = "La cuenta no tiene organización o usuario." });

        var checkins = await _db.patient_checkins.AsNoTracking()
            .Where(c => c.tenant_id == _tenantContext.TenantId.Value &&
                        c.client.archived_at == null &&
                        _db.client_nutritionist_assignments.Any(a =>
                            a.client_id == c.client_id &&
                            a.nutritionist_id == _tenantContext.UserId.Value &&
                            a.is_active))
            .OrderByDescending(c => c.submitted_at)
            .Where(c => c.client_id == _db.patient_checkins.Where(x => x.id == id).Select(x => x.client_id).FirstOrDefault())
            .Take(4)
            .Select(c => new
            {
                c.id, c.client_id, c.weight, c.adherence, c.hunger, c.energy,
                c.sleep_quality, c.sleep_hours, c.training, c.submitted_at,
                ClientName = c.client.full_name
            })
            .ToListAsync();

        var latest = checkins.FirstOrDefault(c => c.id == id);
        if (latest == null) return NotFound();

        var previous = checkins.Where(c => c.id != id)
            .OrderByDescending(c => c.submitted_at)
            .FirstOrDefault();

        var selectedMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "adherence", "hunger", "energy", "sleep_quality", "sleep_hours", "training", "weight"
        };
        var thresholds = new Dictionary<string, FollowupThreshold>(StringComparer.OrdinalIgnoreCase);

        await using (var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT selected_metrics, thresholds
                FROM professional_followup_settings
                WHERE tenant_id=@tenant AND user_id=@user
                LIMIT 1;
                """, connection);
            command.Parameters.AddWithValue("tenant", _tenantContext.TenantId.Value);
            command.Parameters.AddWithValue("user", _tenantContext.UserId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var configuredMetrics = System.Text.Json.JsonSerializer.Deserialize<string[]>(
                    reader.GetFieldValue<string>(0));
                if (configuredMetrics is { Length: > 0 })
                    selectedMetrics = configuredMetrics
                        .Where(x => x is "adherence" or "hunger" or "energy" or "sleep_quality" or "sleep_hours" or "training" or "weight")
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var configuredThresholds = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, FollowupThreshold>>(
                    reader.GetFieldValue<string>(1));
                if (configuredThresholds != null)
                    thresholds = new Dictionary<string, FollowupThreshold>(configuredThresholds, StringComparer.OrdinalIgnoreCase);
            }
        }

        var signals = new List<string>();
        void Compare(string metric, string label, double? current, double? previousValue,
            double? defaultLow = null, double? defaultHigh = null, double? defaultDelta = null)
        {
            if (!selectedMetrics.Contains(metric) || !current.HasValue) return;
            var rule = thresholds.TryGetValue(metric, out var configured)
                ? configured
                : new FollowupThreshold();
            var low = configured?.Low ?? defaultLow;
            var high = configured?.High ?? defaultHigh;
            var deltaDrop = configured?.Drop ?? defaultDelta;
            var deltaRise = configured?.Rise ?? defaultDelta;

            if (low.HasValue && current.Value <= low.Value) signals.Add($"{label}: {current.Value:0.0} (bajo)");
            else if (high.HasValue && current.Value >= high.Value) signals.Add($"{label}: {current.Value:0.0} (alto)");

            if (previousValue.HasValue)
            {
                var change = current.Value - previousValue.Value;
                if (deltaDrop.HasValue && change <= -deltaDrop.Value) signals.Add($"{label}: descenso {Math.Abs(change):0.0}");
                if (deltaRise.HasValue && change >= deltaRise.Value) signals.Add($"{label}: aumento {change:0.0}");
            }
        }

        Compare("adherence", "Adherencia", latest.adherence, previous?.adherence, defaultLow: 5, defaultDelta: 2);
        Compare("hunger", "Hambre", latest.hunger, previous?.hunger, defaultHigh: 8, defaultDelta: 2);
        Compare("energy", "Energía", latest.energy, previous?.energy, defaultLow: 4, defaultDelta: 2);
        Compare("sleep_quality", "Calidad del sueño", latest.sleep_quality, previous?.sleep_quality, defaultLow: 4, defaultDelta: 2);
        Compare("sleep_hours", "Horas de sueño", latest.sleep_hours, previous?.sleep_hours, defaultLow: 6, defaultDelta: 1.5);
        Compare("training", "Entrenamiento", latest.training, previous?.training, defaultLow: 2, defaultDelta: 3);

        if (selectedMetrics.Contains("weight") && latest.weight.HasValue && previous?.weight.HasValue == true && previous.weight.Value > 0)
        {
            var percentage = ((latest.weight.Value - previous.weight.Value) / previous.weight.Value) * 100;
            var weightRule = thresholds.TryGetValue("weight", out var configuredWeight)
                ? configuredWeight
                : new FollowupThreshold();
            var changeThreshold = weightRule.Drop ?? weightRule.Rise ?? 2;
            if (Math.Abs(percentage) >= changeThreshold)
                signals.Add($"Peso: {(percentage > 0 ? "+" : "")}{percentage:0.0}%");
        }

        if (signals.Count == 0)
            return BadRequest(new { message = "No hay señales de seguimiento que requieran una tarea." });

        var title = $"Revisar seguimiento de {latest.ClientName}";
        var description = $"Señales detectadas en el último check-in: {string.Join("; ", signals.Take(6))}. Revisar con el paciente y valorar si procede ajustar el plan.";
        var taskId = await _automation.CreateProfessionalTaskAsync(
            _tenantContext.TenantId.Value,
            new ProfessionalTaskCreateRequest
            {
                ClientId = latest.client_id,
                AssignedUserId = _tenantContext.UserId.Value,
                Title = title,
                Description = description,
                DueAt = DateTime.UtcNow.AddDays(1),
                Priority = signals.Count >= 3 ? "high" : "normal"
            },
            "followup.signal",
            $"followup-signal:{_tenantContext.TenantId.Value}:{latest.id}:{_tenantContext.UserId.Value}");

        return Ok(new { id = taskId });
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

    private sealed class FollowupThreshold
    {
        public double? Low { get; set; }
        public double? High { get; set; }
        public double? Drop { get; set; }
        public double? Rise { get; set; }
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
        public int? Energy { get; set; }
        public int? SleepQuality { get; set; }
        public double? SleepHours { get; set; }
        public int? Training { get; set; }
        public string? Difficulties { get; set; }
        public string? Notes { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
    }
}
