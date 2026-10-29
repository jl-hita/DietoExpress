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
        if (RepDbExerciseCuration.ShouldExclude(id)
            || RepDbExerciseCuration.RequiresManualReview(id))
            return null;

        if (string.IsNullOrWhiteSpace(nameEs) || nameEs.Trim().Length < 3 ||
            !HasUsefulInstructions(instructionsEsJson) ||
            string.IsNullOrWhiteSpace(descriptionEs))
            return null;

        var sourcePrimary = ReadStrings(primaryMusclesJson);
        var sourceSecondary = ReadStrings(secondaryMusclesJson);
        var primary = sourcePrimary.Select(MapMuscle).Where(x => x is not null).Cast<string>().Distinct().ToArray();
        var secondary = sourceSecondary.Select(MapMuscle).Where(x => x is not null).Cast<string>()
            .Except(primary, StringComparer.OrdinalIgnoreCase).Distinct().ToArray();
        if (primary.Length == 0 || sourcePrimary.Length == 0)
            return null;

        var pattern = InferPattern(id, nameEs, nameEn, tagsJson, forceType, mechanic, bodyPart, sourcePrimary);
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

        var requiredEquipment = MapEquipment(equipment ?? "", isBodyweight);
        if (requiredEquipment is null) return null;

        // Isolation/accessory work is valuable, but should rank below the main movement options
        // when the generator chooses one exercise for a movement-pattern slot.
        var selectionPriority = IsAccessoryExercise(id, nameEs, nameEn)
            ? 55
            : pattern is "Squat" or "Hinge" or "HorizontalPush" or "HorizontalPull" ? 85 : 75;

        return new ReviewMetadata(pattern, primary, secondary, exp, goals, requiredEquipment,
            selectionPriority,
            "Enriquecimiento automático RepDB v3: mapeo determinista de metadatos de origen; verificar si cambian las reglas.");
    }

    private static string? InferPattern(string id, string es, string en, string tagsJson,
        string? force, string? mechanic, string? bodyPart, string[] muscles)
    {
        var text = Normalize(string.Join(' ', id, es, en, string.Join(' ', ReadStrings(tagsJson))));
        // Strong name/ID cues first; ambiguous exercises stay pending.
        if (Has(text, "squat", "sentadilla", "leg press", "prensa de piernas", "reverse nordic")) return "Squat";
        if (Has(text, "deadlift", "peso muerto", "good morning", "hip thrust", "puente de gluteos", "glute bridge", "hip bridge", "puente de cadera", "back extension", "extension lumbar",
            "kettlebell swing", "dumbbell swing", "one-arm swing", "swing con mancuerna", "pull-through", "pull through")) return "Hinge";
        if (Has(text, "bench press", "press de banca", "push-up", "push up", "flexion", "chest press", "floor press",
            "landmine press", "svend press", "push press", "tricep kickback", "patada de triceps",
            "dip machine", "dips", "fondos")) return "HorizontalPush";
        if (Has(text, "overhead press", "shoulder press", "military press", "press militar", "arnold press",
            "landmine press", "push press")) return "VerticalPush";
        if (Has(text, "row", "remo", "seated cable row", "bent-over row")) return "HorizontalPull";
        if (Has(text, "pull-up", "pull up", "chin-up", "chin up", "lat pulldown", "jalon al pecho")) return "VerticalPull";
        if (Has(text, "lunge", "zancada", "split squat", "step-up", "step up", "pistol squat", "single-leg")) return "SingleLeg";
        if (Has(text, "crunch", "sit-up", "sit up", "plank", "plancha", "dead bug", "hollow hold", "russian twist", "abdominal",
            "leg raise", "knee raise", "knee tuck", "ball pike", "pike con pelota", "flutter kick", "scissor kick", "windshield wiper", "pallof press", "farmers walk",
            "farmer's walk", "suitcase carry", "overhead carry", "bench leg pull-in", "bench leg pull in",
            "ab rollout", "ab wheel", "rueda abdominal", "v-up", "v up", "tuck hold")) return "Core";

        // Useful isolation exercises are approved too. Map them to the nearest supported pattern
        // so they remain compatible with the current generator contract; their lower priority keeps
        // them behind compound movements when both are available.
        if (Has(text, "lateral raise", "elevacion lateral", "front raise", "elevacion frontal",
            "shoulder fly", "elevacion de hombro")) return "VerticalPush";
        if (Has(text, "chest fly", "pec deck", "cable fly", "dumbbell fly", "aperturas de pecho")) return "HorizontalPush";
        if (Has(text, "face pull", "reverse fly", "rear delt fly", "curl de biceps", "bicep curl",
            "preacher curl", "concentration curl", "forearm curl", "wrist curl", "curl de antebrazo",
            "pullover", "shrug", "encogimiento de hombros", "sled row", "remo con trineo",
            "straight-arm pulldown", "straight arm pulldown", "jalon con brazos rectos",
            "plate pinch", "pinza de discos", "wrist roller", "curl")) return "HorizontalPull";
        if (Has(text, "tricep extension", "triceps extension", "tricep pushdown", "skull crusher",
            "extension de triceps", "jalon de triceps")) return "HorizontalPush";
        if (Has(text, "leg extension", "quadriceps extension", "extension de cuadriceps")) return "Squat";
        if (Has(text, "leg curl", "hamstring curl", "curl femoral", "nordic hamstring")) return "Hinge";
        if (Has(text, "calf raise", "calf press", "elevacion de talones", "gemelos")) return "SingleLeg";
        if (Has(text, "hip abduction", "hip adduction", "abduccion de cadera", "aduccion de cadera",
            "glute kickback", "patada de gluteos", "cable kickback")) return "SingleLeg";

        // Do not infer a movement pattern from muscle group alone: several patterns share muscles.
        return null;
    }

    private static bool IsAccessoryExercise(string id, string? nameEs, string nameEn)
    {
        var text = Normalize(string.Join(' ', id, nameEs, nameEn));
        return Has(text, "lateral raise", "elevacion lateral", "front raise", "elevacion frontal",
            "shoulder fly", "chest fly", "pec deck", "cable fly", "dumbbell fly", "aperturas de pecho",
            "face pull", "reverse fly", "rear delt fly", "bicep curl", "curl de biceps", "preacher curl",
            "concentration curl", "forearm curl", "wrist curl", "tricep extension", "triceps extension",
            "tricep pushdown", "tricep kickback", "skull crusher", "extension de triceps", "leg extension",
            "quadriceps extension", "extension de cuadriceps", "single-leg-extension", "leg curl", "hamstring curl",
            "curl femoral", "nordic hamstring", "calf raise", "calf press", "elevacion de talones",
            "hip abduction", "hip adduction", "abduccion de cadera", "aduccion de cadera", "glute kickback",
            "cable kickback", "pullover", "hip bridge", "puente de cadera", "shrug", "encogimiento de hombros", "wrist roller", "plate pinch",
            "straight-arm pulldown", "straight arm pulldown", "jalon con brazos rectos",
            "pinza de discos", "kettlebell swing", "dumbbell swing", "farmers walk", "farmer's walk",
            "suitcase carry", "overhead carry", "sandbag carry", "ab rollout", "ab wheel", "rueda abdominal",
            "pallof press", "v-up", "v up", "flutter kick", "scissor kick");
    }

    private static string? MapMuscle(string value)
    {
        // Source identifiers are anatomical names, not free text. Use exact mappings:
        // substring matching "lat" used to classify lateral deltoid as lats, and "quad"
        // could misclassify quadratus lumborum as quadriceps.
        var v = Normalize(value).Replace('-', '_').Replace(' ', '_');
        return v switch
        {
            "pectoralis_major" or "pectoralis_minor" or "chest" or "pectorals" => "Chest",
            "latissimus_dorsi" or "lats" => "Lats",
            "rhomboids" or "trapezius" or "middle_trapezius" or "lower_trapezius"
                or "upper_trapezius" or "upper_back" or "middle_back" => "UpperBack",
            "anterior_deltoid" or "lateral_deltoid" or "posterior_deltoid" or "deltoids" => "Deltoids",
            "biceps_brachii" or "brachialis" or "brachioradialis" or "biceps" => "Biceps",
            "triceps_brachii" or "triceps" => "Triceps",
            "forearm_flexors" or "forearm_extensors" or "wrist_flexors" or "wrist_extensors" or "forearms" => "Forearms",
            "rectus_abdominis" or "transverse_abdominis" or "abdominals" or "core" => "Abdominals",
            "internal_obliques" or "external_obliques" or "obliques" => "Obliques",
            "erector_spinae" or "multifidus" or "lower_back" or "quadratus_lumborum" => "ErectorSpinae",
            "gluteus_maximus" or "gluteus_medius" or "gluteus_minimus" or "glutes" => "Glutes",
            "quadriceps" or "rectus_femoris" or "vastus_lateralis" or "vastus_medialis" or "vastus_intermedius" => "Quadriceps",
            "hamstrings" or "biceps_femoris" or "semitendinosus" or "semimembranosus" => "Hamstrings",
            "gastrocnemius" or "soleus" or "calves" => "Calves",
            "adductors" or "adductor_longus" or "adductor_magnus" or "adductor_brevis" or "gracilis" => "Adductors",
            "abductors" or "tensor_fasciae_latae" => "Abductors",
            "hip_flexors" or "iliopsoas" or "psoas_major" or "iliacus" => "HipFlexors",
            "serratus_anterior" => "Serratus",
            "tibialis_anterior" => "TibialisAnterior",
            "supraspinatus" or "infraspinatus" or "teres_minor" or "subscapularis" => "RotatorCuff",
            _ => null
        };
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
        if (Has(v, "trap bar", "trap_bar")) return ["trap-bar"];
        if (Has(v, "barbell", "barra")) return ["barbell"];
        if (Has(v, "dumbbell", "mancuerna")) return ["dumbbell"];
        if (Has(v, "kettlebell", "pesa rusa")) return ["kettlebell"];
        if (Has(v, "cable", "polea")) return ["cable"];
        if (Has(v, "flat bench", "bench", "banco")) return ["bench"];
        if (Has(v, "pull-up bar", "pull up bar", "pull_up_bar", "barra de dominadas")) return ["pull-up-bar"];
        if (Has(v, "suspension trainer", "suspension_trainer", "trx")) return ["suspension-trainer"];
        if (Has(v, "loop band", "loop_band", "resistance band", "resistance_band", "band")) return ["resistance-band"];
        if (Has(v, "rings", "gymnastic rings")) return ["rings"];
        if (Has(v, "dip station", "dip_station")) return ["dip-station"];
        if (Has(v, "leg press", "leg_press")) return ["leg-press-machine"];
        if (Has(v, "hack squat", "hack_squat")) return ["hack-squat-machine"];
        if (Has(v, "leg curl", "leg_curl")) return ["leg-curl-machine"];
        if (Has(v, "leg extension", "leg_extension")) return ["leg-extension-machine"];
        if (Has(v, "hip abduction machine", "hip_abduction_machine", "hip adduction machine", "hip_adduction_machine")) return ["machine"];
        if (Has(v, "back extension machine", "back_extension_machine", "bicep curl machine", "bicep_curl_machine",
            "chest fly machine", "chest_fly_machine", "preacher curl machine", "preacher_curl_machine",
            "reverse fly machine", "reverse_fly_machine", "ab crunch machine", "ab_crunch_machine",
            "shoulder press machine", "shoulder_press_machine", "tricep extension machine", "tricep_extension_machine",
            "standing calf raise machine", "standing_calf_raise_machine", "seated calf raise machine", "seated_calf_raise_machine",
            "plate loaded lateral raise machine", "plate_loaded_lateral_raise_machine", "hip thrust machine", "hip_thrust_machine",
            "plate loaded dip machine", "plate_loaded_dip_machine", "shrug machine", "shrug_machine",
            "chest press machine", "chest_press_machine", "leg press", "leg_press", "hack squat", "hack_squat",
            "leg curl", "leg_curl", "leg extension", "leg_extension", "pec deck", "pec_deck", "smith")) return ["machine"];
        if (Has(v, "machine", "máquina", "maquina")) return ["machine"];
        if (Has(v, "resistance band", "band", "banda elástica", "banda elastica")) return ["resistance-band"];
        if (Has(v, "smith")) return ["smith-machine"];
        if (Has(v, "ez-bar", "ez_bar", "curl bar")) return ["ez-bar"];
        if (Has(v, "medicine ball", "medicine_ball", "balón medicinal", "balon medicinal")) return ["medicine-ball"];
        if (Has(v, "stability ball", "stability_ball")) return ["stability-ball"];
        if (Has(v, "ab wheel", "ab_wheel")) return ["ab-wheel"];
        if (Has(v, "plates", "plate")) return ["weight-plate"];
        if (Has(v, "sled")) return ["sled"];
        if (Has(v, "sandbag", "sand_bag")) return ["sandbag"];
        if (Has(v, "wrist roller", "wrist_roller")) return ["wrist-roller"];
        if (Has(v, "glute ham developer", "glute_ham_developer")) return ["glute-ham-developer"];
        if (Has(v, "battle rope", "battle_rope")) return ["battle-rope"];
        if (Has(v, "treadmill", "elliptical", "ski erg", "ski_erg", "stair climber", "stair_climber", "bicycle", "stationary bike", "stationary_bike", "jump rope", "jump_rope")) return null;
        return null;
    }


    /// <summary>
    /// Creates a conservative Spanish catalogue description from available source metadata.
    /// It describes the target and safe execution intent without inventing a precise technique.
    /// </summary>
    public static string? GenerateDescriptionEs(
        string? nameEs, string nameEn, string? equipment, string? bodyPart,
        string primaryMusclesJson)
    {
        var name = string.IsNullOrWhiteSpace(nameEs) ? nameEn.Trim() : nameEs.Trim();
        if (name.Length < 3) return null;

        var muscles = ReadStrings(primaryMusclesJson)
            .Select(MapMuscle)
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(SpanishMuscleName)
            .ToArray();

        var target = muscles.Length > 0
            ? string.Join(", ", muscles)
            : SpanishBodyPart(bodyPart);
        var equipmentText = SpanishEquipment(equipment);
        var focus = string.IsNullOrWhiteSpace(target)
            ? "la musculatura implicada en el movimiento"
            : target;

        var equipmentSentence = string.IsNullOrWhiteSpace(equipmentText)
            ? ""
            : $" Utiliza {equipmentText} cuando corresponda.";

        return $"Ejercicio {name} orientado principalmente a trabajar {focus}. Realiza el movimiento de forma controlada, mantén una postura estable y utiliza un rango de movimiento cómodo.{equipmentSentence} Adapta la carga y la dificultad a tu nivel y detén el ejercicio si aparece dolor.";
    }

    /// <summary>
    /// Normalizes a source difficulty when available; otherwise estimates a level from explicit
    /// exercise-complexity cues and available mechanics. The result is an estimate, not a clinical assessment.
    /// </summary>
    public static string? InferDifficulty(
        string? sourceDifficulty, string? nameEs, string nameEn, string? equipment,
        string? mechanic, string? forceType, string? bodyPart, string tagsJson,
        string instructionsEsJson)
    {
        var supplied = sourceDifficulty?.Trim().ToLowerInvariant();
        var mapped = supplied switch
        {
            "beginner" or "easy" or "principiante" => "Beginner",
            "intermediate" or "moderate" or "intermedio" => "Intermediate",
            "advanced" or "expert" or "hard" or "avanzado" => "Advanced",
            _ => null
        };
        if (mapped is not null) return mapped;

        var text = Normalize(string.Join(' ', nameEs, nameEn, equipment, mechanic, forceType,
            bodyPart, string.Join(' ', ReadStrings(tagsJson)), string.Join(' ', ReadStrings(instructionsEsJson))));

        if (Has(text, "snatch", "clean and jerk", "muscle-up", "muscle up", "planche",
            "handstand push-up", "handstand push up", "pistol squat", "dragon flag",
            "arrancada", "dos tiempos", "muscle up", "bandera humana"))
            return "Advanced";

        if (Has(text, "deadlift", "peso muerto", "barbell squat", "sentadilla con barra",
            "bench press", "press de banca", "overhead press", "press militar",
            "pull-up", "pull up", "chin-up", "chin up", "lunge", "zancada",
            "single-leg", "a una pierna", "olympic", "olimpico", "olímpico"))
            return "Intermediate";

        if (Has(text, "machine", "máquina", "maquina", "seated", "sentado",
            "leg extension", "extension de cuadriceps", "leg curl", "curl femoral",
            "cable curl", "bicep curl", "curl de biceps", "curl de bíceps",
            "lateral raise", "elevacion lateral", "elevación lateral",
            "chest fly", "pec deck", "extension de triceps", "extensión de tríceps"))
            return "Beginner";

        // If the source supplies a compound-mechanic classification, prefer a moderate estimate.
        if (Has(Normalize(mechanic ?? ""), "compound", "multi-joint", "multi joint"))
            return "Intermediate";

        // Last-resort estimate for records without a usable source difficulty.
        return "Beginner";
    }

    private static string SpanishMuscleName(string value) => value switch
    {
        "Chest" => "pectorales",
        "Lats" => "dorsales",
        "UpperBack" => "espalda alta",
        "Deltoids" => "deltoides",
        "Biceps" => "bíceps",
        "Triceps" => "tríceps",
        "Forearms" => "antebrazos",
        "Abdominals" => "abdominales",
        "Obliques" => "oblicuos",
        "ErectorSpinae" => "erectores espinales",
        "Glutes" => "glúteos",
        "Quadriceps" => "cuádriceps",
        "Hamstrings" => "isquiotibiales",
        "Calves" => "gemelos y sóleo",
        _ => value
    };

    private static string? SpanishBodyPart(string? value)
    {
        var v = Normalize(value ?? "");
        if (Has(v, "chest", "pectoral")) return "los pectorales";
        if (Has(v, "back", "espalda")) return "la espalda";
        if (Has(v, "shoulder", "hombro")) return "los hombros";
        if (Has(v, "arm", "brazo")) return "los brazos";
        if (Has(v, "leg", "pierna")) return "las piernas";
        if (Has(v, "abdominal", "waist", "abdomen")) return "la zona abdominal";
        if (Has(v, "glute", "gluteo")) return "los glúteos";
        return null;
    }

    private static string? SpanishEquipment(string? value)
    {
        var v = Normalize(value ?? "");
        if (string.IsNullOrWhiteSpace(v) || Has(v, "bodyweight", "none", "no equipment", "peso corporal", "ninguno", "sin material"))
            return null;
        if (Has(v, "barbell")) return "una barra";
        if (Has(v, "dumbbell")) return "mancuernas";
        if (Has(v, "kettlebell")) return "una pesa rusa";
        if (Has(v, "cable")) return "una polea";
        if (Has(v, "machine")) return "una máquina";
        if (Has(v, "band")) return "una banda elástica";
        if (Has(v, "smith")) return "una máquina Smith";
        if (Has(v, "bench")) return "un banco";
        if (Has(v, "pull-up bar")) return "una barra de dominadas";
        if (Has(v, "medicine ball")) return "un balón medicinal";
        if (Has(v, "foam roller")) return "un rodillo de espuma";
        return value?.Trim();
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
