using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/professional/dashboard")]
[Authorize(Roles = "nutritionist")]
public sealed class ProfessionalDashboardController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public ProfessionalDashboardController(IConfiguration configuration) => _configuration = configuration;

    [HttpGet]
    public async Task<ActionResult<ProfessionalDashboardDto>> Get()
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        var userId = AuthHelpers.GetUserId(User);
        if (!tenantId.HasValue || !userId.HasValue) return Unauthorized();

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();

        var result = new ProfessionalDashboardDto();

        await using (var command = new NpgsqlCommand("""
            SELECT COUNT(*) FILTER (WHERE status='open' OR status='in_progress')::int,
                   COUNT(*) FILTER (WHERE due_at IS NOT NULL AND due_at < NOW() AND status IN ('open','in_progress'))::int
            FROM professional_tasks
            WHERE tenant_id=@tenant AND assigned_user_id=@user;
            """, connection))
        {
            command.Parameters.AddWithValue("tenant", tenantId.Value);
            command.Parameters.AddWithValue("user", userId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                result.OpenTaskCount = reader.GetInt32(0);
                result.OverdueTaskCount = reader.GetInt32(1);
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT COUNT(*)::int
            FROM patient_conversations c
            JOIN clients cl ON cl.id=c.client_id
            WHERE c.tenant_id=@tenant AND cl.archived_at IS NULL
              AND EXISTS (SELECT 1 FROM client_nutritionist_assignments a
                          WHERE a.client_id=c.client_id AND a.nutritionist_id=@user AND a.is_active)
              AND EXISTS (SELECT 1 FROM patient_messages m
                          WHERE m.conversation_id=c.id AND m.sender_client_id IS NOT NULL AND m.read_at IS NULL);
            """, connection))
        {
            command.Parameters.AddWithValue("tenant", tenantId.Value);
            command.Parameters.AddWithValue("user", userId.Value);
            result.UnreadMessageCount = Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        await using (var command = new NpgsqlCommand("""
            SELECT COUNT(*)::int
            FROM patient_documents pd
            JOIN clients cl ON cl.id=pd.client_id
            WHERE pd.tenant_id=@tenant AND cl.archived_at IS NULL
              AND pd.revoked_at IS NULL AND pd.requires_signature=true AND pd.status='pending'
              AND EXISTS (SELECT 1 FROM client_nutritionist_assignments a
                          WHERE a.client_id=pd.client_id AND a.nutritionist_id=@user AND a.is_active);
            """, connection))
        {
            command.Parameters.AddWithValue("tenant", tenantId.Value);
            command.Parameters.AddWithValue("user", userId.Value);
            result.PendingDocumentCount = Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        await using (var command = new NpgsqlCommand("""
            SELECT pa.id, pa.client_id, COALESCE(cl.full_name,'Paciente'), pa.starts_at, pa.ends_at, pa.status
            FROM patient_appointments pa
            JOIN clients cl ON cl.id=pa.client_id
            WHERE pa.tenant_id=@tenant AND pa.nutritionist_id=@user
              AND cl.archived_at IS NULL
              AND pa.starts_at >= @from AND pa.starts_at < @to
              AND pa.status IN ('requested','confirmed')
            ORDER BY pa.starts_at
            LIMIT 20;
            """, connection))
        {
            command.Parameters.AddWithValue("tenant", tenantId.Value);
            command.Parameters.AddWithValue("user", userId.Value);
            var madrid = GetMadridTimeZone();
            var localToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, madrid).Date;
            var fromUtc = TimeZoneInfo.ConvertTimeToUtc(localToday, madrid);
            command.Parameters.AddWithValue("from", fromUtc);
            command.Parameters.AddWithValue("to", fromUtc.AddDays(1));
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.TodayAppointments.Add(new DashboardAppointmentDto
                {
                    Id = reader.GetInt32(0), ClientId = reader.GetInt32(1), ClientName = reader.GetString(2),
                    StartsAt = reader.GetDateTime(3), EndsAt = reader.GetDateTime(4), Status = reader.GetString(5)
                });
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT pd.client_id, COALESCE(cl.full_name,'Paciente'), COUNT(*)::int
            FROM patient_documents pd
            JOIN clients cl ON cl.id=pd.client_id
            WHERE pd.tenant_id=@tenant AND cl.archived_at IS NULL
              AND pd.revoked_at IS NULL AND pd.requires_signature=true AND pd.status='pending'
              AND EXISTS (SELECT 1 FROM client_nutritionist_assignments a
                          WHERE a.client_id=pd.client_id AND a.nutritionist_id=@user AND a.is_active)
            GROUP BY pd.client_id, cl.full_name
            ORDER BY COUNT(*) DESC, cl.full_name
            LIMIT 10;
            """, connection))
        {
            command.Parameters.AddWithValue("tenant", tenantId.Value);
            command.Parameters.AddWithValue("user", userId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.PendingDocuments.Add(new DashboardPendingClientDto { ClientId=reader.GetInt32(0), ClientName=reader.GetString(1), PendingCount=reader.GetInt32(2) });
        }

        return Ok(result);
    }

    private static TimeZoneInfo GetMadridTimeZone() =>
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Madrid");
}

public sealed class ProfessionalDashboardDto
{
    public int OpenTaskCount { get; set; }
    public int OverdueTaskCount { get; set; }
    public int UnreadMessageCount { get; set; }
    public int PendingDocumentCount { get; set; }
    public List<DashboardAppointmentDto> TodayAppointments { get; set; } = [];
    public List<DashboardPendingClientDto> PendingDocuments { get; set; } = [];
}

public sealed class DashboardAppointmentDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Status { get; set; } = "";
}

public sealed class DashboardPendingClientDto
{
    public int ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public int PendingCount { get; set; }
}
