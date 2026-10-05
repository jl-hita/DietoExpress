using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Estadísticas operativas agregadas para profesionales y administradores de clínica.
/// No persiste métricas derivadas: calcula cada lectura desde las fuentes de verdad del tenant.
/// </summary>
[ApiController]
[Route("api/professional/statistics")]
[Authorize(Roles = "nutritionist,clinic_admin")]
public sealed class ProfessionalStatisticsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public ProfessionalStatisticsController(IConfiguration configuration) => _configuration = configuration;

    [HttpGet]
    public async Task<ActionResult<ProfessionalStatisticsDto>> Get(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        var userId = AuthHelpers.GetUserId(User);
        var isClinicAdmin = User.IsInRole("clinic_admin");

        if (!tenantId.HasValue || (!isClinicAdmin && !userId.HasValue))
            return Unauthorized();

        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = from ?? end.AddDays(-29);
        if (end < start)
            return BadRequest(new { message = "El periodo no es válido." });
        if (end.DayNumber - start.DayNumber > 365)
            return BadRequest(new { message = "El periodo máximo es de 366 días." });

        var fromUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusiveUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();

        var result = new ProfessionalStatisticsDto
        {
            From = start,
            To = end,
            Scope = isClinicAdmin ? "clinic" : "nutritionist"
        };

        var activePatientScope = isClinicAdmin
            ? ""
            : """
              AND EXISTS (
                  SELECT 1 FROM client_nutritionist_assignments ca
                  WHERE ca.client_id = c.id
                    AND ca.nutritionist_id = @user
                    AND ca.is_active
              )
              """;

        var periodPatientScope = isClinicAdmin
            ? ""
            : """
              AND EXISTS (
                  SELECT 1 FROM client_nutritionist_assignments ca
                  WHERE ca.client_id = c.id
                    AND ca.nutritionist_id = @user
                    AND ca.assigned_at < @to
                    AND (ca.unassigned_at IS NULL OR ca.unassigned_at >= @from)
              )
              """;

        await using (var command = new NpgsqlCommand($"""
            SELECT
                COUNT(*) FILTER (WHERE c.archived_at IS NULL)::int,
                COUNT(*) FILTER (WHERE c.created_at >= @from AND c.created_at < @to)::int,
                COUNT(*) FILTER (WHERE c.archived_at >= @from AND c.archived_at < @to)::int
            FROM clients c
            WHERE c.tenant_id=@tenant
              {activePatientScope};
            """, connection))
        {
            AddCommonParameters(command, tenantId.Value, userId, fromUtc, toExclusiveUtc);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result.ActivePatients = reader.GetInt32(0);
                result.NewPatients = reader.GetInt32(1);
                result.ArchivedPatients = reader.GetInt32(2);
            }
        }

        var appointmentScope = isClinicAdmin
            ? "AND pa.tenant_id=@tenant"
            : "AND pa.tenant_id=@tenant AND pa.nutritionist_id=@user";

        await using (var command = new NpgsqlCommand($"""
            SELECT
                COUNT(*) FILTER (WHERE pa.status='completed')::int,
                COUNT(*) FILTER (WHERE pa.status='no_show')::int,
                COUNT(*) FILTER (WHERE pa.status='cancelled')::int,
                COUNT(*)::int
            FROM patient_appointments pa
            JOIN clients c ON c.id=pa.client_id AND c.tenant_id=pa.tenant_id
            WHERE pa.starts_at >= @from AND pa.starts_at < @to
              AND c.archived_at IS NULL
              {appointmentScope};
            """, connection))
        {
            AddCommonParameters(command, tenantId.Value, userId, fromUtc, toExclusiveUtc);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result.CompletedAppointments = reader.GetInt32(0);
                result.NoShowAppointments = reader.GetInt32(1);
                result.CancelledAppointments = reader.GetInt32(2);
                result.TotalAppointments = reader.GetInt32(3);
            }
        }

        var checkinScope = isClinicAdmin
            ? "AND pc.tenant_id=@tenant"
            : """
              AND pc.tenant_id=@tenant
              AND EXISTS (
                  SELECT 1 FROM client_nutritionist_assignments ca
                  WHERE ca.client_id=pc.client_id
                    AND ca.nutritionist_id=@user
                    AND ca.assigned_at <= pc.submitted_at
                    AND (ca.unassigned_at IS NULL OR ca.unassigned_at >= pc.submitted_at)
              )
              """;

        await using (var command = new NpgsqlCommand($"""
            SELECT
                COUNT(*)::int,
                COUNT(*) FILTER (WHERE pc.reviewed_at IS NOT NULL)::int,
                AVG(pc.adherence)::double precision
            FROM patient_checkins pc
            JOIN clients c ON c.id=pc.client_id AND c.tenant_id=pc.tenant_id
            WHERE pc.submitted_at >= @from AND pc.submitted_at < @to
              AND c.archived_at IS NULL
              {checkinScope};
            """, connection))
        {
            AddCommonParameters(command, tenantId.Value, userId, fromUtc, toExclusiveUtc);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result.Checkins = reader.GetInt32(0);
                result.ReviewedCheckins = reader.GetInt32(1);
                result.AverageAdherence = reader.IsDBNull(2) ? null : reader.GetDouble(2);
            }
        }

        if (isClinicAdmin)
        {
            await using var command = new NpgsqlCommand("""
                SELECT
                    COUNT(*) FILTER (WHERE s.status NOT IN ('cancelled', 'canceled')
                        AND s.started_at < @to AND (s.expires_at IS NULL OR s.expires_at >= @from)),
                    COUNT(*) FILTER (WHERE s.cancel_at_period_end
                        AND s.status NOT IN ('cancelled', 'canceled')),
                    COUNT(*) FILTER (WHERE s.status IN ('cancelled', 'canceled')
                        AND s.cancelled_at >= @from AND s.cancelled_at < @to),
                    COUNT(*) FILTER (WHERE s.status NOT IN ('cancelled', 'canceled')
                        AND s.started_at >= @from AND s.started_at < @to)
                FROM subscriptions s
                WHERE s.tenant_id=@tenant;
                """, connection);

            AddCommonParameters(command, tenantId.Value, userId, fromUtc, toExclusiveUtc);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result.ActiveSubscriptions = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                result.ScheduledCancellations = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                result.CancelledSubscriptions = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                result.NewSubscriptions = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            }
        }

        if (isClinicAdmin)
        {
            await using var command = new NpgsqlCommand("""
                SELECT
                    COALESCE(SUM(sp.amount) FILTER (WHERE sp.status='paid' AND sp.paid_at >= @from AND sp.paid_at < @to), 0),
                    COUNT(*) FILTER (WHERE sp.status='paid' AND sp.paid_at >= @from AND sp.paid_at < @to)::int
                FROM subscription_payments sp
                JOIN subscriptions s ON s.id=sp.subscription_id
                WHERE s.tenant_id=@tenant;
                """, connection);
            AddCommonParameters(command, tenantId.Value, userId, fromUtc, toExclusiveUtc);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result.SubscriptionRevenue = reader.GetDecimal(0);
                result.PaidPayments = reader.GetInt32(1);
            }
        }

        await using (var command = new NpgsqlCommand($"""
            WITH ranked AS (
                SELECT b.client_id, b.weight, b.body_fat, b.muscle_mass,
                       ROW_NUMBER() OVER (PARTITION BY b.client_id ORDER BY b.measurement_date ASC, b.id ASC) AS first_rank,
                       ROW_NUMBER() OVER (PARTITION BY b.client_id ORDER BY b.measurement_date DESC, b.id DESC) AS last_rank
                FROM biometrics b
                JOIN clients c ON c.id=b.client_id
                WHERE c.tenant_id=@tenant
                  AND b.measurement_date >= @from::date
                  AND b.measurement_date < @to::date + interval '1 day'
                  AND c.archived_at IS NULL
                  {periodPatientScope}
            ),
            per_client AS (
                SELECT
                    client_id,
                    MAX(weight) FILTER (WHERE first_rank=1) AS first_weight,
                    MAX(weight) FILTER (WHERE last_rank=1) AS last_weight,
                    MAX(body_fat) FILTER (WHERE first_rank=1) AS first_body_fat,
                    MAX(body_fat) FILTER (WHERE last_rank=1) AS last_body_fat,
                    MAX(muscle_mass) FILTER (WHERE first_rank=1) AS first_muscle,
                    MAX(muscle_mass) FILTER (WHERE last_rank=1) AS last_muscle
                FROM ranked
                GROUP BY client_id
            )
            SELECT
                AVG(last_weight-first_weight),
                AVG(last_body_fat-first_body_fat),
                AVG(last_muscle-first_muscle)
            FROM per_client
            WHERE first_weight IS NOT NULL AND last_weight IS NOT NULL;
            """, connection))
        {
            AddCommonParameters(command, tenantId.Value, userId, fromUtc, toExclusiveUtc);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result.WeightChangeKg = reader.IsDBNull(0) ? null : reader.GetDouble(0);
                result.BodyFatChangePoints = reader.IsDBNull(1) ? null : reader.GetDouble(1);
                result.MuscleMassChangeKg = reader.IsDBNull(2) ? null : reader.GetDouble(2);
            }
        }

        if (isClinicAdmin)
        {
            await using var command = new NpgsqlCommand("""
                SELECT
                    u.id,
                    u.full_name,
                    (
                        SELECT COUNT(DISTINCT ca.client_id)::int
                        FROM client_nutritionist_assignments ca
                        JOIN clients c ON c.id=ca.client_id
                        WHERE ca.nutritionist_id=u.id
                          AND ca.is_active
                          AND c.tenant_id=@tenant
                          AND c.archived_at IS NULL
                    ),
                    (
                        SELECT COUNT(*)::int
                        FROM patient_appointments pa
                        JOIN clients c ON c.id=pa.client_id AND c.tenant_id=pa.tenant_id
                        WHERE pa.tenant_id=@tenant
                          AND pa.nutritionist_id=u.id
                          AND pa.starts_at >= @from
                          AND pa.starts_at < @to
                          AND pa.status='completed'
                          AND c.archived_at IS NULL
                    ),
                    (
                        SELECT COUNT(*)::int
                        FROM patient_checkins pc
                        JOIN clients c ON c.id=pc.client_id AND c.tenant_id=pc.tenant_id
                        WHERE pc.tenant_id=@tenant
                          AND pc.submitted_at >= @from
                          AND pc.submitted_at < @to
                          AND c.archived_at IS NULL
                          AND EXISTS (
                              SELECT 1
                              FROM client_nutritionist_assignments ca
                              WHERE ca.client_id=pc.client_id
                                AND ca.nutritionist_id=u.id
                                AND ca.assigned_at <= pc.submitted_at
                                AND (ca.unassigned_at IS NULL OR ca.unassigned_at >= pc.submitted_at)
                          )
                    )
                FROM users u
                WHERE u.tenant_id=@tenant
                  AND u.role='nutritionist'
                  AND u.archived_at IS NULL
                ORDER BY u.full_name;
                """, connection);

            AddCommonParameters(command, tenantId.Value, userId, fromUtc, toExclusiveUtc);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.NutritionistWorkload.Add(new ProfessionalStatisticsWorkloadDto
                {
                    NutritionistId = reader.GetInt32(0),
                    NutritionistName = reader.GetString(1),
                    ActivePatients = reader.GetInt32(2),
                    CompletedAppointments = reader.GetInt32(3),
                    Checkins = reader.GetInt32(4)
                });
            }
        }

        await using (var command = new NpgsqlCommand($"""
            WITH months AS (
                SELECT generate_series(
                    date_trunc('month', @from::timestamptz),
                    date_trunc('month', @to::timestamptz),
                    interval '1 month'
                ) AS month_start
            )
            SELECT
                m.month_start,
                (
                    SELECT COUNT(*)::int FROM clients c
                    WHERE c.tenant_id=@tenant
                      AND c.created_at >= m.month_start
                      AND c.created_at < m.month_start + interval '1 month'
                      {periodPatientScope}
                ),
                (
                    SELECT COUNT(*)::int FROM patient_appointments pa
                    JOIN clients c ON c.id=pa.client_id AND c.tenant_id=pa.tenant_id
                    WHERE pa.tenant_id=@tenant
                      AND pa.starts_at >= m.month_start
                      AND pa.starts_at < m.month_start + interval '1 month'
                      AND pa.status='completed'
                      AND c.archived_at IS NULL
                      {(isClinicAdmin ? "" : "AND pa.nutritionist_id=@user")}
                ),
                (
                    SELECT COUNT(*)::int FROM patient_checkins pc
                    JOIN clients c ON c.id=pc.client_id AND c.tenant_id=pc.tenant_id
                    WHERE pc.tenant_id=@tenant
                      AND pc.submitted_at >= m.month_start
                      AND pc.submitted_at < m.month_start + interval '1 month'
                      AND c.archived_at IS NULL
                      {(isClinicAdmin ? "" : """
                      AND EXISTS (
                          SELECT 1 FROM client_nutritionist_assignments ca
                          WHERE ca.client_id=pc.client_id
                            AND ca.nutritionist_id=@user
                            AND ca.assigned_at <= pc.submitted_at
                            AND (ca.unassigned_at IS NULL OR ca.unassigned_at >= pc.submitted_at)
                      )
                      """)}
                )
            FROM months m
            ORDER BY m.month_start;
            """, connection))
        {
            AddCommonParameters(command, tenantId.Value, userId, fromUtc, toExclusiveUtc);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Series.Add(new ProfessionalStatisticsPointDto
                {
                    Month = DateOnly.FromDateTime(reader.GetDateTime(0)),
                    NewPatients = reader.GetInt32(1),
                    CompletedAppointments = reader.GetInt32(2),
                    Checkins = reader.GetInt32(3)
                });
            }
        }

        return Ok(result);
    }

    private static void AddCommonParameters(
        NpgsqlCommand command,
        int tenantId,
        int? userId,
        DateTime fromUtc,
        DateTime toExclusiveUtc)
    {
        command.Parameters.AddWithValue("tenant", tenantId);
        if (userId.HasValue)
            command.Parameters.AddWithValue("user", userId.Value);
        command.Parameters.AddWithValue("from", fromUtc);
        command.Parameters.AddWithValue("to", toExclusiveUtc);
    }
}

