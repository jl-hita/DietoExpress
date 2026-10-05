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

        var scope = isClinicAdmin
            ? ""
            : """
              AND EXISTS (
                  SELECT 1 FROM client_nutritionist_assignments ca
                  WHERE ca.client_id = c.id
                    AND ca.nutritionist_id = @user
                    AND ca.is_active
              )
              """;

        await using (var command = new NpgsqlCommand($"""
            SELECT
                COUNT(*) FILTER (WHERE c.archived_at IS NULL)::int,
                COUNT(*) FILTER (WHERE c.created_at >= @from AND c.created_at < @to)::int,
                COUNT(*) FILTER (WHERE c.archived_at >= @from AND c.archived_at < @to)::int
            FROM clients c
            WHERE c.tenant_id=@tenant
              {scope};
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
                    AND ca.is_active
              )
              """;

        await using (var command = new NpgsqlCommand($"""
            SELECT
                COUNT(*)::int,
                COUNT(*) FILTER (WHERE pc.reviewed_at IS NOT NULL)::int,
                AVG(pc.adherence)
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
                      {scope}
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
                            AND ca.is_active
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
    public List<ProfessionalStatisticsPointDto> Series { get; set; } = [];
}

public sealed class ProfessionalStatisticsPointDto
{
    public DateOnly Month { get; set; }
    public int NewPatients { get; set; }
    public int CompletedAppointments { get; set; }
    public int Checkins { get; set; }
}
