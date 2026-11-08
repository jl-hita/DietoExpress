using System.Text.Json;
using System.Text.Json.Nodes;

namespace Anguloso.Server.Logica;

/// <summary>
/// Applies an explicitly reviewed, exercise-ID-targeted volume reduction to future-session JSON.
/// It never changes completed history or infers targets from names or unrelated exercise metadata.
/// </summary>
public static class TrainingAdaptationPrescriptionAdjuster
{
    public static bool TryReduceSets(
        string json,
        IReadOnlySet<string> targetExerciseIds,
        out string updatedJson,
        out string legacyPrescription) =>
        TryReduceSets(json, targetExerciseIds, out updatedJson, out legacyPrescription, out _);

    public static bool TryReduceSets(
        string json,
        IReadOnlySet<string> targetExerciseIds,
        out string updatedJson,
        out string legacyPrescription,
        out IReadOnlySet<string> reducedExerciseIds)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        var reduced = new HashSet<string>(StringComparer.Ordinal);
        reducedExerciseIds = reduced;
        if (targetExerciseIds is null || targetExerciseIds.Count == 0)
            return false;

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray exercises)
                return false;

            var changed = false;
            foreach (var item in exercises)
            {
                if (item is not JsonObject exercise || exercise["sets"] is not JsonValue setsNode ||
                    !setsNode.TryGetValue<int>(out var sets) || sets <= 1)
                    continue;

                // The adapter proposes changes for stable exercise IDs only. Do not infer a
                // target from names or modify a different exercise with similar wording.
                var exerciseId = ReadString(exercise, "exerciseId", string.Empty);
                if (string.IsNullOrWhiteSpace(exerciseId) || !targetExerciseIds.Contains(exerciseId))
                    continue;

                // Remember the plan's original dose the first time it is reduced, so volume can later be
                // restored (one set at a time) when the fatigue signal disappears.
                exercise["adaptationOriginalSets"] ??= sets;
                exercise["sets"] = sets - 1;
                reduced.Add(exerciseId);
                changed = true;
            }

            if (!changed)
                return false;

            legacyPrescription = string.Join(Environment.NewLine, exercises.OfType<JsonObject>()
                .Select(FormatExercise).Where(line => !string.IsNullOrWhiteSpace(line)));
            if (string.IsNullOrWhiteSpace(legacyPrescription))
                return false;

            updatedJson = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// Gives back one set to exercises whose volume was previously reduced by an approved adaptation
    /// (marker <c>adaptationOriginalSets</c>), never above the original dose. The marker is removed
    /// once the original dose is reached.
    /// </summary>
    public static bool TryRestoreSets(
        string json,
        IReadOnlySet<string> targetExerciseIds,
        out string updatedJson,
        out string legacyPrescription,
        out IReadOnlySet<string> restoredExerciseIds)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        var restored = new HashSet<string>(StringComparer.Ordinal);
        restoredExerciseIds = restored;
        if (targetExerciseIds is null || targetExerciseIds.Count == 0) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray exercises) return false;
            foreach (var item in exercises)
            {
                if (item is not JsonObject exercise ||
                    exercise["sets"] is not JsonValue setsNode || !setsNode.TryGetValue<int>(out var sets) ||
                    exercise["adaptationOriginalSets"] is not JsonValue originalNode ||
                    !originalNode.TryGetValue<int>(out var original) || sets >= original)
                    continue;
                var exerciseId = ReadString(exercise, "exerciseId", string.Empty);
                if (string.IsNullOrWhiteSpace(exerciseId) || !targetExerciseIds.Contains(exerciseId)) continue;
                exercise["sets"] = sets + 1;
                if (sets + 1 >= original) exercise.Remove("adaptationOriginalSets");
                restored.Add(exerciseId);
            }
            if (restored.Count == 0) return false;
            legacyPrescription = string.Join(Environment.NewLine, exercises.OfType<JsonObject>()
                .Select(FormatExercise).Where(line => !string.IsNullOrWhiteSpace(line)));
            updatedJson = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return !string.IsNullOrWhiteSpace(legacyPrescription);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// Lowers session intensity by asking for one more repetition in reserve (maximum 5) in every
    /// exercise that has a RIR target. Used when low recovery repeats but no single exercise can be
    /// blamed, so volume is left untouched. Internal product rule; the professional approves it.
    /// </summary>
    public static bool TryRaiseReserve(string json, out string updatedJson, out string legacyPrescription)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["exercises"] is not JsonArray exercises) return false;
            var changed = false;
            foreach (var item in exercises)
            {
                if (item is not JsonObject exercise ||
                    exercise["targetRepetitionsInReserve"] is not JsonValue rirNode ||
                    !rirNode.TryGetValue<decimal>(out var rir) || rir >= 5m)
                    continue;
                exercise["targetRepetitionsInReserve"] = Math.Min(5m, rir + 1m);
                changed = true;
            }
            if (!changed) return false;
            legacyPrescription = string.Join(Environment.NewLine, exercises.OfType<JsonObject>()
                .Select(FormatExercise).Where(line => !string.IsNullOrWhiteSpace(line)));
            updatedJson = root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return !string.IsNullOrWhiteSpace(legacyPrescription);
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    private static string FormatExercise(JsonObject exercise)
    {
        var order = ReadInt(exercise, "order", 0);
        var name = ReadString(exercise, "exerciseNameSnapshot", "Ejercicio");
        var sets = ReadInt(exercise, "sets", 1);
        var min = ReadInt(exercise, "minRepetitions", 1);
        var max = ReadInt(exercise, "maxRepetitions", min);
        var rir = exercise["targetRepetitionsInReserve"] is JsonValue rirNode && rirNode.TryGetValue<decimal>(out var rirValue)
            ? rirValue.ToString() : "—";
        var rest = ReadInt(exercise, "restSeconds", 0);
        var progression = ReadProgression(exercise["progressionRule"]);
        var id = ReadString(exercise, "exerciseId", string.Empty);
        var version = ReadString(exercise, "catalogVersion", string.Empty);
        return $"{order}. {name} — {sets} × {min}-{max} reps; RIR {rir}; descanso {rest}s; progresión {progression}. [ID: {id}; catálogo: {version}]";
    }

    private static int ReadInt(JsonObject node, string key, int fallback) =>
        node[key] is JsonValue value && value.TryGetValue<int>(out var result) ? result : fallback;
    private static string ReadString(JsonObject node, string key, string fallback) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var result) ? result : fallback;
    private static string ReadProgression(JsonNode? value)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var numeric) &&
            Enum.IsDefined(typeof(TrainingProgressionRule), numeric))
            return ((TrainingProgressionRule)numeric).ToString();
        return value?.ToString() ?? "—";
    }
}