public sealed class ProfessionalStatisticsDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string Scope { get; set; } = "nutritionist";
    public int ActivePatients { get; set; }
    public int NewPatients { get; set; }
    public int ArchivedPatients { get; set; }
    public int TotalAppointments { get; set; }
    public int CompletedAppointments { get; set; }
    public int NoShowAppointments { get; set; }
    public int CancelledAppointments { get; set; }
    public int Checkins { get; set; }
    public int ReviewedCheckins { get; set; }
    public double? AverageAdherence { get; set; }
    public decimal SubscriptionRevenue { get; set; }
    public int PaidPayments { get; set; }
    public List<ProfessionalStatisticsWorkloadDto> NutritionistWorkload { get; set; } = [];
    public int ActiveSubscriptions { get; set; }
    public int ScheduledCancellations { get; set; }
    public int CancelledSubscriptions { get; set; }
    public int NewSubscriptions { get; set; }
    public double? WeightChangeKg { get; set; }
    public double? BodyFatChangePoints { get; set; }
    public double? MuscleMassChangeKg { get; set; }
    public List<ProfessionalStatisticsPointDto> Series { get; set; } = [];
}

public sealed class ProfessionalStatisticsWorkloadDto
{
    public int NutritionistId { get; set; }
    public string NutritionistName { get; set; } = string.Empty;
    public int ActivePatients { get; set; }
    public int CompletedAppointments { get; set; }
    public int Checkins { get; set; }
}

public sealed class ProfessionalStatisticsPointDto
{
    public DateOnly Month { get; set; }
    public int NewPatients { get; set; }
    public int CompletedAppointments { get; set; }
    public int Checkins { get; set; }
}
