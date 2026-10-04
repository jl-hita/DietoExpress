using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace Anguloso.Server.Controllers;

[Route("api/admin/alerts")]
[ApiController]
[Authorize(Roles = "superadmin")]
public class AdminAlertsController : ControllerBase
{
    private readonly angulosodbContext _context;

    public AdminAlertsController(angulosodbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Devuelve las incidencias de infraestructura todavía no revisadas por un SuperAdmin.
    /// Los detalles técnicos no se exponen: el panel muestra únicamente información operativa segura.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetActiveAlerts()
    {
        var alerts = new List<AdminAlertDto>();
        var connection = _context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, severity, component, title, message,
                   first_seen_at, last_seen_at, occurrences
            FROM system_alerts
            WHERE resolved_at IS NULL
            ORDER BY CASE severity
                       WHEN 'critical' THEN 0
                       WHEN 'error' THEN 1
                       WHEN 'warning' THEN 2
                       ELSE 3
                     END,
                     last_seen_at DESC
            LIMIT 50;";

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            alerts.Add(new AdminAlertDto
            {
                Id = reader.GetInt64(0),
                Severity = reader.GetString(1),
                Component = reader.GetString(2),
                Title = reader.GetString(3),
                Message = reader.GetString(4),
                FirstSeenAt = reader.GetDateTime(5),
                LastSeenAt = reader.GetDateTime(6),
                Occurrences = reader.GetInt32(7)
            });
        }

        return Ok(alerts);
    }

    /// <summary>
    /// Marca una incidencia como revisada. La incidencia permanece en el historial técnico.
    /// </summary>
    [HttpPost("{id:long}/resolve")]
    public async Task<IActionResult> ResolveAlert(long id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId <= 0)
            return Unauthorized();

        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE system_alerts
            SET resolved_at = NOW(), resolved_by_user_id = @user_id
            WHERE id = @id AND resolved_at IS NULL;";

        var userParameter = command.CreateParameter();
        userParameter.ParameterName = "user_id";
        userParameter.Value = userId;
        command.Parameters.Add(userParameter);

        var idParameter = command.CreateParameter();
        idParameter.ParameterName = "id";
        idParameter.Value = id;
        command.Parameters.Add(idParameter);

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        var affected = await command.ExecuteNonQueryAsync();
        if (affected == 0)
            return NotFound("Incidencia no encontrada o ya revisada.");

        return NoContent();
    }
}

public class AdminAlertDto
{
    public long Id { get; set; }
    public string Severity { get; set; } = "warning";
    public string Component { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public int Occurrences { get; set; }
}
