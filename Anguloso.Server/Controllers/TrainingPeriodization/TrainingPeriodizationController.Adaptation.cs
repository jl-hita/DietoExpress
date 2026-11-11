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
/// Propuestas de adaptación, solicitudes de revisión del paciente y tendencia de carga.
/// </summary>
public sealed partial class TrainingPeriodizationController
{
    [HttpGet("clients/{clientId:int}/adaptation-proposals")]
    public async Task<IActionResult> GetAdaptationProposals(int clientId, CancellationToken cancellationToken)
    {
        if (!await CanManageClient(clientId, cancellationToken)) return NotFound();
        await using var command = CreateCommand("""
            SELECT p.id, p.client_id, p.plan_id, p.source_session_id, p.source_record_id,
                   p.action, p.explanation, p.status, p.created_at, p.review_notes, p.applied_future_sessions,
                   s.scheduled_date, s.title
            FROM training_adaptation_proposals p
            JOIN training_sessions s ON s.id = p.source_session_id
            WHERE p.client_id = @clientId AND p.status = 'pending'
            ORDER BY p.created_at DESC, p.id DESC
            LIMIT 100
            """, null, ("clientId", clientId));
        var items = new List<(long Id, int ClientId, int PlanId, int SourceSessionId, long SourceRecordId,
            string Action, string Explanation, string Status, DateTime CreatedAt, string? ReviewNotes,
            int AppliedFutureSessions, DateOnly SessionDate, string SessionTitle)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                items.Add((
                    reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetInt32(3), reader.GetInt64(4), reader.GetString(5),
                    reader.GetString(6), reader.GetString(7), reader.GetDateTime(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetInt32(10),
                    reader.GetFieldValue<DateOnly>(11), reader.GetString(12)));
        }

        var response = new List<object>();
        foreach (var item in items)
        {
            IReadOnlyList<object> exerciseRecommendations = item.Action == "review_and_reduce_training_stress"
                ? (await GetExerciseVolumeRecommendationsAsync(item.PlanId, cancellationToken))
                    .Select(recommendation => (object)new
                    {
                        exerciseId = recommendation.ExerciseId,
                        action = recommendation.Action.ToString(),
                        explanation = recommendation.Explanation,
                        suggestedWorkingSets = recommendation.SuggestedWorkingSets,
                        exposuresAnalyzed = recommendation.ExposuresAnalyzed
                    }).ToArray()
                : Array.Empty<object>();
            response.Add(new
            {
                id = item.Id, clientId = item.ClientId, planId = item.PlanId,
                sourceSessionId = item.SourceSessionId, sourceRecordId = item.SourceRecordId,
                action = item.Action, explanation = item.Explanation, status = item.Status,
                createdAt = item.CreatedAt, reviewNotes = item.ReviewNotes,
                appliedFutureSessions = item.AppliedFutureSessions, sessionDate = item.SessionDate,
                sessionTitle = item.SessionTitle, exerciseVolumeRecommendations = exerciseRecommendations
            });
        }
        return Ok(response);
    }

    [HttpPost("adaptation-proposals/{proposalId:long}/review")]
    public async Task<IActionResult> ReviewAdaptationProposal(
        long proposalId, [FromBody] ReviewTrainingAdaptationProposalRequest request, CancellationToken cancellationToken)
    {
        if (request.Decision is not ("approved" or "rejected"))
            return BadRequest(new { message = "La decisión debe ser approved o rejected." });
        if (request.Notes?.Length > 2000)
            return BadRequest(new { message = "Las notas de revisión no pueden superar los 2000 caracteres." });
        if (request.TargetExerciseIds?.Count > 40 ||
            (request.TargetExerciseIds?.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 100) ?? false) ||
            (request.TargetExerciseIds?.Distinct(StringComparer.Ordinal).Count() != request.TargetExerciseIds?.Count))
            return BadRequest(new { message = "Los ejercicios seleccionados no son válidos." });

