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
                Barriers = c.barriers,
                Notes = c.notes,
                ReviewedAt = c.reviewed_at,
                ReviewedByUserId = c.reviewed_by_user_id
            })
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("attention")]
    // Panel global de atención: solo muestra patrones repetidos o semanas completas sin check-in, con reglas deterministas.
    public async Task<ActionResult<IReadOnlyList<FollowupAttentionDto>>> GetAttention([FromQuery] int limit = 8)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.UserId.HasValue)
            return BadRequest(new { message = "La cuenta no tiene organización o usuario." });
        limit = Math.Clamp(limit, 1, 30);

        var rows = new List<AttentionCheckinRow>();
        await using (var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                WITH ranked AS (
                    SELECT c.id, c.client_id, cl.full_name, c.week_start, c.submitted_at,
                           c.adherence, c.hunger, c.energy, c.sleep_quality, c.reviewed_at,
                           ROW_NUMBER() OVER (
                               PARTITION BY c.client_id
                               ORDER BY c.week_start DESC, c.submitted_at DESC
                           ) AS row_number
                    FROM patient_checkins c
                    JOIN clients cl ON cl.id=c.client_id
                    WHERE c.tenant_id=@tenant AND cl.archived_at IS NULL
                      AND EXISTS (
                          SELECT 1 FROM client_nutritionist_assignments a
                          WHERE a.client_id=c.client_id
                            AND a.nutritionist_id=@user AND a.is_active
                      )
                )
                SELECT id, client_id, full_name, week_start, submitted_at,
                       adherence, hunger, energy, sleep_quality, reviewed_at
                FROM ranked
                WHERE row_number <= 4
                ORDER BY client_id, week_start ASC, submitted_at ASC;
                """, connection);
            command.Parameters.AddWithValue("tenant", _tenantContext.TenantId.Value);
            command.Parameters.AddWithValue("user", _tenantContext.UserId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new AttentionCheckinRow(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
                    reader.GetFieldValue<DateOnly>(3), reader.GetDateTime(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetDateTime(9)));
            }
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentWeekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var attention = new List<FollowupAttentionDto>();
        foreach (var group in rows.GroupBy(row => row.ClientId))
        {
            var history = group.OrderBy(row => row.WeekStart).ThenBy(row => row.SubmittedAt).ToArray();
            if (history.Length == 0) continue;
            var latest = history[^1];
            var previous = history.Length > 1 ? history[^2] : null;
            var signals = new List<string>();
            var missedWeeks = Math.Max(0, (currentWeekStart.DayNumber - latest.WeekStart.DayNumber) / 7 - 1);
            if (missedWeeks > 0)
                signals.Add($"Sin check-in en {missedWeeks} semana(s) completa(s)");

            if (latest.Adherence.HasValue && previous?.Adherence.HasValue == true &&
                latest.Adherence.Value <= 60 && previous.Adherence.Value <= 60)
                signals.Add($"Adherencia baja en dos revisiones ({previous.Adherence}% y {latest.Adherence}%)");

            if (latest.Hunger.HasValue && previous?.Hunger.HasValue == true &&
                latest.Hunger.Value >= 8 && previous.Hunger.Value >= 8)
                signals.Add($"Hambre alta en dos revisiones ({previous.Hunger}/10 y {latest.Hunger}/10)");

            if (latest.Energy.HasValue && previous?.Energy.HasValue == true &&
                latest.Energy.Value <= 4 && previous.Energy.Value <= 4)
                signals.Add($"Energía baja en dos revisiones ({previous.Energy}/10 y {latest.Energy}/10)");

            if (latest.SleepQuality.HasValue && previous?.SleepQuality.HasValue == true &&
                latest.SleepQuality.Value <= 4 && previous.SleepQuality.Value <= 4)
                signals.Add($"Calidad del sueño baja en dos revisiones ({previous.SleepQuality}/10 y {latest.SleepQuality}/10)");

            if (history.Length >= 3)
            {
                var lastThree = history.TakeLast(3).ToArray();
                if (lastThree.All(x => x.Adherence.HasValue) &&
                    lastThree[0].Adherence!.Value > lastThree[1].Adherence!.Value &&
                    lastThree[1].Adherence!.Value > lastThree[2].Adherence!.Value &&
                    lastThree[0].Adherence!.Value - lastThree[2].Adherence!.Value >= 15)
                    signals.Add($"Adherencia en descenso durante tres revisiones ({lastThree[0].Adherence}% → {lastThree[2].Adherence}%)");
            }

            if (signals.Count == 0) continue;
            attention.Add(new FollowupAttentionDto
            {
                ClientId = latest.ClientId,
                ClientName = latest.ClientName,
                LastCheckinAt = latest.SubmittedAt,
                LastWeekStart = latest.WeekStart,
                Signals = signals,
                Priority = signals.Count >= 2 || latest.Adherence <= 40 ? "high" : "normal"
            });
        }

        return Ok(attention
            .OrderByDescending(item => item.Priority == "high")
            .ThenByDescending(item => item.Signals.Count)
            .ThenByDescending(item => item.LastCheckinAt)
            .Take(limit)
            .ToList());
    }

    private sealed record AttentionCheckinRow(
        int Id, int ClientId, string ClientName, DateOnly WeekStart, DateTime SubmittedAt,
        int? Adherence, int? Hunger, int? Energy, int? SleepQuality, DateTime? ReviewedAt);

    public sealed class FollowupAttentionDto
    {
        public int ClientId { get; set; }
        public string ClientName { get; set; } = "";
        public DateTime LastCheckinAt { get; set; }
        public DateOnly LastWeekStart { get; set; }
        public List<string> Signals { get; set; } = new();
        public string Priority { get; set; } = "normal";
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
                c.id, c.client_id, c.week_start, c.weight, c.adherence, c.hunger, c.energy,
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

        // La API almacena adherencia como porcentaje (0–100), a diferencia de las escalas 0–10 del resto de métricas.
        Compare("adherence", "Adherencia (%)", latest.adherence, previous?.adherence, defaultLow: 60, defaultDelta: 15);
        Compare("hunger", "Hambre", latest.hunger, previous?.hunger, defaultHigh: 8, defaultDelta: 2);
        Compare("energy", "Energía", latest.energy, previous?.energy, defaultLow: 4, defaultDelta: 2);
        Compare("sleep_quality", "Calidad del sueño", latest.sleep_quality, previous?.sleep_quality, defaultLow: 4, defaultDelta: 2);
        Compare("sleep_hours", "Horas de sueño", latest.sleep_hours, previous?.sleep_hours, defaultLow: 6, defaultDelta: 1.5);
        Compare("training", "Entrenamiento", latest.training, previous?.training, defaultLow: 2, defaultDelta: 3);

        var third = checkins.Where(c => c.id != id)
            .OrderByDescending(c => c.submitted_at)
            .Skip(1)
            .FirstOrDefault();
        if (selectedMetrics.Contains("adherence") && latest.adherence.HasValue &&
            previous?.adherence.HasValue == true && third?.adherence.HasValue == true &&
            third.adherence.Value > previous.adherence.Value &&
            previous.adherence.Value > latest.adherence.Value &&
            third.adherence.Value - latest.adherence.Value >= 15)
            signals.Add($"Adherencia en descenso persistente: {third.adherence}% → {previous.adherence}% → {latest.adherence}%");

        if (selectedMetrics.Contains("hunger") && latest.hunger.HasValue &&
            previous?.hunger.HasValue == true && third?.hunger.HasValue == true &&
            third.hunger.Value < previous.hunger.Value &&
            previous.hunger.Value < latest.hunger.Value &&
            latest.hunger.Value - third.hunger.Value >= 2)
            signals.Add($"Hambre en aumento persistente: {third.hunger}/10 → {previous.hunger}/10 → {latest.hunger}/10");

        if (selectedMetrics.Contains("energy") && latest.energy.HasValue &&
            previous?.energy.HasValue == true && third?.energy.HasValue == true &&
            third.energy.Value > previous.energy.Value &&
            previous.energy.Value > latest.energy.Value &&
            third.energy.Value - latest.energy.Value >= 2)
            signals.Add($"Energía en descenso persistente: {third.energy}/10 → {previous.energy}/10 → {latest.energy}/10");

        if (selectedMetrics.Contains("sleep_quality") && latest.sleep_quality.HasValue &&
            previous?.sleep_quality.HasValue == true && third?.sleep_quality.HasValue == true &&
            third.sleep_quality.Value > previous.sleep_quality.Value &&
            previous.sleep_quality.Value > latest.sleep_quality.Value &&
            third.sleep_quality.Value - latest.sleep_quality.Value >= 2)
            signals.Add($"Calidad del sueño en descenso persistente: {third.sleep_quality}/10 → {previous.sleep_quality}/10 → {latest.sleep_quality}/10");

        var currentWeekStart = DateOnly.FromDateTime(DateTime.UtcNow);
        currentWeekStart = currentWeekStart.AddDays(-(((int)currentWeekStart.DayOfWeek + 6) % 7));
        var missedWeeks = Math.Max(0, (currentWeekStart.DayNumber - latest.week_start.DayNumber) / 7 - 1);
        if (missedWeeks > 0) signals.Add($"Sin check-in en {missedWeeks} semana(s) completa(s)");

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
        public string[]? Barriers { get; set; }
        public string? Notes { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public int? ReviewedByUserId { get; set; }
    }
}
