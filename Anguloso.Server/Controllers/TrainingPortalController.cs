using System.Data;
using System.Security.Claims;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Controllers;

/// <summary>Acceso del paciente exclusivamente a sus propias sesiones deportivas y registros.</summary>
[ApiController]
[Route("api/portal/training")]
[Authorize(Roles = "patient")]
public sealed class TrainingPortalController : ControllerBase
{
    private readonly angulosodbContext _context;
    public TrainingPortalController(angulosodbContext context) => _context = context;

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("clientId"), out var clientId)) return Unauthorized();
        if (!int.TryParse(User.FindFirstValue("portalTokenVersion"), out var version)) return Unauthorized();
        var clientValid = await _context.clients.AsNoTracking().AnyAsync(c => c.id == clientId && c.archived_at == null && c.portal_token_version == version, cancellationToken);
        if (!clientValid) return Unauthorized();

        await using var profileCommand = Command("SELECT enabled FROM training_profiles WHERE client_id = @clientId", ("clientId", clientId));
        var profileValue = await profileCommand.ExecuteScalarAsync(cancellationToken);
        if (profileValue is null || profileValue is DBNull || !Convert.ToBoolean(profileValue))
            return Ok(new { enabled = false, sessions = Array.Empty<object>() });

        await using var command = Command("""
            SELECT s.id, s.plan_id, s.scheduled_date, s.title, s.session_type, s.duration_minutes, s.intensity_target, s.instructions, s.status,
                   r.completed_at, r.actual_duration_minutes, r.session_rpe, r.notes
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            LEFT JOIN training_session_records r ON r.session_id = s.id AND r.client_id = f.client_id
            WHERE f.client_id = @clientId AND f.enabled = TRUE AND p.status IN ('active','draft','paused')
            ORDER BY s.scheduled_date, s.id
            """, ("clientId", clientId));
        var sessions = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(new
            {
                id = reader.GetInt32(0), planId = reader.GetInt32(1), scheduledDate = reader.GetFieldValue<DateOnly>(2),
                title = reader.GetString(3), sessionType = reader.GetString(4),
                durationMinutes = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                intensityTarget = reader.IsDBNull(6) ? null : reader.GetString(6),
                instructions = reader.IsDBNull(7) ? null : reader.GetString(7), status = reader.GetString(8),
                completedAt = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9),
                actualDurationMinutes = reader.IsDBNull(10) ? (int?)null : reader.GetInt32(10),
                sessionRpe = reader.IsDBNull(11) ? (decimal?)null : reader.GetDecimal(11),
                recordNotes = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }
        return Ok(new { enabled = true, sessions });
    }

    [HttpPost("sessions/{sessionId:int}/record")]
    public async Task<IActionResult> RecordSession(int sessionId, [FromBody] PatientTrainingRecordRequest request, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("clientId"), out var clientId)) return Unauthorized();
        if (!int.TryParse(User.FindFirstValue("portalTokenVersion"), out var version)) return Unauthorized();
        var clientValid = await _context.clients.AsNoTracking().AnyAsync(c => c.id == clientId && c.archived_at == null && c.portal_token_version == version, cancellationToken);
        if (!clientValid) return Unauthorized();
        if (request.ActualDurationMinutes is < 1 or > 1440 || request.SessionRpe is < 0 or > 10)
            return BadRequest(new { message = "La duración debe estar entre 1 y 1440 minutos y el RPE entre 0 y 10." });

        await using var command = Command("""
            INSERT INTO training_session_records(session_id, client_id, completed_at, actual_duration_minutes, session_rpe, notes)
            SELECT s.id, @clientId, COALESCE(@completedAt, NOW()), @duration, @rpe, @notes
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE s.id = @sessionId AND f.client_id = @clientId AND f.enabled = TRUE
            ON CONFLICT(session_id, client_id) DO UPDATE SET
                completed_at = EXCLUDED.completed_at,
                actual_duration_minutes = EXCLUDED.actual_duration_minutes,
                session_rpe = EXCLUDED.session_rpe,
                notes = EXCLUDED.notes,
                updated_at = NOW()
            RETURNING id
            """, ("sessionId", sessionId), ("clientId", clientId), ("completedAt", request.CompletedAt),
            ("duration", request.ActualDurationMinutes), ("rpe", request.SessionRpe), ("notes", request.Notes));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is null ? NotFound() : Ok(new { id = Convert.ToInt64(id), sessionId, clientId });
    }

    private NpgsqlCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            if (value is DateOnly) parameter.NpgsqlDbType = NpgsqlDbType.Date;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}

public sealed record PatientTrainingRecordRequest(DateTime? CompletedAt, int? ActualDurationMinutes, decimal? SessionRpe, string? Notes);
