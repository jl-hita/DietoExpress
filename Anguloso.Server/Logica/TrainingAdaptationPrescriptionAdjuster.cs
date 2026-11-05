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
        out string legacyPrescription)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
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

                exercise["sets"] = sets - 1;
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
