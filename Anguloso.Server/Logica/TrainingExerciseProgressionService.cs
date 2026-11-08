using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Anguloso.Server.Models;

namespace Anguloso.Server.Logica;

/// <summary>
/// Loads the patient's recent per-exercise execution history and evaluates future sessions.
/// Data access stays separate from the pure progression engine so its rules remain unit-testable.
/// </summary>
public sealed class TrainingExerciseProgressionService
{
    private readonly angulosodbContext _context;

    public TrainingExerciseProgressionService(angulosodbContext context) => _context = context;

    public async Task<IReadOnlyList<TrainingExerciseProgressionRecommendationDto>> GetRecommendationsAsync(
        int clientId, CancellationToken cancellationToken)
    {
        var history = new Dictionary<(int PlanId, string ExerciseId), List<Anguloso.Server.Logica.TrainingExerciseExposure>>();

        // Bound history independently for each active plan. A single global LIMIT could
        // let a busy/older plan crowd out every record from another active plan, even though
        // progression is deliberately isolated by plan and stable exercise ID. Keep enough
        // recent sessions per plan for the engine's three-exposure rules and future extensions.
        await using (var historyCommand = Command("""
            WITH ranked_history AS (
                SELECT s.plan_id, s.session_type, s.exercise_prescription_data::text AS prescription_json,
                       r.exercise_feedback::text AS feedback_json, r.outcome,
                       r.recovery_rating, r.pain_signal, s.scheduled_date, s.id,
                       ROW_NUMBER() OVER (
                           PARTITION BY s.plan_id
                           ORDER BY s.scheduled_date DESC, s.id DESC
                       ) AS plan_record_rank
                FROM training_session_records r
                JOIN training_sessions s ON s.id = r.session_id
                JOIN training_plans p ON p.id = s.plan_id
                JOIN training_profiles f ON f.id = p.profile_id
                WHERE f.client_id = @clientId AND f.enabled = TRUE AND p.status = 'active'
                  AND r.outcome IS NOT NULL AND s.session_type = 'strength'
            )
            SELECT plan_id, session_type, prescription_json, feedback_json, outcome, recovery_rating, pain_signal
            FROM ranked_history
            WHERE plan_record_rank <= 60
            ORDER BY scheduled_date DESC, id DESC
            """, ("clientId", clientId)))
        await using (var reader = await historyCommand.ExecuteReaderAsync(cancellationToken))
        {
            var blockedPlans = new HashSet<int>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var planId = reader.GetInt32(0);
                // Cardio, mobility and recovery sessions are not exercise-progression exposures.
                if (!string.Equals(reader.GetString(1), "strength", StringComparison.OrdinalIgnoreCase))
                    continue;
                // The query is newest-first. Once a strength session lacks a usable structured
                // prescription, older records for that plan cannot safely stand in for it.
                if (blockedPlans.Contains(planId)) continue;
                if (reader.IsDBNull(2))
                {
                    blockedPlans.Add(planId);
                    continue;
                }

                var completed = reader.GetString(4) == "completed";
                var recoveryInsufficient = !reader.IsDBNull(5) && reader.GetString(5) == "insufficient";
                var sessionPain = !reader.IsDBNull(6) && reader.GetString(6) == "new_or_worsening";
                var recordedExerciseIds = new HashSet<string>(StringComparer.Ordinal);
                var progressionEligibility = new Dictionary<string, bool>(StringComparer.Ordinal);
                var historicalPrescriptions = new Dictionary<string, Anguloso.Server.Logica.TrainingExerciseProgressionPrescriptionSnapshot>(StringComparer.Ordinal);
                try
                {
                    // The historical prescription identifies deload/non-progressable exposures
                    // and the exact range/RIR/sets that applied to this session.
                    using var prescriptionDocument = JsonDocument.Parse(reader.GetString(2));
                    if (prescriptionDocument.RootElement.ValueKind != JsonValueKind.Object ||
                        !prescriptionDocument.RootElement.TryGetProperty("exercises", out var prescribed) ||
                        prescribed.ValueKind != JsonValueKind.Array)
                    {
                        blockedPlans.Add(planId);
                        continue;
                    }

                    var prescribedItems = prescribed.EnumerateArray().ToArray();
                    if (prescribedItems.Length == 0 ||
                        prescribedItems.Any(item => item.ValueKind != JsonValueKind.Object ||
                            string.IsNullOrWhiteSpace(JsonString(item, "exerciseId"))))
                    {
                        blockedPlans.Add(planId);
                        continue;
                    }

                    foreach (var item in prescribedItems)
                    {
                        var exerciseId = JsonString(item, "exerciseId")!;
                        progressionEligibility[exerciseId] =
                            ParseProgressionRule(item) == TrainingProgressionRule.DoubleProgression;
                        historicalPrescriptions[exerciseId] = new(
                            JsonInt(item, "sets"),
                            JsonInt(item, "minRepetitions"),
                            JsonInt(item, "maxRepetitions"),
                            JsonDecimal(item, "targetRepetitionsInReserve"));
                    }
                }
                catch (JsonException)
                {
                    // Unknown historical prescription metadata must not become progression evidence.
                    blockedPlans.Add(planId);
                    continue;
                }

                try
                {
                    if (!reader.IsDBNull(3))
                    {
                        using var document = JsonDocument.Parse(reader.GetString(3));
                        if (document.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var exercise in document.RootElement.EnumerateArray())
                            {
                                if (exercise.ValueKind != JsonValueKind.Object) continue;
                                var exerciseId = JsonString(exercise, "exerciseId");
                                if (string.IsNullOrWhiteSpace(exerciseId)) continue;
                                recordedExerciseIds.Add(exerciseId);
                                var sets = new List<Anguloso.Server.Logica.TrainingExecutedSet>();
                                if (exercise.TryGetProperty("sets", out var setArray) &&
                                    setArray.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var set in setArray.EnumerateArray())
                                    {
                                        // Warm-up sets are intentionally excluded from the progression signal:
                                        // they prepare the lifter but are not part of the prescribed work-set target.
                                        if (JsonString(set, "kind") != "working") continue;
                                        sets.Add(new(
                                            "working",
                                            JsonDecimal(set, "weightKg"),
                                            JsonInt(set, "repetitions"),
                                            JsonDecimal(set, "rir"),
                                            JsonBool(set, "completed")));
                                    }
                                }

                                var pain = sessionPain || JsonString(exercise, "skippedSetReason") == "pain";
                                var key = (planId, exerciseId);
                                if (!history.TryGetValue(key, out var exposures))
                                    history[key] = exposures = [];
                                historicalPrescriptions.TryGetValue(exerciseId, out var historicalPrescription);
                                exposures.Add(new(
                                    sets, completed, recoveryInsufficient, pain,
                                    ProgressionEligible: progressionEligibility.GetValueOrDefault(exerciseId),
                                    HistoricalPrescription: historicalPrescription,
                                    SkippedSetReason: JsonString(exercise, "skippedSetReason")));
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // Corrupt execution feedback must not prevent recording a missing exposure
                    // from the independent prescription payload below.
                }

                // A prescribed exercise absent from the execution payload is not a successful
                // exposure. Represent it as missing data so the engine cannot skip it and progress.
                foreach (var (exerciseId, progressionEligible) in progressionEligibility)
                {
                    if (recordedExerciseIds.Contains(exerciseId)) continue;
                    var key = (planId, exerciseId);
                    if (!history.TryGetValue(key, out var exposures))
                        history[key] = exposures = [];
                    historicalPrescriptions.TryGetValue(exerciseId, out var historicalPrescription);
                    exposures.Add(new(Array.Empty<Anguloso.Server.Logica.TrainingExecutedSet>(),
                        completed, recoveryInsufficient, sessionPain, ProgressionEligible: progressionEligible,
                        HistoricalPrescription: historicalPrescription));
                }
            }
        }

        // The history query is newest-first; the pure engine requires chronological order.
        foreach (var exposures in history.Values)
            exposures.Reverse();

        var recommendations = new List<TrainingExerciseProgressionRecommendationDto>();
        await using var upcomingCommand = Command("""
            SELECT s.id, s.plan_id, s.exercise_prescription_data::text
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE f.client_id = @clientId AND f.enabled = TRUE AND p.status = 'active'
              AND s.scheduled_date >= CURRENT_DATE AND s.exercise_prescription_data IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM training_session_records existing
                  WHERE existing.session_id = s.id AND existing.client_id = @clientId
                    AND existing.outcome IS NOT NULL
              )
            ORDER BY s.scheduled_date, s.id
            LIMIT 100
            """, ("clientId", clientId));
        await using var upcomingReader = await upcomingCommand.ExecuteReaderAsync(cancellationToken);
        while (await upcomingReader.ReadAsync(cancellationToken))
        {
            var sessionId = upcomingReader.GetInt32(0);
            var planId = upcomingReader.GetInt32(1);
            try
            {
                using var document = JsonDocument.Parse(upcomingReader.GetString(2));
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("exercises", out var exercises) ||
                    exercises.ValueKind != JsonValueKind.Array) continue;

                foreach (var exercise in exercises.EnumerateArray())
                {
                    if (exercise.ValueKind != JsonValueKind.Object) continue;
                    var exerciseId = JsonString(exercise, "exerciseId");
                    if (string.IsNullOrWhiteSpace(exerciseId)) continue;
                    var rule = ParseProgressionRule(exercise);
                    var prescription = new Anguloso.Server.Logica.TrainingExerciseProgressionPrescription(
                        exerciseId,
                        JsonInt(exercise, "sets") ?? 0,
                        JsonInt(exercise, "minRepetitions") ?? 0,
                        JsonInt(exercise, "maxRepetitions") ?? 0,
                        JsonDecimal(exercise, "targetRepetitionsInReserve"),
                        rule,
                        JsonDecimal(exercise, "smallestLoadIncrementKg"),
                        JsonDecimal(exercise, "maximumLoadIncreasePercent"));
                    history.TryGetValue((planId, exerciseId), out var exposures);
                    IReadOnlyList<Anguloso.Server.Logica.TrainingExerciseExposure> exerciseHistory =
                        exposures is null ? Array.Empty<Anguloso.Server.Logica.TrainingExerciseExposure>() : exposures;
                    var result = Anguloso.Server.Logica.TrainingExerciseProgressionEngine.Evaluate(
                        prescription, exerciseHistory);
                    recommendations.Add(new(
                        sessionId,
                        exerciseId,
                        JsonString(exercise, "exerciseNameSnapshot"),
                        ToApiProgressionAction(result.Action),
                        result.Explanation,
                        result.SuggestedLoadKg,
                        result.LoadIncreaseKg,
                        result.SuggestedRepetitionsBySet,
                        result.MaximumLoadIncreasePercent));
                }
            }
            catch (JsonException)
            {
                // An invalid or legacy prescription must not produce an invented recommendation.
            }
        }

