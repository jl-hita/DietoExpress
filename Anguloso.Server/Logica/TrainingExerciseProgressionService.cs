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

        // Keep the latest 60 records per patient across active plans. We group by plan and
        // stable exercise ID so feedback from another plan or a similarly named movement
        // can never influence this exercise's recommendation.
        await using (var historyCommand = Command("""
            SELECT s.plan_id, s.exercise_prescription_data::text, r.exercise_feedback::text, r.outcome, r.recovery_rating, r.pain_signal
            FROM training_session_records r
            JOIN training_sessions s ON s.id = r.session_id
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE f.client_id = @clientId AND f.enabled = TRUE AND p.status = 'active'
              AND r.outcome IS NOT NULL AND s.exercise_prescription_data IS NOT NULL
            ORDER BY s.scheduled_date DESC, s.id DESC
            LIMIT 60
            """, ("clientId", clientId)))
        await using (var reader = await historyCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var planId = reader.GetInt32(0);
                var completed = reader.GetString(3) == "completed";
                var recoveryInsufficient = !reader.IsDBNull(4) && reader.GetString(4) == "insufficient";
                var sessionPain = !reader.IsDBNull(5) && reader.GetString(5) == "new_or_worsening";
                var recordedExerciseIds = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                        if (!reader.IsDBNull(2))
                        {
                            using var document = JsonDocument.Parse(reader.GetString(2));
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
                                    exposures.Add(new(sets, completed, recoveryInsufficient, pain));
                                }
                            }
                        }
                }
                catch (JsonException)
                {
                    // Corrupt execution feedback must not prevent recording a missing exposure
                    // from the independent prescription payload below.
                }

                try
                {
                        // A prescribed exercise absent from the execution payload is not a successful
                        // exposure. Represent it as missing data so the engine cannot skip it and progress.
                        if (!reader.IsDBNull(1))
                        {
                            using var prescriptionDocument = JsonDocument.Parse(reader.GetString(1));
                            if (prescriptionDocument.RootElement.ValueKind == JsonValueKind.Object &&
                                prescriptionDocument.RootElement.TryGetProperty("exercises", out var prescribed) &&
                                prescribed.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in prescribed.EnumerateArray())
                                {
                                    if (item.ValueKind != JsonValueKind.Object) continue;
                                    var exerciseId = JsonString(item, "exerciseId");
                                    if (string.IsNullOrWhiteSpace(exerciseId) || recordedExerciseIds.Contains(exerciseId)) continue;
                                    var key = (planId, exerciseId);
                                    if (!history.TryGetValue(key, out var exposures))
                                        history[key] = exposures = [];
                                    exposures.Add(new(Array.Empty<Anguloso.Server.Logica.TrainingExecutedSet>(),
                                        completed, recoveryInsufficient, sessionPain));
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Corrupt legacy feedback is ignored, never interpreted as a successful set.
                }
                catch (JsonException)
                {
                    // Corrupt legacy prescriptions cannot identify exercises safely.
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

    private static string? JsonString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static int? JsonInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
            ? number : null;

    private static decimal? JsonDecimal(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.TryGetDecimal(out var number)
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
        _ => "increase_load"
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

