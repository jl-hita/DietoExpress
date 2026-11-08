using System.Data;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anguloso.Server.Logica;

/// <summary>
/// Applies, by itself and inside one transaction, the adaptation proposals that
/// <see cref="TrainingAutoAdaptationPolicy"/> allows, and writes updated working loads into upcoming
/// sessions. Completed history is never touched; only future, planned, unrecorded sessions of an
/// active plan change. Every automatic application is recorded on the proposal (status approved, no
/// reviewer, note starting with <c>auto:&lt;kind&gt;</c>) so a professional can always see and undo it.
/// </summary>
public sealed class TrainingAutoAdaptationApplier
{
    private readonly angulosodbContext _db;
    private readonly ILogger<TrainingAutoAdaptationApplier> _logger;

    public TrainingAutoAdaptationApplier(angulosodbContext db, ILogger<TrainingAutoAdaptationApplier> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Returns the number of sessions changed, or -1 when the proposal was left pending for a person.</summary>
    public async Task<int> TryApplyAsync(
        long proposalId, int planId, string action, IReadOnlyList<string> exerciseIds, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var decision = TrainingAutoAdaptationPolicy.Decide(
                action, exerciseIds, DateTimeOffset.UtcNow, await LoadRecentAsync(connection, transaction, planId, cancellationToken));
            if (!decision.ShouldApply)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return -1;
            }

            var note = $"Ajuste automático del sistema (propuesta #{proposalId}): ";
            var changed = decision.Kind switch
            {
                TrainingAutoAdaptationKind.ReduceSets => await ReduceSetsAsync(connection, transaction, planId, decision.ExerciseIds, note, cancellationToken),
                TrainingAutoAdaptationKind.RaiseReserve => await RewriteAsync(connection, transaction, planId, TrainingAutoAdaptationPolicy.MaximumFutureSessions, null,
                    note + "una repetición más en reserva (RIR +1, máximo 5) en las próximas tres sesiones por recuperación insuficiente repetida.", cancellationToken,
                    json => TrainingAdaptationPrescriptionAdjuster.TryRaiseReserve(json, out var updated, out var legacy) ? (true, updated, legacy) : (false, json, string.Empty)),
                TrainingAutoAdaptationKind.RestoreSets => await RewriteAsync(connection, transaction, planId, TrainingAutoAdaptationPolicy.MaximumFutureSessions, null,
                    note + "se recupera una serie reducida antes (sin superar la dosis original).", cancellationToken,
                    json => TrainingAdaptationPrescriptionAdjuster.TryRestoreSets(json, decision.ExerciseIds.ToHashSet(StringComparer.Ordinal), out var updated, out var legacy, out _) ? (true, updated, legacy) : (false, json, string.Empty)),
                TrainingAutoAdaptationKind.Deload => await RewriteAsync(connection, transaction, planId, 14,
                    DateOnly.FromDateTime(DateTime.UtcNow).AddDays(TrainingAutoAdaptationPolicy.DeloadWindowDays),
                    note + "semana de descarga conservadora (mitad de series, RIR +2) por estrés acumulado en las últimas sesiones.", cancellationToken,
                    json => TrainingAdaptationPrescriptionAdjuster.TryApplyDeload(json, out var updated, out var legacy) ? (true, updated, legacy) : (false, json, string.Empty)),
                TrainingAutoAdaptationKind.AddMuscleSet or TrainingAutoAdaptationKind.ReduceMuscleSet => await RewriteAsync(
                    connection, transaction, planId, 1, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
                    note + (decision.Kind == TrainingAutoAdaptationKind.AddMuscleSet
                        ? "una serie más (poco estímulo repetido, bien recuperado y con margen de volumen)."
                        : "una serie menos (estímulo alto y fatiga acumulada repetidos)."), cancellationToken,
                    json => TrainingAdaptationPrescriptionAdjuster.TryAdjustSets(json, decision.ExerciseIds.FirstOrDefault() ?? string.Empty,
                        decision.Kind == TrainingAutoAdaptationKind.AddMuscleSet ? 1 : -1, out var updated, out var legacy) ? (true, updated, legacy) : (false, json, string.Empty)),
                _ => await HoldProgressionAsync(connection, transaction, planId, note, cancellationToken)
            };

            await using (var update = new NpgsqlCommand("""
                UPDATE training_adaptation_proposals
                SET status = 'approved', reviewed_by_user_id = NULL, reviewed_at = NOW(),
                    review_notes = @notes, applied_future_sessions = @applied
                WHERE id = @id AND status = 'pending'
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("notes", $"auto:{TrainingAutoAdaptationPolicy.DescribeKind(decision.Kind)}|Aplicada automáticamente por el sistema; un profesional puede revisarla o deshacerla.");
                update.Parameters.AddWithValue("applied", changed);
                update.Parameters.AddWithValue("id", proposalId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return changed;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            // The proposal stays pending for a person; an adaptation failure must never fail the patient's record.
            _logger.LogWarning(exception, "No se pudo aplicar automáticamente la propuesta {ProposalId}.", proposalId);
            return -1;
        }
    }

    /// <summary>
    /// Writes the progression engine's suggested working load (reduce or increase) into each upcoming
    /// session, so the next workout already shows the adjusted weight.
    /// </summary>
    public async Task<int> ApplyLoadSuggestionsAsync(int clientId, CancellationToken cancellationToken)
    {
        var recommendations = await new TrainingExerciseProgressionService(_db).GetRecommendationsAsync(clientId, cancellationToken);
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        var changed = 0;
        foreach (var recommendation in recommendations.Where(item =>
                     item.Action is "reduce_load" or "increase_load" && item.SuggestedLoadKg is > 0))
        {
            string? json;
            await using (var select = new NpgsqlCommand("""
                SELECT s.exercise_prescription_data::text FROM training_sessions s
                JOIN training_plans p ON p.id = s.plan_id
                WHERE s.id = @id AND s.scheduled_date >= CURRENT_DATE AND s.status = 'planned' AND p.status = 'active'
                  AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id)
                """, connection))
            {
                select.Parameters.AddWithValue("id", recommendation.SessionId);
                json = await select.ExecuteScalarAsync(cancellationToken) as string;
            }
            if (json is null) continue;
            var verb = recommendation.Action == "reduce_load" ? "reducida" : "aumentada";
            if (!TrainingAdaptationPrescriptionAdjuster.TrySetLoad(
                    json, recommendation.ExerciseId, recommendation.SuggestedLoadKg!.Value,
                    $"Carga {verb} automáticamente a partir de tu historial reciente.", out var updated, out var legacy))
                continue;
            await using var update = new NpgsqlCommand("""
                UPDATE training_sessions SET exercise_prescription_data = @json::jsonb, exercise_prescription = @text, updated_at = NOW()
                WHERE id = @id AND status = 'planned'
                  AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = training_sessions.id)
                """, connection);
            update.Parameters.AddWithValue("json", updated);
            update.Parameters.AddWithValue("text", legacy);
            update.Parameters.AddWithValue("id", recommendation.SessionId);
            changed += await update.ExecuteNonQueryAsync(cancellationToken);
        }
        return changed;
    }

    private static async Task<IReadOnlyList<TrainingAutoAdaptationRecord>> LoadRecentAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int planId, CancellationToken cancellationToken)
    {
        var result = new List<TrainingAutoAdaptationRecord>();
        await using var command = new NpgsqlCommand("""
            SELECT review_notes, reviewed_at FROM training_adaptation_proposals
            WHERE plan_id = @planId AND status = 'approved' AND reviewed_by_user_id IS NULL
              AND review_notes LIKE 'auto:%' AND reviewed_at > NOW() - INTERVAL '28 days'
            """, connection, transaction);
        command.Parameters.AddWithValue("planId", planId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var kindText = reader.GetString(0).Split('|')[0]["auto:".Length..];
            var kind = Enum.GetValues<TrainingAutoAdaptationKind>()
                .FirstOrDefault(value => TrainingAutoAdaptationPolicy.DescribeKind(value) == kindText);
            if (kind != TrainingAutoAdaptationKind.None)
                result.Add(new(kind, new DateTimeOffset(reader.GetDateTime(1), TimeSpan.Zero)));
        }
        return result;
    }

    private static async Task<List<(int Id, string Json, string? Instructions)>> SelectFutureSessionsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int planId, DateOnly? untilDate, CancellationToken cancellationToken)
    {
        var rows = new List<(int, string, string?)>();
        await using var select = new NpgsqlCommand("""
            SELECT s.id, s.exercise_prescription_data::text, s.instructions
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id AND p.status = 'active'
            WHERE s.plan_id = @planId AND s.scheduled_date > CURRENT_DATE AND s.status = 'planned'
              AND (CAST(@until AS date) IS NULL OR s.scheduled_date <= CAST(@until AS date))
              AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id)
              AND s.exercise_prescription_data IS NOT NULL
            ORDER BY s.scheduled_date, s.id FOR UPDATE
            """, connection, transaction);
        select.Parameters.AddWithValue("planId", planId);
        select.Parameters.AddWithValue("until", NpgsqlTypes.NpgsqlDbType.Date, (object?)untilDate ?? DBNull.Value);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return rows;
    }

    private static async Task<int> WriteSessionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int sessionId, string json, string legacy,
        string? instructions, string note, CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand("""
            UPDATE training_sessions
            SET exercise_prescription_data = @json::jsonb, exercise_prescription = @text,
                instructions = @instructions, updated_at = NOW()
            WHERE id = @id AND scheduled_date > CURRENT_DATE AND status = 'planned'
              AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = training_sessions.id)
            """, connection, transaction);
        update.Parameters.AddWithValue("json", json);
        update.Parameters.AddWithValue("text", legacy);
        update.Parameters.AddWithValue("instructions", string.IsNullOrWhiteSpace(instructions) ? note : $"{instructions.Trim()}\n{note}");
        update.Parameters.AddWithValue("id", sessionId);
        return await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> RewriteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int planId, int maxSessions, DateOnly? untilDate,
        string note, CancellationToken cancellationToken, Func<string, (bool Changed, string Json, string Legacy)> rewrite)
    {
        var changed = 0;
        foreach (var row in await SelectFutureSessionsAsync(connection, transaction, planId, untilDate, cancellationToken))
        {
            if (changed >= maxSessions) break;
            var (didChange, json, legacy) = rewrite(row.Json);
            if (!didChange) continue;
            changed += await WriteSessionAsync(connection, transaction, row.Id, json, legacy, row.Instructions, note, cancellationToken);
        }
        return changed;
    }

    private static async Task<int> ReduceSetsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int planId, IReadOnlyList<string> exerciseIds,
        string note, CancellationToken cancellationToken)
    {
        var remaining = exerciseIds.ToDictionary(id => id, _ => TrainingAutoAdaptationPolicy.MaximumFutureSessions, StringComparer.Ordinal);
        var changed = 0;
        foreach (var row in await SelectFutureSessionsAsync(connection, transaction, planId, null, cancellationToken))
        {
            var eligible = remaining.Where(item => item.Value > 0).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            if (eligible.Count == 0) break;
            if (!TrainingAdaptationPrescriptionAdjuster.TryReduceSets(row.Json, eligible, out var updated, out var legacy, out var reduced)) continue;
            var affected = await WriteSessionAsync(connection, transaction, row.Id, updated, legacy, row.Instructions,
                $"{note}reducción de una serie durante un máximo de tres exposiciones futuras por ejercicio ({string.Join(", ", reduced)}) por fatiga repetida.", cancellationToken);
            changed += affected;
            if (affected > 0)
                foreach (var id in reduced) remaining[id] = Math.Max(0, remaining[id] - 1);
        }
        return changed;
    }

    private static async Task<int> HoldProgressionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int planId, string note, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE training_sessions s
            SET intensity_target = 'Mantener la exigencia actual; sin progresión hasta nueva evaluación',
                instructions = CASE WHEN COALESCE(s.instructions, '') = '' THEN @note ELSE s.instructions || E'\n' || @note END,
                updated_at = NOW()
            WHERE s.plan_id = @planId AND s.scheduled_date > CURRENT_DATE AND s.status = 'planned'
              AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id)
              AND EXISTS (SELECT 1 FROM training_plans p WHERE p.id = s.plan_id AND p.status = 'active')
              AND s.id IN (SELECT id FROM training_sessions WHERE plan_id = @planId AND scheduled_date > CURRENT_DATE
                           AND status = 'planned' ORDER BY scheduled_date, id LIMIT 3)
            """, connection, transaction);
        command.Parameters.AddWithValue("note", note + "se mantiene la exigencia actual (sin progresión) en las próximas sesiones.");
        command.Parameters.AddWithValue("planId", planId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