        return recommendations;
    }

    /// <summary>
    /// Last recorded working performance per exercise (any of the client's plans, newest record wins),
    /// used by the generator to prescribe starting loads. Warm-up and incomplete sets are ignored;
    /// an exercise with no usable set simply has no reference.
    /// </summary>
    public async Task<IReadOnlyList<TrainingLoadReference>> GetLoadReferencesAsync(
        int clientId, CancellationToken cancellationToken)
    {
        var references = new Dictionary<string, TrainingLoadReference>(StringComparer.Ordinal);
        await using var command = Command("""
            SELECT r.exercise_feedback::text
            FROM training_session_records r
            JOIN training_sessions s ON s.id = r.session_id
            WHERE r.client_id = @clientId AND r.outcome IS NOT NULL AND s.session_type = 'strength'
              AND r.exercise_feedback IS NOT NULL
            ORDER BY s.scheduled_date DESC, s.id DESC
            LIMIT 40
            """, ("clientId", clientId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            try
            {
                using var document = JsonDocument.Parse(reader.GetString(0));
                if (document.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var exercise in document.RootElement.EnumerateArray())
                {
                    var exerciseId = JsonString(exercise, "exerciseId");
                    if (string.IsNullOrWhiteSpace(exerciseId) || references.ContainsKey(exerciseId)) continue;
                    if (!exercise.TryGetProperty("sets", out var setArray) || setArray.ValueKind != JsonValueKind.Array) continue;
                    var sets = setArray.EnumerateArray()
                        .Where(set => JsonString(set, "kind") == "working" && JsonBool(set, "completed"))
                        .Select(set => (Weight: JsonDecimal(set, "weightKg"), Reps: JsonInt(set, "repetitions"), Rir: JsonDecimal(set, "rir")))
                        .Where(set => set.Weight is > 0 && set.Reps is >= 1)
                        .ToArray();
                    if (sets.Length == 0) continue;
                    var top = sets.Max(set => set.Weight!.Value);
                    var atTop = sets.Where(set => set.Weight == top).ToArray();
                    var rirs = atTop.Where(set => set.Rir is not null).Select(set => set.Rir!.Value).ToArray();
                    references[exerciseId] = new TrainingLoadReference(
                        exerciseId, top, atTop.Min(set => set.Reps!.Value),
                        rirs.Length == atTop.Length ? rirs.Average() : null, atTop.Length);
                }
            }
            catch (JsonException)
            {
                // A corrupt record must not block generation; it simply provides no reference.
            }
        }
        return references.Values.ToArray();
    }

    private static string? JsonString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static int? JsonInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number : null;

    private static decimal? JsonDecimal(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out var number)
            ? number : null;

    private static bool JsonBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static Anguloso.Server.Logica.TrainingProgressionRule ParseProgressionRule(JsonElement exercise)
    {
        if (exercise.ValueKind != JsonValueKind.Object ||
            !exercise.TryGetProperty("progressionRule", out var value))
            return Anguloso.Server.Logica.TrainingProgressionRule.None;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric) &&
            Enum.IsDefined(typeof(Anguloso.Server.Logica.TrainingProgressionRule), numeric))
            return (Anguloso.Server.Logica.TrainingProgressionRule)numeric;
        if (value.ValueKind == JsonValueKind.String &&
            Enum.TryParse<Anguloso.Server.Logica.TrainingProgressionRule>(value.GetString(), true, out var parsed))
            return parsed;
        return Anguloso.Server.Logica.TrainingProgressionRule.None;
    }

    private static string ToApiProgressionAction(Anguloso.Server.Logica.TrainingExerciseProgressionAction action) => action switch
    {
        Anguloso.Server.Logica.TrainingExerciseProgressionAction.InvalidPrescription => "invalid_prescription",
        Anguloso.Server.Logica.TrainingExerciseProgressionAction.InvalidData => "invalid_data",
        Anguloso.Server.Logica.TrainingExerciseProgressionAction.InsufficientData => "insufficient_data",
        Anguloso.Server.Logica.TrainingExerciseProgressionAction.NotApplicable => "not_applicable",
        Anguloso.Server.Logica.TrainingExerciseProgressionAction.HoldAndReview => "hold_and_review",
        Anguloso.Server.Logica.TrainingExerciseProgressionAction.AddRepetitions => "add_repetitions",
        Anguloso.Server.Logica.TrainingExerciseProgressionAction.ConfirmTopOfRange => "confirm_top_of_range",
        Anguloso.Server.Logica.TrainingExerciseProgressionAction.StagnationReview => "stagnation_review",
        Anguloso.Server.Logica.TrainingExerciseProgressionAction.ReduceLoad => "reduce_load",
        Anguloso.Server.Logica.TrainingExerciseProgressionAction.IncreaseLoad => "increase_load",
        // Unknown enum values must never become a positive load-progression recommendation.
        _ => "invalid_data"
    };


    private DbCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) connection.Open();
        var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}

public sealed record TrainingExerciseProgressionRecommendationDto(
    int SessionId,
    string ExerciseId,
    string? ExerciseName,
    string Action,
    string Explanation,
    decimal? SuggestedLoadKg,
    decimal? LoadIncreaseKg,
    IReadOnlyList<int>? SuggestedRepetitionsBySet,
    decimal? MaximumLoadIncreasePercent);

