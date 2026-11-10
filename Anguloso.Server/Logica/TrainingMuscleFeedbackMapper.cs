using System.Text.Json;

namespace Anguloso.Server.Logica;

/// <summary>
/// Maps the newest-first session history into chronological per-exercise muscle feedback.
/// Every prescribed exercise receives an exposure; missing answers are neutral, and an unusable
/// newest prescription blocks older history from being mistaken for recent evidence.
/// </summary>
public static class TrainingMuscleFeedbackMapper
{
    public static IReadOnlyList<TrainingMuscleFeedbackExposure> Map(
        IEnumerable<TrainingMuscleFeedbackRecord> newestFirst)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        var records = newestFirst.ToArray();
        if (records.Length == 0)
            return Array.Empty<TrainingMuscleFeedbackExposure>();

        var parsed = new List<(TrainingMuscleFeedbackRecord Record, IReadOnlyList<PrescribedExercise> Exercises,
            IReadOnlyDictionary<string, JsonElement> Feedback)>();
        foreach (var record in records)
        {
            var exercises = ReadPrescription(record.PrescriptionJson);
            if (exercises is null || exercises.Count == 0)
            {
                // If the newest record is unusable, no older feedback can safely replace it.
                // If a later/older record is unusable, keep only the newer valid window.
                if (parsed.Count == 0)
                    return Array.Empty<TrainingMuscleFeedbackExposure>();
                break;
            }

            parsed.Add((record, exercises, ReadFeedback(record.ExerciseFeedbackJson)));
        }

        var exposures = new List<TrainingMuscleFeedbackExposure>();
        foreach (var item in parsed.AsEnumerable().Reverse())
        {
            var completed = string.Equals(item.Record.Outcome, "completed", StringComparison.OrdinalIgnoreCase);
            foreach (var exercise in item.Exercises)
            {
                if (exercise.PrimaryMuscles.Count == 0)
                    continue;

                item.Feedback.TryGetValue(exercise.ExerciseId, out var feedback);
                exposures.Add(new TrainingMuscleFeedbackExposure(
                    item.Record.Date,
                    exercise.ExerciseId,
                    exercise.PrimaryMuscles,
                    exercise.Sets,
                    feedback.ValueKind == JsonValueKind.Object ? ReadString(feedback, "stimulus") : null,
                    feedback.ValueKind == JsonValueKind.Object ? ReadString(feedback, "discomfort") : null,
                    feedback.ValueKind == JsonValueKind.Object ? ReadString(feedback, "preFatigue") : null,
                    completed));
            }
        }

        return exposures;
    }

    private static IReadOnlyList<PrescribedExercise>? ReadPrescription(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("exercises", out var exercises) ||
                exercises.ValueKind != JsonValueKind.Array)
                return null;

            var result = new List<PrescribedExercise>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exercise in exercises.EnumerateArray())
            {
                if (exercise.ValueKind != JsonValueKind.Object)
                    return null;
                var id = ReadString(exercise, "exerciseId");
                if (string.IsNullOrWhiteSpace(id) ||
                    !exercise.TryGetProperty("sets", out var setsElement) ||
                    setsElement.ValueKind != JsonValueKind.Number ||
                    !setsElement.TryGetInt32(out var sets) || sets is < 1 or > 100 ||
                    !exercise.TryGetProperty("primaryMuscleGroups", out var musclesElement) ||
                    musclesElement.ValueKind != JsonValueKind.Array ||
                    !ids.Add(id))
                    return null;

                var muscles = musclesElement.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                result.Add(new PrescribedExercise(id, sets, muscles));
            }

            return result.Count == 0 ? null : result;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadFeedback(string? json)
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
                if (exercise.ValueKind != JsonValueKind.Object)
                    continue;
                var id = ReadString(exercise, "exerciseId");
                if (!string.IsNullOrWhiteSpace(id))
                    result.TryAdd(id, exercise.Clone());
            }
        }
        catch (JsonException)
        {
            // Invalid feedback is missing information, never evidence for a volume change.
        }
        catch (InvalidOperationException)
        {
            // Unexpected JSON shapes are treated as missing feedback.
        }

        return result;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record PrescribedExercise(
        string ExerciseId,
        int Sets,
        IReadOnlyList<string> PrimaryMuscles);
}

public sealed record TrainingMuscleFeedbackRecord(
    DateOnly Date,
    string Outcome,
    string? ExerciseFeedbackJson,
    string? PrescriptionJson);
