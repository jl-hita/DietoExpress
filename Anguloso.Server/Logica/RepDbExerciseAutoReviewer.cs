using System.Text.Json;

namespace Anguloso.Server.Logica;

/// <summary>
/// Conservative, deterministic first-pass mapping from RepDB metadata to DietoExpress's
/// controlled vocabulary. This is metadata enrichment, not a claim of human review.
/// </summary>
internal static class RepDbExerciseAutoReviewer
{
    internal sealed record ReviewMetadata(
        string MovementPattern,
        string[] PrimaryMuscles,
        string[] SecondaryMuscles,
        string MinimumExperience,
        string[] SuitableGoals,
        string[] RequiredEquipment,
        int SelectionPriority,
        string Notes);

    public static ReviewMetadata? TryBuild(
        string id, string? nameEs, string nameEn, string? descriptionEs,
        string instructionsEsJson, string? difficulty, string? equipment,
        string? forceType, string? mechanic, string? bodyPart,
        string primaryMusclesJson, string secondaryMusclesJson, string goalsJson,
        string tagsJson, bool? isBodyweight)
    {
        var name = (nameEs ?? nameEn).Trim();
        if (string.IsNullOrWhiteSpace(nameEs) || nameEs.Trim().Length < 3 ||
            !HasUsefulInstructions(instructionsEsJson) ||
            string.IsNullOrWhiteSpace(descriptionEs) ||
            string.IsNullOrWhiteSpace(equipment))
            return null;

        var sourcePrimary = ReadStrings(primaryMusclesJson);
        var sourceSecondary = ReadStrings(secondaryMusclesJson);
        var primary = sourcePrimary.Select(MapMuscle).Where(x => x is not null).Cast<string>().Distinct().ToArray();
        var secondary = sourceSecondary.Select(MapMuscle).Where(x => x is not null).Cast<string>()
            .Except(primary, StringComparer.OrdinalIgnoreCase).Distinct().ToArray();
        if (primary.Length == 0 || sourcePrimary.Length == 0)
            return null;

        var pattern = InferPattern(id, name, nameEn, tagsJson, forceType, mechanic, bodyPart, sourcePrimary);
        if (pattern is null)
            return null;

        var exp = difficulty?.Trim().ToLowerInvariant() switch
        {
            "beginner" or "easy" or "principiante" => "Beginner",
            "intermediate" or "moderate" or "intermedio" => "Intermediate",
            "advanced" or "expert" or "hard" or "avanzado" => "Advanced",
            _ => (string?)null
        };
        if (exp is null) return null;

        var goals = ReadStrings(goalsJson).Select(MapGoal).Where(x => x is not null).Cast<string>().Distinct().ToArray();
        if (goals.Length == 0)
        {
            // General fitness is the only safe default when the source has no interpretable goal.
            goals = ["GeneralFitness"];
        }

        var requiredEquipment = MapEquipment(equipment, isBodyweight);
        if (requiredEquipment is null) return null;

        return new ReviewMetadata(pattern, primary, secondary, exp, goals, requiredEquipment,
            pattern is "Squat" or "Hinge" or "HorizontalPush" or "HorizontalPull" ? 85 : 75,
            "Enriquecimiento automático RepDB v1: mapeo determinista de metadatos de origen; verificar si cambian las reglas.");
    }

    private static string? InferPattern(string id, string es, string en, string tagsJson,
        string? force, string? mechanic, string? bodyPart, string[] muscles)
    {
        var text = string.Join(' ', id, es, en, string.Join(' ', ReadStrings(tagsJson))).ToLowerInvariant();
        var mech = mechanic?.ToLowerInvariant() ?? "";
        var forceType = force?.ToLowerInvariant() ?? "";
        var part = bodyPart?.ToLowerInvariant() ?? "";
        // Strong name/ID cues first; ambiguous exercises stay pending.
        if (Has(text, "squat", "sentadilla", "leg press", "prensa de piernas")) return "Squat";
        if (Has(text, "deadlift", "peso muerto", "good morning", "hip thrust", "puente de gluteos", "glute bridge", "back extension", "extension lumbar")) return "Hinge";
        if (Has(text, "bench press", "press de banca", "push-up", "push up", "flexion", "chest press", "floor press")) return "HorizontalPush";
        if (Has(text, "overhead press", "shoulder press", "military press", "press militar", "arnold press")) return "VerticalPush";
        if (Has(text, "row", "remo", "seated cable row", "bent-over row")) return "HorizontalPull";
        if (Has(text, "pull-up", "pull up", "chin-up", "chin up", "lat pulldown", "jalon al pecho")) return "VerticalPull";
        if (Has(text, "lunge", "zancada", "split squat", "step-up", "step up", "pistol squat", "single-leg")) return "SingleLeg";
        if (Has(text, "crunch", "sit-up", "sit up", "plank", "plancha", "dead bug", "hollow hold", "russian twist", "abdominal")) return "Core";

        // Do not infer a movement pattern from muscle group alone: several patterns share muscles.
        return null;
    }

