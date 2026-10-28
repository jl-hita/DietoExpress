using System.Data;
using System.Security.Claims;
using System.Text.Json;
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
                   r.completed_at, r.actual_duration_minutes, r.session_rpe, r.notes, s.exercise_prescription,
                   r.outcome, r.non_completion_reason, r.recovery_rating, r.pain_signal, r.progression_target_achieved,
                   s.exercise_prescription_data::text, r.exercise_feedback::text
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
                recordNotes = reader.IsDBNull(12) ? null : reader.GetString(12),
                exercisePrescription = reader.IsDBNull(13) ? null : reader.GetString(13),
                outcome = reader.IsDBNull(14) ? null : reader.GetString(14),
                nonCompletionReason = reader.IsDBNull(15) ? null : reader.GetString(15),
                recoveryRating = reader.IsDBNull(16) ? null : reader.GetString(16),
                painSignal = reader.IsDBNull(17) ? null : reader.GetString(17),
                progressionTargetAchieved = reader.IsDBNull(18) ? (bool?)null : reader.GetBoolean(18),
                exercisePrescriptionData = reader.IsDBNull(19) ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(19)),
                exerciseFeedback = reader.IsDBNull(20) ? Array.Empty<PatientTrainingExerciseFeedback>() :
                    JsonSerializer.Deserialize<PatientTrainingExerciseFeedback[]>(reader.GetString(20), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? Array.Empty<PatientTrainingExerciseFeedback>()
            });
        }
        await reader.DisposeAsync();

        var feedback = new List<Anguloso.Server.Logica.TrainingSessionFeedback>();
        await using (var feedbackCommand = Command("""
            SELECT r.outcome, r.non_completion_reason, r.recovery_rating, r.pain_signal, r.session_rpe, r.progression_target_achieved
            FROM training_session_records r
            JOIN training_sessions s ON s.id = r.session_id
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE f.client_id = @clientId AND f.enabled = TRUE AND p.status = 'active' AND r.outcome IS NOT NULL
            ORDER BY r.updated_at DESC
            LIMIT 3
            """, ("clientId", clientId)))
        await using (var feedbackReader = await feedbackCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await feedbackReader.ReadAsync(cancellationToken))
            {
                feedback.Add(new Anguloso.Server.Logica.TrainingSessionFeedback(
                    ParseOutcome(feedbackReader.GetString(0)),
                    feedbackReader.IsDBNull(1) ? Anguloso.Server.Logica.TrainingNonCompletionReason.None : ParseReason(feedbackReader.GetString(1)),
                    feedbackReader.IsDBNull(2) ? Anguloso.Server.Logica.TrainingRecoveryRating.Unknown : ParseRecovery(feedbackReader.GetString(2)),
                    feedbackReader.IsDBNull(3) ? Anguloso.Server.Logica.TrainingPainSignal.None : ParsePain(feedbackReader.GetString(3)),
                    feedbackReader.IsDBNull(4) ? null : feedbackReader.GetDecimal(4),
                    feedbackReader.IsDBNull(5) ? null : feedbackReader.GetBoolean(5)));
            }
        }

        feedback.Reverse(); // Policy contract is oldest-to-newest.
        var adaptation = feedback.Count == 0
            ? null
            : Anguloso.Server.Logica.TrainingAdaptationPolicy.Evaluate(feedback);
        return Ok(new
        {
            enabled = true,
            sessions,
            adaptationRecommendation = adaptation is null ? null : new
            {
                action = ToApiAction(adaptation.Action),
                explanation = adaptation.Explanation
            }
        });
    }

    [HttpPost("sessions/{sessionId:int}/record")]
    public async Task<IActionResult> RecordSession(int sessionId, [FromBody] PatientTrainingRecordRequest request, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("clientId"), out var clientId)) return Unauthorized();
        if (!int.TryParse(User.FindFirstValue("portalTokenVersion"), out var version)) return Unauthorized();
        var clientValid = await _context.clients.AsNoTracking().AnyAsync(c => c.id == clientId && c.archived_at == null && c.portal_token_version == version, cancellationToken);
        if (!clientValid) return Unauthorized();
        var structuredFeedback = request.Outcome is not null;
        var missedSession = request.Outcome == "not_completed";
        if ((structuredFeedback && !missedSession && request.ActualDurationMinutes is null) ||
            request.ActualDurationMinutes is < 1 or > 1440 ||
            (structuredFeedback && !missedSession && request.SessionRpe is null) ||
            request.SessionRpe is < 0 or > 10)
            return BadRequest(new { message = "Para una sesión realizada, indica una duración entre 1 y 1440 minutos y un RPE entre 0 y 10." });
        if (!IsAllowed(request.Outcome, "completed", "partial", "not_completed") ||
            !IsAllowed(request.NonCompletionReason, "none", "time", "fatigue", "equipment", "motivation", "illness", "other") ||
            !IsAllowed(request.RecoveryRating, "unknown", "good", "partial", "insufficient") ||
            !IsAllowed(request.PainSignal, "none", "new_or_worsening"))
            return BadRequest(new { message = "El feedback de entrenamiento contiene un valor no válido." });
        if (request.Outcome is not null &&
            request.Outcome != "completed" &&
            (request.NonCompletionReason is null or "none"))
            return BadRequest(new { message = "Indica el motivo cuando la sesión fue parcial o no se realizó." });
        if (request.Outcome == "completed" &&
            request.NonCompletionReason is not (null or "none"))
            return BadRequest(new { message = "Una sesión completada no puede tener un motivo de no finalización." });
        if (request.Outcome is null &&
            (request.NonCompletionReason is not null || request.RecoveryRating is not null ||
             request.PainSignal is not null || request.ProgressionTargetAchieved is not null))
            return BadRequest(new { message = "El feedback estructurado requiere indicar el resultado de la sesión." });
        if (request.Notes?.Length > 4000)
            return BadRequest(new { message = "Las notas no pueden superar los 4000 caracteres." });
        if (request.ExerciseFeedback is { Count: > 100 })
            return BadRequest(new { message = "No se pueden registrar más de 100 ejercicios por sesión." });
        if (request.ExerciseFeedback is not null)
        {
            var invalidExerciseFeedback = request.ExerciseFeedback.Any(item =>
                string.IsNullOrWhiteSpace(item.ExerciseName) || item.ExerciseName.Length > 200 ||
                item.PlannedSets is < 1 or > 100 || item.CompletedSets < 0 ||
                item.CompletedSets > item.PlannedSets ||
                (item.CompletedSets < item.PlannedSets &&
                    !IsAllowed(item.SkippedSetReason, "time", "fatigue", "equipment", "motivation", "illness", "pain", "other")) ||
                (item.CompletedSets == item.PlannedSets && item.SkippedSetReason is not null));
            if (invalidExerciseFeedback)
                return BadRequest(new { message = "Revisa las series realizadas y el motivo de las series omitidas de cada ejercicio." });
            var duplicateExercise = request.ExerciseFeedback
                .Where(item => !string.IsNullOrWhiteSpace(item.ExerciseId))
                .GroupBy(item => item.ExerciseId, StringComparer.Ordinal)
                .Any(group => group.Count() > 1);
            if (duplicateExercise)
                return BadRequest(new { message = "Un ejercicio no puede aparecer repetido en el registro." });
        }

        await using var command = Command("""
            INSERT INTO training_session_records(
                session_id, client_id, completed_at, actual_duration_minutes, session_rpe, notes,
                outcome, non_completion_reason, recovery_rating, pain_signal, progression_target_achieved, exercise_feedback)
            SELECT s.id, @clientId, CASE WHEN CAST(@outcome AS text) = 'not_completed' THEN NULL ELSE COALESCE(@completedAt, NOW()) END, @duration, @rpe, @notes,
                   @outcome, @reason, @recovery, @pain, @progressionAchieved,
                   CASE WHEN CAST(@exerciseFeedback AS text) IS NULL THEN NULL ELSE CAST(@exerciseFeedback AS jsonb) END
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE s.id = @sessionId AND f.client_id = @clientId AND f.enabled = TRUE
            ON CONFLICT(session_id, client_id) DO UPDATE SET
                completed_at = EXCLUDED.completed_at,
                actual_duration_minutes = EXCLUDED.actual_duration_minutes,
                session_rpe = EXCLUDED.session_rpe,
                notes = EXCLUDED.notes,
                outcome = COALESCE(EXCLUDED.outcome, training_session_records.outcome),
                non_completion_reason = COALESCE(EXCLUDED.non_completion_reason, training_session_records.non_completion_reason),
                recovery_rating = COALESCE(EXCLUDED.recovery_rating, training_session_records.recovery_rating),
                pain_signal = COALESCE(EXCLUDED.pain_signal, training_session_records.pain_signal),
                progression_target_achieved = COALESCE(EXCLUDED.progression_target_achieved, training_session_records.progression_target_achieved),
                exercise_feedback = COALESCE(EXCLUDED.exercise_feedback, training_session_records.exercise_feedback),
                updated_at = NOW()
            RETURNING id
            """, ("sessionId", sessionId), ("clientId", clientId), ("completedAt", request.CompletedAt),
            ("duration", request.ActualDurationMinutes), ("rpe", request.SessionRpe), ("notes", request.Notes),
            ("outcome", request.Outcome), ("reason", request.NonCompletionReason), ("recovery", request.RecoveryRating),
            ("pain", request.PainSignal), ("progressionAchieved", request.ProgressionTargetAchieved),
            ("exerciseFeedback", request.ExerciseFeedback is { Count: > 0 }
                ? JsonSerializer.Serialize(request.ExerciseFeedback, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                : null));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        if (id is null) return NotFound();
        if (structuredFeedback)
            await CreateAdaptationProposalAsync(clientId, sessionId, Convert.ToInt64(id), cancellationToken);
        return Ok(new { id = Convert.ToInt64(id), sessionId, clientId });
    }

    private async Task CreateAdaptationProposalAsync(int clientId, int sessionId, long recordId, CancellationToken cancellationToken)
    {
        var feedback = new List<(long RecordId, Anguloso.Server.Logica.TrainingSessionFeedback Feedback)>();
        await using (var command = Command("""
            SELECT r.id, r.outcome, r.non_completion_reason, r.recovery_rating, r.pain_signal,
                   r.session_rpe, r.progression_target_achieved
            FROM training_session_records r
            JOIN training_sessions s ON s.id = r.session_id
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE f.client_id = @clientId AND s.plan_id = (
                SELECT plan_id FROM training_sessions WHERE id = @sessionId
            ) AND p.status = 'active' AND r.outcome IS NOT NULL
            ORDER BY CASE WHEN r.id = @recordId THEN 0 ELSE 1 END, r.updated_at DESC, r.id DESC
            LIMIT 3
            """, ("clientId", clientId), ("sessionId", sessionId), ("recordId", recordId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                feedback.Add((reader.GetInt64(0), new Anguloso.Server.Logica.TrainingSessionFeedback(
                    ParseOutcome(reader.GetString(1)),
                    reader.IsDBNull(2) ? Anguloso.Server.Logica.TrainingNonCompletionReason.None : ParseReason(reader.GetString(2)),
                    reader.IsDBNull(3) ? Anguloso.Server.Logica.TrainingRecoveryRating.Unknown : ParseRecovery(reader.GetString(3)),
                    reader.IsDBNull(4) ? Anguloso.Server.Logica.TrainingPainSignal.None : ParsePain(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    reader.IsDBNull(6) ? null : reader.GetBoolean(6))));
        }
        if (feedback.Count == 0 || feedback.All(x => x.RecordId != recordId)) return;
        feedback.Reverse();
        var decision = Anguloso.Server.Logica.TrainingAdaptationPolicy.Evaluate(feedback.Select(x => x.Feedback).ToArray());
        var action = ToApiAction(decision.Action);
        if (action is null) return;

        await using var supersede = Command("""
            UPDATE training_adaptation_proposals
            SET status = 'superseded', review_notes = 'Sustituida por una nueva evaluación del feedback.'
            WHERE source_record_id = @recordId AND status = 'pending' AND action <> @action
            """, ("recordId", recordId), ("action", action));
        await supersede.ExecuteNonQueryAsync(cancellationToken);

        await using var insert = Command("""
            INSERT INTO training_adaptation_proposals(
                client_id, plan_id, source_session_id, source_record_id, action, explanation)
            SELECT @clientId, s.plan_id, s.id, @recordId, @action, @explanation
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE s.id = @sessionId AND f.client_id = @clientId AND p.status = 'active'
            ON CONFLICT(source_record_id, action) DO NOTHING
            """, ("clientId", clientId), ("sessionId", sessionId), ("recordId", recordId),
            ("action", action), ("explanation", decision.Explanation));
        await insert.ExecuteNonQueryAsync(cancellationToken);
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

    private static bool IsAllowed(string? value, params string[] allowed) =>
        value is null || allowed.Contains(value, StringComparer.Ordinal);

    private static Anguloso.Server.Logica.TrainingSessionOutcome ParseOutcome(string value) => value switch
    {
        "completed" => Anguloso.Server.Logica.TrainingSessionOutcome.Completed,
        "partial" => Anguloso.Server.Logica.TrainingSessionOutcome.Partial,
        _ => Anguloso.Server.Logica.TrainingSessionOutcome.NotCompleted
    };

    private static Anguloso.Server.Logica.TrainingNonCompletionReason ParseReason(string value) => value switch
    {
        "time" => Anguloso.Server.Logica.TrainingNonCompletionReason.Time,
        "fatigue" => Anguloso.Server.Logica.TrainingNonCompletionReason.Fatigue,
        "equipment" => Anguloso.Server.Logica.TrainingNonCompletionReason.Equipment,
        "motivation" => Anguloso.Server.Logica.TrainingNonCompletionReason.Motivation,
        "illness" => Anguloso.Server.Logica.TrainingNonCompletionReason.Illness,
        "other" => Anguloso.Server.Logica.TrainingNonCompletionReason.Other,
        _ => Anguloso.Server.Logica.TrainingNonCompletionReason.None
    };

    private static Anguloso.Server.Logica.TrainingRecoveryRating ParseRecovery(string value) => value switch
    {
        "good" => Anguloso.Server.Logica.TrainingRecoveryRating.Good,
        "partial" => Anguloso.Server.Logica.TrainingRecoveryRating.Partial,
        "insufficient" => Anguloso.Server.Logica.TrainingRecoveryRating.Insufficient,
        _ => Anguloso.Server.Logica.TrainingRecoveryRating.Unknown
    };

    private static Anguloso.Server.Logica.TrainingPainSignal ParsePain(string value) =>
        value == "new_or_worsening"
            ? Anguloso.Server.Logica.TrainingPainSignal.NewOrWorsening
            : Anguloso.Server.Logica.TrainingPainSignal.None;

    private static string ToApiAction(Anguloso.Server.Logica.TrainingAdaptationAction action) => action switch
    {
        Anguloso.Server.Logica.TrainingAdaptationAction.InsufficientData => "insufficient_data",
        Anguloso.Server.Logica.TrainingAdaptationAction.InvalidFeedback => "invalid_feedback",
        Anguloso.Server.Logica.TrainingAdaptationAction.NoChange => "no_change",
        Anguloso.Server.Logica.TrainingAdaptationAction.ReorganizeFutureSessions => "reorganize_future_sessions",
        Anguloso.Server.Logica.TrainingAdaptationAction.HoldProgressionAndReview => "hold_progression_and_review",
        Anguloso.Server.Logica.TrainingAdaptationAction.ReviewAndReduceTrainingStress => "review_and_reduce_training_stress",
        Anguloso.Server.Logica.TrainingAdaptationAction.ProgressAccordingToPrescription => "progress_according_to_prescription",
        _ => "pause_affected_exercise_and_review"
    };
}

public sealed record PatientTrainingExerciseFeedback(
    string? ExerciseId,
    string ExerciseName,
    int PlannedSets,
    int CompletedSets,
    string? SkippedSetReason = null);

public sealed record PatientTrainingRecordRequest(
    DateTime? CompletedAt,
    int? ActualDurationMinutes,
    decimal? SessionRpe,
    string? Notes,
    string? Outcome = null,
    string? NonCompletionReason = null,
    string? RecoveryRating = null,
    string? PainSignal = null,
    bool? ProgressionTargetAchieved = null,
    List<PatientTrainingExerciseFeedback>? ExerciseFeedback = null);
