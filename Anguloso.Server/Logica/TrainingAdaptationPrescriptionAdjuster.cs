using System.Text.Json;
using System.Text.Json.Nodes;

namespace Anguloso.Server.Logica;

/// <summary>
/// Applies an explicitly reviewed, muscle-targeted volume reduction to future-session JSON.
/// It never changes completed history and refuses broad reductions when muscle metadata is absent.
/// </summary>
public static class TrainingAdaptationPrescriptionAdjuster
{
    public static bool TryReduceSets(
        string json,
        IReadOnlySet<TrainingMuscleGroup> targetMuscles,
        out string updatedJson,
        out string legacyPrescription)
    {
        updatedJson = json;
        legacyPrescription = string.Empty;
        if (targetMuscles is null || targetMuscles.Count == 0)
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

                // Compound exercises affect every listed muscle. Only reduce an exercise when
                // the professional explicitly selected all its reviewed primary and secondary
                // groups; otherwise the change would silently under-dose an unselected muscle.
                if (!TryReadMuscleGroups(exercise["primaryMuscleGroups"], out var primary) || primary.Count == 0 ||
                    !TryReadMuscleGroups(exercise["secondaryMuscleGroups"], out var secondary))
                    continue;
                var affectedGroups = primary.Concat(secondary).Distinct().ToArray();
                if (affectedGroups.Length == 0 || affectedGroups.Any(group => !targetMuscles.Contains(group)))
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

    private static bool TryReadMuscleGroups(JsonNode? node, out IReadOnlyList<TrainingMuscleGroup> groups)
    {
        groups = Array.Empty<TrainingMuscleGroup>();
        if (node is not JsonArray array)
            return false;

        var result = new List<TrainingMuscleGroup>();
        foreach (var item in array)
        {
            TrainingMuscleGroup muscle;
            if (item is JsonValue value && value.TryGetValue<int>(out var numeric) &&
                Enum.IsDefined(typeof(TrainingMuscleGroup), numeric))
                muscle = (TrainingMuscleGroup)numeric;
            else if (item is JsonValue text && text.TryGetValue<string>(out var name) &&
                     Enum.TryParse<TrainingMuscleGroup>(name, true, out var parsed) &&
                     Enum.IsDefined(parsed))
                muscle = parsed;
            else
                return false;
            result.Add(muscle);
        }

        if (result.Distinct().Count() != result.Count)
            return false;
        groups = result;
        return true;
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