    private static string? MapMuscle(string value)
    {
        var v = Normalize(value);
        if (Has(v, "chest", "pector", "pectoral")) return "Chest";
        if (Has(v, "lat", "dorsal ancho")) return "Lats";
        if (Has(v, "upper back", "middle back", "trap", "rhomboid", "espalda alta", "romboide", "trapecio")) return "UpperBack";
        if (Has(v, "shoulder", "deltoid", "hombro", "deltoides")) return "Deltoids";
        if (Has(v, "bicep", "bíceps")) return "Biceps";
        if (Has(v, "tricep", "tríceps")) return "Triceps";
        if (Has(v, "forearm", "antebrazo")) return "Forearms";
        if (Has(v, "abdominal", "abs", "rectus abdominis", "core", "abdomen")) return "Abdominals";
        if (Has(v, "oblique", "oblicuo")) return "Obliques";
        if (Has(v, "erector spinae", "lower back", "lumbar", "erectores espinales")) return "ErectorSpinae";
        if (Has(v, "glute", "gluteo", "glúteo")) return "Glutes";
        if (Has(v, "quadricep", "quad", "cuádriceps", "cuadriceps")) return "Quadriceps";
        if (Has(v, "hamstring", "isquiotibial")) return "Hamstrings";
        if (Has(v, "calf", "calves", "soleus", "gastrocnemius", "gemelo", "sóleo")) return "Calves";
        return null;
    }

    private static string? MapGoal(string value)
    {
        var v = Normalize(value);
        if (Has(v, "strength", "powerlifting", "fuerza")) return "Strength";
        if (Has(v, "hypertrophy", "muscle gain", "bodybuilding", "hipertrofia", "musculacion")) return "Hypertrophy";
        if (Has(v, "fitness", "general", "conditioning", "health", "acondicionamiento", "salud")) return "GeneralFitness";
        return null;
    }

    private static string[]? MapEquipment(string value, bool? bodyweight)
    {
        var v = Normalize(value);
        if (bodyweight == true || Has(v, "bodyweight", "none", "no equipment", "peso corporal", "ninguno", "sin material"))
            return [];
        if (Has(v, "barbell", "barra")) return ["barbell"];
        if (Has(v, "dumbbell", "mancuerna")) return ["dumbbell"];
        if (Has(v, "kettlebell", "pesa rusa")) return ["kettlebell"];
        if (Has(v, "cable", "polea")) return ["cable"];
        if (Has(v, "machine", "máquina", "maquina")) return ["machine"];
        if (Has(v, "resistance band", "band", "banda elástica", "banda elastica")) return ["resistance-band"];
        if (Has(v, "smith")) return ["smith-machine"];
        if (Has(v, "ez-bar", "curl bar")) return ["ez-bar"];
        if (Has(v, "bench", "banco")) return ["bench"];
        if (Has(v, "pull-up bar", "barra de dominadas")) return ["pull-up-bar"];
        if (Has(v, "medicine ball", "balón medicinal", "balon medicinal")) return ["medicine-ball"];
        if (Has(v, "foam roller", "rodillo")) return ["foam-roller"];
        return null;
    }

    private static bool HasUsefulInstructions(string json)
    {
        var instructions = ReadStrings(json);
        return instructions.Length >= 2 && instructions.All(x => x.Trim().Length >= 8);
    }

    private static string[] ReadStrings(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            return doc.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()?.Trim() ?? "")
                .Where(x => x.Length > 0).ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static string Normalize(string value) => value.ToLowerInvariant()
        .Replace("á", "a").Replace("é", "e").Replace("í", "i")
        .Replace("ó", "o").Replace("ú", "u").Replace("ü", "u");

    private static bool Has(string text, params string[] terms) =>
        terms.Any(term => text.Contains(Normalize(term), StringComparison.OrdinalIgnoreCase));
}
