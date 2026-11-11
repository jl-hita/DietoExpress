using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Sesiones de entrenamiento y su registro.
/// </summary>
public sealed partial class TrainingPeriodizationController
{
    [HttpGet("plans/{planId:int}/sessions")]
    public async Task<IActionResult> GetSessions(int planId, CancellationToken cancellationToken)
    {
        if (!await CanAccessPlan(planId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT s.id, s.plan_id, s.scheduled_date, s.title, s.session_type, s.duration_minutes, s.intensity_target, s.instructions, s.status,
                   r.completed_at, r.actual_duration_minutes, r.session_rpe, r.notes, s.exercise_prescription,
                   s.exercise_prescription_data::text
            FROM training_sessions s JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            LEFT JOIN training_session_records r ON r.session_id = s.id AND r.client_id = f.client_id
            WHERE p.id = @planId AND f.enabled = TRUE
            ORDER BY s.scheduled_date, s.id
            """, ("planId", planId));
        var items = new List<TrainingSessionDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new TrainingSessionDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetFieldValue<DateOnly>(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetDateTime(9), reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11), reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(14))));
        return Ok(items);
    }

    [HttpPost("plans/{planId:int}/sessions")]
    public async Task<IActionResult> CreateSession(int planId, [FromBody] CreateTrainingSessionRequest request, CancellationToken cancellationToken)
    {
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 160) return BadRequest(new { message = "El nombre de la sesión es obligatorio (máximo 160 caracteres)." });
        if (request.DurationMinutes is < 1 or > 1440) return BadRequest(new { message = "La duración debe estar entre 1 y 1440 minutos." });
        if (request.ExercisePrescription?.Length > 6000) return BadRequest(new { message = "La prescripción de ejercicios admite hasta 6000 caracteres." });
        if (request.ExercisePrescriptionData.HasValue &&
            (request.ExercisePrescriptionData.Value.ValueKind != JsonValueKind.Object || request.ExercisePrescriptionData.Value.GetRawText().Length > 20000))
            return BadRequest(new { message = "La prescripción estructurada debe ser un objeto JSON de hasta 20000 caracteres." });
        if (request.SessionType is not ("endurance" or "strength" or "mobility" or "recovery" or "health" or "general" or "other"))
            return BadRequest(new { message = "El tipo de sesión no es válido." });
        await using var command = Command("""
            INSERT INTO training_sessions(plan_id, scheduled_date, title, session_type, duration_minutes, intensity_target, instructions, exercise_prescription, exercise_prescription_data)
            SELECT id, @date, @title, @type, @duration, @intensity, @instructions, @exercisePrescription, @prescriptionData::jsonb
            FROM training_plans
            WHERE id = @planId AND @date >= start_date
              AND (end_date IS NULL OR @date <= end_date)
            RETURNING id
            """, ("planId", planId), ("date", request.ScheduledDate), ("title", request.Title.Trim()), ("type", request.SessionType),
            ("duration", request.DurationMinutes), ("intensity", request.IntensityTarget), ("instructions", request.Instructions),
            ("exercisePrescription", request.ExercisePrescription?.Trim()),
            ("prescriptionData", request.ExercisePrescriptionData.HasValue ? request.ExercisePrescriptionData.Value.GetRawText() : null));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is null
            ? Conflict(new { message = "La fecha de la sesión debe estar dentro del periodo del plan." })
            : CreatedAtAction(nameof(GetSessions), new { planId }, new { id = Convert.ToInt32(id), request.ScheduledDate, request.Title, request.SessionType, request.DurationMinutes, status = "planned" });
    }

    [HttpPut("sessions/{sessionId:int}")]
    public async Task<IActionResult> UpdateSession(int sessionId, [FromBody] CreateTrainingSessionRequest request, CancellationToken cancellationToken)
    {
        var clientId = await GetSessionClientId(sessionId, cancellationToken);
        if (!clientId.HasValue || !await CanManageClient(clientId.Value, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 160)
            return BadRequest(new { message = "El nombre de la sesión es obligatorio (máximo 160 caracteres)." });
        if (request.DurationMinutes is < 1 or > 1440)
            return BadRequest(new { message = "La duración debe estar entre 1 y 1440 minutos." });
        if (request.SessionType is not ("endurance" or "strength" or "mobility" or "recovery" or "health" or "general" or "other"))
            return BadRequest(new { message = "El tipo de sesión no es válido." });
        if (request.IntensityTarget?.Length > 120 || request.Instructions?.Length > 4000 || request.ExercisePrescription?.Length > 6000)
            return BadRequest(new { message = "La intensidad admite 120 caracteres y las instrucciones 4000." });
        if (request.ExercisePrescriptionData.HasValue &&
            (request.ExercisePrescriptionData.Value.ValueKind != JsonValueKind.Object || request.ExercisePrescriptionData.Value.GetRawText().Length > 20000))
            return BadRequest(new { message = "La prescripción estructurada debe ser un objeto JSON de hasta 20000 caracteres." });

        await using var command = Command("""
            UPDATE training_sessions s
            SET scheduled_date = @date, title = @title, session_type = @type, duration_minutes = @duration,
                intensity_target = @intensity, instructions = @instructions, exercise_prescription = @exercisePrescription,
                exercise_prescription_data = CASE
                    WHEN s.exercise_prescription IS DISTINCT FROM @exercisePrescription THEN @prescriptionData::jsonb
                    ELSE COALESCE(@prescriptionData::jsonb, s.exercise_prescription_data)
                END, updated_at = NOW()
            FROM training_plans p JOIN training_profiles f ON f.id = p.profile_id
            WHERE s.plan_id = p.id AND s.id = @sessionId AND f.enabled = TRUE
              AND @date >= p.start_date AND (p.end_date IS NULL OR @date <= p.end_date)
            RETURNING s.id
            """, ("sessionId", sessionId), ("date", request.ScheduledDate), ("title", request.Title.Trim()),
            ("type", request.SessionType), ("duration", request.DurationMinutes), ("intensity", request.IntensityTarget?.Trim()),
            ("instructions", request.Instructions?.Trim()), ("exercisePrescription", request.ExercisePrescription?.Trim()),
            ("prescriptionData", request.ExercisePrescriptionData.HasValue ? request.ExercisePrescriptionData.Value.GetRawText() : null));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is null
            ? Conflict(new { message = "La fecha de la sesión debe estar dentro del periodo del plan." })
            : Ok(new { id = Convert.ToInt32(id), clientId = clientId.Value, message = "Sesión actualizada." });
    }

    [HttpDelete("sessions/{sessionId:int}")]
    public async Task<IActionResult> DeleteFutureSession(int sessionId, CancellationToken cancellationToken)
    {
        var clientId = await GetSessionClientId(sessionId, cancellationToken);
        if (!clientId.HasValue || !await CanManageClient(clientId.Value, cancellationToken)) return NotFound();

        // Lock the session before checking for records. A concurrent execution record must
        // never be cascaded away by a professional deleting a session from the weekly editor.
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var find = Command("""
            SELECT scheduled_date
            FROM training_sessions
            WHERE id = @sessionId
            FOR UPDATE
            """, ("sessionId", sessionId));
        var scheduledDateValue = await find.ExecuteScalarAsync(cancellationToken);
        if (scheduledDateValue is not DateOnly scheduledDate) return NotFound();
        if (scheduledDate < DateOnly.FromDateTime(DateTime.UtcNow))
            return Conflict(new { message = "No se pueden eliminar sesiones pasadas; conserva el historial de entrenamiento." });

        await using var hasRecords = Command("""
            SELECT EXISTS (
                SELECT 1 FROM training_session_records WHERE session_id = @sessionId
            )
            """, ("sessionId", sessionId));
        if (Convert.ToBoolean(await hasRecords.ExecuteScalarAsync(cancellationToken)))
            return Conflict(new { message = "Esta sesión ya tiene un registro de realización y no se puede eliminar." });

        await using var delete = Command("""
            DELETE FROM training_sessions
            WHERE id = @sessionId AND scheduled_date >= CURRENT_DATE
            """, ("sessionId", sessionId));
        var deleted = await delete.ExecuteNonQueryAsync(cancellationToken);
        if (deleted != 1)
            return Conflict(new { message = "La sesión ya no está disponible para eliminarse." });

        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("sessions/{sessionId:int}/record")]
    public async Task<IActionResult> RecordSession(int sessionId, [FromBody] RecordTrainingSessionRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        var clientId = await GetSessionClientId(sessionId, cancellationToken);
        // Los colaboradores tienen acceso de consulta: solo el profesional responsable puede modificar registros.
        if (!clientId.HasValue || !await CanManageClient(clientId.Value, cancellationToken)) return NotFound();
        if (request.ActualDurationMinutes is < 1 or > 1440 || request.SessionRpe is < 0 or > 10)
            return BadRequest(new { message = "La duración debe ser positiva y el RPE debe estar entre 0 y 10." });
        await using var command = Command("""
            INSERT INTO training_session_records(session_id, client_id, completed_at, actual_duration_minutes, session_rpe, notes)
            SELECT s.id, f.client_id, COALESCE(@completedAt, NOW()), @duration, @rpe, @notes
            FROM training_sessions s JOIN training_plans p ON p.id = s.plan_id JOIN training_profiles f ON f.id = p.profile_id
            WHERE s.id = @sessionId AND f.enabled = TRUE
            ON CONFLICT(session_id, client_id) DO UPDATE SET completed_at = EXCLUDED.completed_at,
                actual_duration_minutes = EXCLUDED.actual_duration_minutes, session_rpe = EXCLUDED.session_rpe,
                notes = EXCLUDED.notes, updated_at = NOW()
            RETURNING id
            """, ("sessionId", sessionId), ("completedAt", request.CompletedAt), ("duration", request.ActualDurationMinutes),
            ("rpe", request.SessionRpe), ("notes", request.Notes));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is null ? NotFound() : Ok(new { id = Convert.ToInt64(id), clientId = clientId.Value, sessionId });
    }
}
