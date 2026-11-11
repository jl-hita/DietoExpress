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
/// Acceso a clientes/planes, notificaciones y utilidades de acceso a datos compartidas por todas las partes.
/// </summary>
public sealed partial class TrainingPeriodizationController
{
    private async Task<IReadOnlyList<TrainingExerciseVolumeRecommendation>> GetExerciseVolumeRecommendationsAsync(
        int planId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null)
    {
        var records = new List<TrainingExerciseVolumeFeedbackRecord>();
        await using (var command = CreateCommand("""
            SELECT r.outcome, r.exercise_feedback::text, s.exercise_prescription_data::text
            FROM training_session_records r
            JOIN training_sessions s ON s.id = r.session_id
            WHERE s.plan_id = @planId AND s.session_type = 'strength' AND r.outcome IS NOT NULL
            ORDER BY s.scheduled_date DESC, r.id DESC
            LIMIT 12
            """, transaction, ("planId", planId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                records.Add(new TrainingExerciseVolumeFeedbackRecord(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map(records);
        return TrainingExerciseVolumeAdapter.Evaluate(exposures);
    }

    private async Task<int> ReduceFutureSessionVolumeAsync(
        int planId, long proposalId, IReadOnlyList<string> targetExerciseIds,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        // Session-wide recovery cannot identify a target exercise. Require explicit IDs from
        // exercise-level evidence; an empty selection must never reduce the whole plan.
        var selectedExerciseIds = targetExerciseIds.ToHashSet(StringComparer.Ordinal);
        if (selectedExerciseIds.Count == 0)
            return 0;

        var rows = new List<(int Id, string Json, string? Instructions)>();
        await using (var select = CreateCommand("""
            SELECT s.id, s.exercise_prescription_data::text, s.instructions
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id AND p.status = 'active'
            WHERE s.plan_id = @planId AND s.scheduled_date > CURRENT_DATE AND s.status = 'planned'
              AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id)
              AND s.exercise_prescription_data IS NOT NULL
            ORDER BY s.scheduled_date, s.id FOR UPDATE
            """, transaction, ("planId", planId)))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                rows.Add((reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));

        const int maximumFutureExposureReductionsPerExercise = 3;
        var remainingReductions = selectedExerciseIds.ToDictionary(
            exerciseId => exerciseId, _ => maximumFutureExposureReductionsPerExercise, StringComparer.Ordinal);
        var changed = 0;
        foreach (var row in rows)
        {
            var eligibleExerciseIds = remainingReductions
                .Where(item => item.Value > 0)
                .Select(item => item.Key)
                .ToHashSet(StringComparer.Ordinal);
            if (eligibleExerciseIds.Count == 0)
                break;

            if (!TrainingAdaptationPrescriptionAdjuster.TryReduceSets(
                    row.Json, eligibleExerciseIds, out var updatedJson, out var updatedText, out var reducedExerciseIds))
                continue;

            var note = $"Adaptación aprobada por profesional (propuesta #{proposalId}): reducción de una serie durante un máximo de tres exposiciones futuras por ejercicio ({string.Join(", ", reducedExerciseIds)}).";
            var instructions = string.IsNullOrWhiteSpace(row.Instructions) ? note : $"{row.Instructions.Trim()}\n{note}";
            await using var update = CreateCommand("""
                UPDATE training_sessions
                SET exercise_prescription_data = @json::jsonb, exercise_prescription = @prescription,
                    instructions = @instructions, updated_at = NOW()
                WHERE id = @sessionId AND scheduled_date > CURRENT_DATE AND status = 'planned'
                  AND EXISTS (SELECT 1 FROM training_plans p WHERE p.id = training_sessions.plan_id AND p.status = 'active')
                  AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = training_sessions.id)
                """, transaction, ("json", updatedJson), ("prescription", updatedText),
                ("instructions", instructions), ("sessionId", row.Id));
            var affected = await update.ExecuteNonQueryAsync(cancellationToken);
            changed += affected;
            if (affected > 0)
            {
                foreach (var exerciseId in reducedExerciseIds)
                    remainingReductions[exerciseId] = Math.Max(0, remainingReductions[exerciseId] - 1);
            }
        }
        return changed;
    }

    /// <summary>
    /// Rewrites up to <paramref name="maxSessions"/> future, still-unrecorded sessions of an active plan
    /// with a pure JSON transformation (reserve raise, set restoration). Completed history is never
    /// touched and only an approved proposal reaches this code.
    /// </summary>
    private async Task<int> RewriteFutureSessionsAsync(
        int planId, int maxSessions, string note, NpgsqlTransaction transaction, CancellationToken cancellationToken,
        Func<string, (bool Changed, string Json, string Legacy)> rewrite)
    {
        var rows = new List<(int Id, string Json, string? Instructions)>();
        await using (var select = CreateCommand("""
            SELECT s.id, s.exercise_prescription_data::text, s.instructions
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id AND p.status = 'active'
            WHERE s.plan_id = @planId AND s.scheduled_date > CURRENT_DATE AND s.status = 'planned'
              AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id)
              AND s.exercise_prescription_data IS NOT NULL
            ORDER BY s.scheduled_date, s.id FOR UPDATE
            """, transaction, ("planId", planId)))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                rows.Add((reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));

        var changed = 0;
        foreach (var row in rows)
        {
            if (changed >= maxSessions) break;
            var (didChange, json, legacy) = rewrite(row.Json);
            if (!didChange) continue;
            var instructions = string.IsNullOrWhiteSpace(row.Instructions) ? note : $"{row.Instructions.Trim()}\n{note}";
            await using var update = CreateCommand("""
                UPDATE training_sessions
                SET exercise_prescription_data = @json::jsonb, exercise_prescription = @prescription,
                    instructions = @instructions, updated_at = NOW()
                WHERE id = @sessionId AND scheduled_date > CURRENT_DATE AND status = 'planned'
                  AND EXISTS (SELECT 1 FROM training_plans p WHERE p.id = training_sessions.plan_id AND p.status = 'active')
                  AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = training_sessions.id)
                """, transaction, ("json", json), ("prescription", legacy),
                ("instructions", instructions), ("sessionId", row.Id));
            changed += await update.ExecuteNonQueryAsync(cancellationToken);
        }
        return changed;
    }

    private async Task<int> HoldFutureSessionProgressionAsync(int planId, long proposalId, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var note = $"Adaptación aprobada por profesional (propuesta #{proposalId}): mantener la exigencia actual, sin progresión hasta nueva revisión.";
        await using var command = CreateCommand("""
            UPDATE training_sessions s
            SET intensity_target = 'Mantener la exigencia actual; sin progresión hasta nueva revisión profesional',
                instructions = CASE
                    WHEN COALESCE(s.instructions, '') LIKE '%' || @note || '%' THEN s.instructions
                    WHEN COALESCE(s.instructions, '') = '' THEN @note
                    ELSE s.instructions || E'\n' || @note
                END,
                updated_at = NOW()
            WHERE s.plan_id = @planId AND s.scheduled_date > CURRENT_DATE AND s.status = 'planned'
              AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id)
            """, transaction, ("planId", planId), ("note", note));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> CanAccessClient(int clientId, CancellationToken cancellationToken) =>
        await CanAccessClientInternal(clientId, manage: false, cancellationToken);

    private async Task<bool> CanManageClient(int clientId, CancellationToken cancellationToken) =>
        await CanAccessClientInternal(clientId, manage: true, cancellationToken);

    private async Task<bool> CanAccessClientInternal(int clientId, bool manage, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return false;
        var superAdmin = User.IsInRole("superadmin");
        var clinicAdmin = User.IsInRole("clinic_admin");
        await using var command = Command("""
            SELECT EXISTS (
                SELECT 1 FROM clients c
                WHERE c.id = @clientId AND c.archived_at IS NULL
                  AND (@superAdmin OR (
                      c.tenant_id = (SELECT u.tenant_id FROM users u WHERE u.id = @userId)
                      AND (@clinicAdmin OR c.user_id = @userId
                           OR EXISTS (SELECT 1 FROM client_nutritionist_assignments a WHERE a.client_id = c.id AND a.nutritionist_id = @userId AND a.is_active)
                           OR (NOT @manage AND EXISTS (
                               SELECT 1 FROM training_collaborations tc
                               WHERE tc.client_id = c.id AND tc.professional_user_id = @userId
                                 AND tc.status = 'active'
                           ))
                      )
                  ))
            )
            """, ("clientId", clientId), ("userId", userId.Value), ("superAdmin", superAdmin), ("clinicAdmin", clinicAdmin), ("manage", manage));
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<bool> CanAccessPlan(int planId, CancellationToken cancellationToken) =>
        await CanAccessPlanInternal(planId, manage: false, cancellationToken);

    private async Task<bool> CanManagePlan(int planId, CancellationToken cancellationToken) =>
        await CanAccessPlanInternal(planId, manage: true, cancellationToken);

    private async Task<bool> CanAccessPlanInternal(int planId, bool manage, CancellationToken cancellationToken)
    {
        var clientId = await _context.Database.SqlQueryRaw<int?>(
            """SELECT f.client_id AS "Value" FROM training_plans p JOIN training_profiles f ON f.id = p.profile_id WHERE p.id = {0}""", planId)
            .SingleOrDefaultAsync(cancellationToken);
        return clientId.HasValue && await CanAccessClientInternal(clientId.Value, manage, cancellationToken);
    }


    private async Task<int?> GetMesocyclePlanId(int mesocycleId, CancellationToken cancellationToken) =>
        await _context.Database.SqlQueryRaw<int?>(
            """SELECT plan_id AS "Value" FROM training_mesocycles WHERE id = {0}""", mesocycleId)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<int?> GetSessionClientId(int sessionId, CancellationToken cancellationToken) =>
        await _context.Database.SqlQueryRaw<int?>(
            """SELECT f.client_id AS "Value" FROM training_sessions s JOIN training_plans p ON p.id = s.plan_id JOIN training_profiles f ON f.id = p.profile_id WHERE s.id = {0}""", sessionId)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task NotifyTrainingPlanAssignedAsync(int clientId, int planId, string planName, CancellationToken cancellationToken)
    {
        var tenantId = await _context.clients.AsNoTracking()
            .Where(c => c.id == clientId && c.archived_at == null)
            .Select(c => c.tenant_id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!tenantId.HasValue) return;

        try
        {
            var notifications = HttpContext.RequestServices.GetService<Anguloso.Server.Logica.NotificationService>();
            if (notifications != null)
                await notifications.CreateForPatientAsync(tenantId.Value, clientId, "training_plan_assigned",
                    "Nuevo plan de entrenamiento", $"Tu profesional ha preparado el plan «{planName}». Ya puedes consultarlo en DietoExpress.",
                    "/patient?tab=training", true, $"training-plan:{planId}:assigned");
            var push = HttpContext.RequestServices.GetService<Anguloso.Server.Logica.PatientPushService>();
            if (push != null)
                await push.SendToClientAsync(clientId, "Nuevo plan de entrenamiento",
                    "Tu profesional ha preparado un plan. Abre DietoExpress para consultarlo.",
                    "training", $"training-plan:{planId}:assigned", cancellationToken);
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<TrainingPeriodizationController>>()
                .LogWarning(ex, "No se pudo notificar la asignación del plan de entrenamiento {PlanId}.", planId);
        }
    }

    private NpgsqlCommand Command(string sql, params (string Name, object? Value)[] parameters) => CreateCommand(sql, null, parameters);

    private NpgsqlCommand Command(string sql, (string Name, object? Value) first, (string Name, object? Value) second, NpgsqlTransaction transaction) =>
        CreateCommand(sql, transaction, first, second);

    private NpgsqlCommand Command(string sql, (string Name, object? Value) first, NpgsqlTransaction transaction) =>
        CreateCommand(sql, transaction, first);

    private NpgsqlCommand Command(string sql, (string Name, object? Value) first, (string Name, object? Value) second,
        (string Name, object? Value) third, NpgsqlTransaction transaction) =>
        CreateCommand(sql, transaction, first, second, third);

    private NpgsqlCommand CreateCommand(string sql, NpgsqlTransaction? transaction, params (string Name, object? Value)[] parameters)
    {
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (transaction is not null) command.Transaction = transaction;
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
