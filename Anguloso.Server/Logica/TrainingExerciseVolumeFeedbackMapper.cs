using System.Text.Json;

namespace Anguloso.Server.Logica;

/// <summary>
/// Maps persisted session/exercise feedback JSON into the pure volume-adapter contract.
/// The query supplying records must order newest-first; this mapper reverses them to the
/// oldest-to-newest history order expected by the adapter.
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
            if (string.IsNullOrWhiteSpace(record.ExerciseFeedbackJson))
                continue;

            try
            {
                using var feedbackDocument = JsonDocument.Parse(record.ExerciseFeedbackJson);
                if (feedbackDocument.RootElement.ValueKind != JsonValueKind.Array)
                    continue;
                var targetRir = ReadTargetRir(record.PrescriptionJson);

                foreach (var exercise in feedbackDocument.RootElement.EnumerateArray())
                {
                    var exerciseId = ReadString(exercise, "exerciseId");
                    if (string.IsNullOrWhiteSpace(exerciseId))
                        continue;
                    var plannedSets = ReadInt(exercise, "plannedSets");
                    var completedSets = ReadInt(exercise, "completedSets");
                    if (plannedSets is < 1 or > 100 || completedSets < 0 || completedSets > plannedSets)
                        continue;

                    var skippedReason = ReadString(exercise, "skippedSetReason");
                    var rirValues = ReadCompletedWorkingSetRir(exercise);
                    var averageRir = completedSets > 0 && rirValues.Count == completedSets ? rirValues.Average() : (decimal?)null;
                    exposures.Add(new TrainingExerciseVolumeExposure(
                        exerciseId,
                        plannedSets,
                        completedSets,
                        skippedReason,
                        SessionCompleted: string.Equals(record.Outcome, "completed", StringComparison.OrdinalIgnoreCase),
                        // The session-level pain signal is intentionally not copied to every exercise.
                        // Only an exercise's own skipped-set reason identifies the affected exercise.
                        PainReported: string.Equals(skippedReason, "pain", StringComparison.OrdinalIgnoreCase),
                        TargetRir: targetRir.GetValueOrDefault(exerciseId),
                        ActualAverageRir: averageRir));
                }
            }
            catch (JsonException)
            {
                // Legacy or malformed feedback is ignored; missing data never becomes a fatigue signal.
            }
            catch (InvalidOperationException)
            {
                // A syntactically valid JSON value can still have an unexpected shape.
            }
        }

        return exposures;
    }

    private static IReadOnlyDictionary<string, decimal?> ReadTargetRir(string? prescriptionJson)
    {
        var result = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(prescriptionJson))
            return result;
        try
        {
            using var document = JsonDocument.Parse(prescriptionJson);
            if (!document.RootElement.TryGetProperty("exercises", out var exercises) ||
                exercises.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var exercise in exercises.EnumerateArray())
            {
                var id = ReadString(exercise, "exerciseId");
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                decimal? rir = null;
                if (exercise.TryGetProperty("targetRepetitionsInReserve", out var rirElement) &&
                    rirElement.ValueKind == JsonValueKind.Number && rirElement.TryGetDecimal(out var value))
                    rir = value;
                result[id] = rir;
            }
        }
        catch (JsonException)
        {
            // Malformed/legacy prescription data is not grounds for inventing an RIR target.
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

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result) ? result : 0;
}

public sealed record TrainingExerciseVolumeFeedbackRecord(
    string Outcome,
    string? ExerciseFeedbackJson,
    string? PrescriptionJson);
