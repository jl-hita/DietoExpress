using System.Text.Json;

namespace Anguloso.Server.Logica;

/// <summary>
/// Maps persisted session/exercise feedback JSON into the pure volume-adapter contract.
/// The query supplying records must order newest-first; this mapper reverses them to the
/// oldest-to-newest history order expected by the adapter. Every prescribed exercise receives
/// an exposure, including a neutral missing-data exposure when the client did not submit feedback.
/// </summary>
public static class TrainingExerciseVolumeFeedbackMapper
{
    public static IReadOnlyList<TrainingExerciseVolumeExposure> Map(
        IEnumerable<TrainingExerciseVolumeFeedbackRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var exposures = new List<TrainingExerciseVolumeExposure>();
        foreach (var record in records.Reverse())
        {
            var prescription = ReadPrescription(record.PrescriptionJson);
            if (prescription.Count == 0)
            {
                // A recent session without a parseable structured prescription cannot be
                // matched safely to exercise IDs. Fail closed rather than reusing stale history.
                return Array.Empty<TrainingExerciseVolumeExposure>();
            }

            var feedbackById = ReadFeedback(record.ExerciseFeedbackJson);
            foreach (var (exerciseId, target) in prescription)
            {
                if (!feedbackById.TryGetValue(exerciseId, out var exercise) ||
                    ReadInt(exercise, "plannedSets") != target.PlannedSets)
                {
                    // Missing or mismatched feedback is unknown, never a fatigue signal.
                    exposures.Add(new TrainingExerciseVolumeExposure(
                        exerciseId, target.PlannedSets, 0,
                        SessionCompleted: IsCompleted(record.Outcome),
                        TargetRir: target.TargetRir));
                    continue;
                }

                var completedSets = ReadInt(exercise, "completedSets");
                if (completedSets < 0 || completedSets > target.PlannedSets)
                {
                    exposures.Add(new TrainingExerciseVolumeExposure(
                        exerciseId, target.PlannedSets, 0,
                        SessionCompleted: IsCompleted(record.Outcome),
                        TargetRir: target.TargetRir));
                    continue;
                }

                var skippedReason = ReadString(exercise, "skippedSetReason");
                var rirValues = ReadCompletedWorkingSetRir(exercise);
                var averageRir = completedSets > 0 && rirValues.Count == completedSets
                    ? rirValues.Average()
                    : (decimal?)null;
                exposures.Add(new TrainingExerciseVolumeExposure(
                    exerciseId,
                    target.PlannedSets,
                    completedSets,
                    skippedReason,
                    SessionCompleted: IsCompleted(record.Outcome),
                    // Session-wide pain is intentionally not copied to every exercise.
                    PainReported: string.Equals(skippedReason, "pain", StringComparison.OrdinalIgnoreCase),
                    TargetRir: target.TargetRir,
                    ActualAverageRir: averageRir));
            }
        }

        return exposures;
    }

    private static Dictionary<string, (int PlannedSets, decimal? TargetRir)> ReadPrescription(string? json)
    {
        var result = new Dictionary<string, (int PlannedSets, decimal? TargetRir)>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("exercises", out var exercises) ||
                exercises.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var exercise in exercises.EnumerateArray())
            {
                if (exercise.ValueKind != JsonValueKind.Object)
                    return new Dictionary<string, (int PlannedSets, decimal? TargetRir)>(StringComparer.Ordinal);
                var id = ReadString(exercise, "exerciseId");
                if (string.IsNullOrWhiteSpace(id) ||
                    !exercise.TryGetProperty("sets", out var setsElement) ||
                    setsElement.ValueKind != JsonValueKind.Number ||
                    !setsElement.TryGetInt32(out var plannedSets) ||
                    plannedSets is < 1 or > 100)
                    return new Dictionary<string, (int PlannedSets, decimal? TargetRir)>(StringComparer.Ordinal);
                decimal? rir = null;
                if (exercise.TryGetProperty("targetRepetitionsInReserve", out var rirElement) &&
                    rirElement.ValueKind == JsonValueKind.Number && rirElement.TryGetDecimal(out var value) &&
                    value is >= 0 and <= 10)
                    rir = value;
                if (!result.TryAdd(id, (plannedSets, rir)))
                    return new Dictionary<string, (int PlannedSets, decimal? TargetRir)>(StringComparer.Ordinal);
            }
        }
        catch (JsonException)
        {
            // Malformed/legacy prescriptions cannot identify exercises safely.
        }
        catch (InvalidOperationException)
        {
            // A syntactically valid JSON value can still have an unexpected shape.
        }
        return result;
    }

    private static Dictionary<string, JsonElement> ReadFeedback(string? json)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var exercise in document.RootElement.EnumerateArray())
            {
                var id = ReadString(exercise, "exerciseId");
                if (!string.IsNullOrWhiteSpace(id))
                    result.TryAdd(id, exercise.Clone());
            }
        }
        catch (JsonException)
        {
            // Invalid feedback is treated as missing data, not as fatigue.
        }
        catch (InvalidOperationException)
        {
            // Unexpected JSON shapes are treated as missing data.
        }
        return result;
    }

    private static IReadOnlyList<decimal> ReadCompletedWorkingSetRir(JsonElement exercise)
    {
        if (!exercise.TryGetProperty("sets", out var sets) || sets.ValueKind != JsonValueKind.Array)
            return Array.Empty<decimal>();
        var values = new List<decimal>();
        foreach (var set in sets.EnumerateArray())
        {
            if (ReadString(set, "kind") != "working" ||
                !set.TryGetProperty("completed", out var completed) ||
                completed.ValueKind != JsonValueKind.True ||
                !set.TryGetProperty("rir", out var rir) ||
                rir.ValueKind != JsonValueKind.Number ||
                !rir.TryGetDecimal(out var value) || value is < 0 or > 10)
                continue;
            values.Add(value);
        }
        return values;
    }

    private static bool IsCompleted(string outcome) =>
        string.Equals(outcome, "completed", StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result) ? result : 0;
}

public sealed record TrainingExerciseVolumeFeedbackRecord(
    string Outcome,
    string? ExerciseFeedbackJson,
    string? PrescriptionJson);