        var clientId = await _context.Database.SqlQueryRaw<int?>(
            """SELECT client_id AS "Value" FROM training_adaptation_proposals WHERE id = {0}""", proposalId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!clientId.HasValue || !await CanManageClient(clientId.Value, cancellationToken)) return NotFound();
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            int planId;
            string action;
            string status;
            await using (var proposalCommand = CreateCommand("""
                SELECT plan_id, action, status FROM training_adaptation_proposals
                WHERE id = @proposalId AND client_id = @clientId FOR UPDATE
                """, transaction, ("proposalId", proposalId), ("clientId", clientId.Value)))
            await using (var reader = await proposalCommand.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return NotFound();
                }
                planId = reader.GetInt32(0);
                action = reader.GetString(1);
                status = reader.GetString(2);
            }
            if (status != "pending")
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Conflict(new { message = "Esta propuesta ya ha sido revisada o sustituida." });
            }

            var applied = 0;
            if (request.Decision == "approved" && action == "review_and_reduce_training_stress" &&
                request.TargetExerciseIds is { Count: > 0 })
            {
                var allowedExerciseIds = (await GetExerciseVolumeRecommendationsAsync(planId, cancellationToken, transaction))
                    .Where(item => item.Action == TrainingExerciseVolumeAction.ProposeReduceOneSet)
                    .Select(item => item.ExerciseId)
                    .ToHashSet(StringComparer.Ordinal);
                var requestedExerciseIds = request.TargetExerciseIds ?? Array.Empty<string>();
                if (requestedExerciseIds.Any(id => !allowedExerciseIds.Contains(id)))
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return BadRequest(new { message = "Solo se pueden adaptar ejercicios con una recomendación vigente de reducción basada en sus registros recientes." });
                }
                applied = await ReduceFutureSessionVolumeAsync(planId, proposalId,
                    requestedExerciseIds, transaction, cancellationToken);
            }
            else if (request.Decision == "approved" && action == "restore_volume")
            {
                var restorable = (await GetExerciseVolumeRecommendationsAsync(planId, cancellationToken, transaction))
                    .Where(item => item.Action == TrainingExerciseVolumeAction.ProposeRestoreOneSet)
                    .Select(item => item.ExerciseId)
                    .ToHashSet(StringComparer.Ordinal);
                var requestedIds = (request.TargetExerciseIds ?? Array.Empty<string>()).Count == 0
                    ? restorable
                    : request.TargetExerciseIds!.ToHashSet(StringComparer.Ordinal);
                if (requestedIds.Any(id => !restorable.Contains(id)))
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return BadRequest(new { message = "Solo se pueden restaurar ejercicios con una recomendación vigente basada en sus registros recientes." });
                }
                applied = await RewriteFutureSessionsAsync(planId, 3,
                    $"Adaptación aprobada por profesional (propuesta #{proposalId}): se recupera una serie previamente reducida (sin superar la dosis original).",
                    transaction, cancellationToken,
                    json => TrainingAdaptationPrescriptionAdjuster.TryRestoreSets(json, requestedIds, out var updated, out var legacy, out _)
                        ? (true, updated, legacy) : (false, json, string.Empty));
            }
            else if (request.Decision == "approved" && action == "review_and_reduce_training_stress" &&
                     (request.TargetExerciseIds is null || request.TargetExerciseIds.Count == 0))
            {
                // Repeated low recovery with no culprit exercise: lower intensity (one more rep in
                // reserve) in the next three sessions instead of leaving the signal without effect.
                applied = await RewriteFutureSessionsAsync(planId, 3,
                    $"Adaptación aprobada por profesional (propuesta #{proposalId}): una repetición más en reserva (RIR +1, máximo 5) durante las próximas tres sesiones por recuperación insuficiente repetida.",
                    transaction, cancellationToken,
                    json => TrainingAdaptationPrescriptionAdjuster.TryRaiseReserve(json, out var updated, out var legacy)
                        ? (true, updated, legacy) : (false, json, string.Empty));
            }
            else if (request.Decision == "approved" && action == "hold_progression_and_review")
                applied = await HoldFutureSessionProgressionAsync(planId, proposalId, transaction, cancellationToken);

            await using (var update = CreateCommand("""
                UPDATE training_adaptation_proposals
                SET status = @status, reviewed_by_user_id = @userId, reviewed_at = NOW(),
                    review_notes = @notes, applied_future_sessions = @applied
                WHERE id = @proposalId AND status = 'pending'
                """, transaction, ("status", request.Decision), ("userId", userId.Value),
                ("notes", request.Notes?.Trim()), ("applied", applied), ("proposalId", proposalId)))
                await update.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            var message = request.Decision == "rejected"
                ? "Propuesta rechazada. No se ha cambiado ninguna sesión."
                : applied > 0
                    ? $"Propuesta aprobada. Se han adaptado {applied} sesiones futuras; el historial permanece intacto."
                    : action == "pause_affected_exercise_and_review"
                        ? "Revisión registrada. Por seguridad no se ha reanudado ni modificado automáticamente el ejercicio asociado al dolor; ajusta manualmente la planificación antes de reanudarlo."
                        : action == "review_and_reduce_training_stress" && (request.TargetExerciseIds is null || request.TargetExerciseIds.Count == 0)
                            ? "Propuesta aprobada y registrada, pero no se ha reducido volumen: selecciona explícitamente los ejercicios con señales repetidas de fatiga para evitar recortar todas las series indiscriminadamente."
                            : "Propuesta aprobada y registrada. No se ha aplicado ningún cambio automático; revisa manualmente la planificación futura si procede.";
            return Ok(new { status = request.Decision, appliedFutureSessions = applied, message });
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Pending manual review requests raised by the client. They never change the plan by themselves.</summary>
    [HttpGet("clients/{clientId:int}/review-requests")]
    public async Task<IActionResult> GetReviewRequests(int clientId, CancellationToken cancellationToken)
    {
        if (!await CanManageClient(clientId, cancellationToken)) return NotFound();
        await using var command = CreateCommand("""
            SELECT r.id, r.plan_id, p.name, p.version, r.note, r.status, r.created_at
            FROM training_review_requests r
            JOIN training_plans p ON p.id = r.plan_id
            WHERE r.client_id = @clientId AND r.status = 'pending'
            ORDER BY r.created_at DESC, r.id DESC
            LIMIT 100
            """, null, ("clientId", clientId));
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new
            {
                id = reader.GetInt64(0), planId = reader.GetInt32(1), planName = reader.GetString(2),
                planVersion = reader.GetInt32(3), note = reader.IsDBNull(4) ? null : reader.GetString(4),
                status = reader.GetString(5), createdAt = reader.GetDateTime(6)
            });
        return Ok(items);
    }

    /// <summary>Marks a request as attended or rejected. Never touches sessions or the plan.</summary>
    [HttpPost("review-requests/{requestId:long}/resolve")]
    public async Task<IActionResult> ResolveReviewRequest(
        long requestId, [FromBody] ResolveTrainingReviewRequest request, CancellationToken cancellationToken)
    {
        if (request.Decision is not ("attended" or "rejected"))
            return BadRequest(new { message = "La decisión debe ser attended o rejected." });
        if (request.Notes?.Length > 2000)
            return BadRequest(new { message = "La nota no puede superar los 2000 caracteres." });
        var clientId = await _context.Database.SqlQueryRaw<int?>(
            """SELECT client_id AS "Value" FROM training_review_requests WHERE id = {0}""", requestId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!clientId.HasValue || !await CanManageClient(clientId.Value, cancellationToken)) return NotFound();
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();

        await using var update = CreateCommand("""
            UPDATE training_review_requests
            SET status = @status, resolved_by_user_id = @userId, resolved_at = NOW(), resolution_notes = @notes
            WHERE id = @id AND status = 'pending'
            """, null, ("status", request.Decision), ("userId", userId.Value),
            ("notes", string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()), ("id", requestId));
        if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
            return Conflict(new { message = "Esta solicitud ya ha sido atendida o rechazada." });
        return Ok(new
        {
            status = request.Decision,
            message = request.Decision == "attended"
                ? "Solicitud marcada como atendida. No se ha modificado ninguna sesión."
                : "Solicitud rechazada. No se ha modificado ninguna sesión."
        });
    }

    [HttpGet("plans/{planId:int}/load-trend")]
    public async Task<IActionResult> GetLoadTrend(int planId, CancellationToken cancellationToken)
    {
        if (!await CanAccessPlan(planId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT date_trunc('week', s.scheduled_date)::date AS week_start,
                   COUNT(r.id) AS completed_sessions,
                   COALESCE(SUM(s.duration_minutes), 0) AS planned_minutes,
                   COALESCE(SUM(r.actual_duration_minutes), 0) AS actual_minutes,
                   COALESCE(SUM(r.actual_duration_minutes * r.session_rpe), 0) AS load_score
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            LEFT JOIN training_session_records r ON r.session_id = s.id AND r.client_id = f.client_id
            WHERE p.id = @planId AND f.enabled = TRUE
              AND s.scheduled_date >= CURRENT_DATE - INTERVAL '11 weeks'
              AND s.scheduled_date <= CURRENT_DATE
            GROUP BY date_trunc('week', s.scheduled_date)::date
            ORDER BY week_start
            """, ("planId", planId));
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new {
                weekStart = reader.GetFieldValue<DateOnly>(0),
                completedSessions = reader.GetInt64(1),
                plannedMinutes = reader.GetInt64(2),
                actualMinutes = reader.GetInt64(3),
                loadScore = reader.GetDecimal(4)
            });
        return Ok(items);
    }
}
