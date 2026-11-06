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

        // RepDB difficulty is not a direct minimum-experience requirement. These controlled,
        // scalable variants are appropriate beginner options when load and range are adapted.
        // Keep technically demanding barbell deadlifts and ab-wheel rollouts intermediate.
        if (id is "dumbbell-romanian-deadlift" or "hip-thrust" or "barbell-glute-bridge"
            or "dumbbell-shoulder-press" or "machine-shoulder-press"
            or "cable-crunch" or "cable-pallof-press" or "side-plank"
            or "dumbbell-farmers-walk" or "reverse-lunge" or "step-ups"
            // Stable, supported rowing and assisted-pull-up variants scale well for novices.
            or "chest-supported-db-row" or "single-arm-db-row" or "assisted-pull-ups")
            exp = "Beginner";

        if (exp is null) return null;

        var goals = ReadStrings(goalsJson).Select(MapGoal).Where(x => x is not null).Cast<string>()
            // RepDB goal tags are narrower than the app's broad training goals. A resistance
            // exercise suitable for strength or hypertrophy is also usable in general fitness;
            // minimum experience and equipment still constrain who can receive it.
            .Append("GeneralFitness")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var requiredEquipment = MapEquipment(equipment ?? "", isBodyweight);
        if (requiredEquipment is null) return null;
        // Some RepDB exercises declare extra mandatory equipment in tags rather than equipment.
        // A tibialis raise needs a bench to support the working leg as well as the kettlebell.
        if (Has(tagsJson, "requires_bench", "requires bench"))
            requiredEquipment = requiredEquipment.Append("bench")
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // Accessory work is selected only after the main patterns and only when it addresses
        // an unmet muscle target; priority breaks ties between compatible accessories.
        var selectionPriority = GetSelectionPriority(id, nameEs, nameEn, pattern);



        return new ReviewMetadata(pattern, primary, secondary, exp, goals, requiredEquipment,
            selectionPriority,
            "Enriquecimiento automático RepDB v4: mapeo determinista de metadatos de origen; verificar si cambian las reglas.");
    }

    private static string? InferPattern(string id, string es, string en, string tagsJson,
        string? force, string? mechanic, string? bodyPart, string[] muscles)
    {
        var text = Normalize(string.Join(' ', id, es, en, string.Join(' ', ReadStrings(tagsJson))));
        // Stable RepDB identifiers take precedence over translated display names. This prevents
        // ambiguous substrings (for example "row" inside "crow") from deciding biomechanics.
        var explicitPattern = id.ToLowerInvariant() switch
        {
            "ohp" or "dumbbell-shoulder-press" or "machine-shoulder-press" => "VerticalPush",
            "cable-crunch" or "ab-wheel-rollout" or "cable-pallof-press" or "side-plank" or "dumbbell-farmers-walk"
                or "machine-seated-crunch" or "dead-bug" or "hanging-knee-raise" or "captains-chair-knee-raise" => "Core",
            "machine-back-extension" or "glute-bridge" => "Hinge",
            "db-squat" or "horizontal-leg-press" => "Squat",
            "chest-supported-kettlebell-row" or "kneeling-cable-row" or "one-arm-seated-cable-row" => "HorizontalPull",
            "lat-pulldown" or "v-bar-lat-pulldown" or "pull-up" or "chin-ups" or "assisted-pull-ups" => "VerticalPull",
            "bench-press" or "db-bench-press" or "incline-bench-press" or "incline-db-press"
                or "smith-machine-bench-press" or "smith-machine-incline-bench-press"
                or "chest-press-machine" or "push-up" or "knee-push-ups" or "incline-push-ups" => "HorizontalPush",
            "barbell-row" or "single-arm-db-row" or "chest-supported-db-row"
                or "seated-cable-row" or "chest-supported-machine-row" or "t-bar-row" => "HorizontalPull",
            "squat" or "front-squat" or "goblet-squat" or "leg-press" or "hack-squat" => "Squat",
            "deadlift" or "hex-bar-deadlift" or "romanian-deadlift"
                or "dumbbell-romanian-deadlift" or "hip-thrust" or "plate-loaded-glute-drive" => "Hinge",
            "bulgarian-split-squat" or "reverse-lunge" or "step-ups" => "SingleLeg",
            // Knee-flexion hamstring work is isolation, not a hip hinge. Keep these exercises
            // available to the accessory selector instead of letting deadlift variants displace them.
            "leg-curl" or "seated-leg-curl" or "nordic-hamstring-curl"
                or "ball-leg-curl" or "banded-standing-curl" or "trx-hamstring-curl"
                or "reverse-nordic-curl" => "Accessory",
            _ => (string?)null
        };
        if (explicitPattern is not null) return explicitPattern;
        // Isolations/accessories have their own optional generator slots; they must not
        // displace the primary compound movement chosen for a broad movement pattern.
        if (id is "machine-reverse-fly" or "machine-chest-fly" or "machine-bicep-curl"
            or "single-arm-plate-loaded-lateral-raise" or "machine-calf-raise"
            or "side-lying-hip-abduction" or "side-lying-hip-adduction" or "bodyweight-calf-raise")
            return "Accessory";
        if (IsAccessoryExercise(id, es, en)) return "Accessory";
        // Strong name/ID cues first; ambiguous exercises stay pending.
        if (Has(text, "overhead press", "shoulder press", "military press", "press militar", "arnold press", "landmine press", "push press",
            "pike push ups", "pike push-up", "flexiones en pica", "dips", "fondos")) return "VerticalPush";
        if (Has(text, "bulgarian split squat", "split squat", "pistol squat", "cossack squat", "sentadilla bulgara", "sentadilla en tijera", "sentadilla cosaca",
            "lunge", "zancada", "step-up", "step up", "single-leg-press")) return "SingleLeg";
        if (Has(text, "squat", "sentadilla", "leg press", "prensa de piernas", "reverse nordic")) return "Squat";
        if (Has(text, "deadlift", "peso muerto", "good morning", "hip thrust", "puente de gluteos", "glute bridge", "hip bridge", "puente de cadera", "back extension", "extension lumbar",
            "kettlebell swing", "dumbbell swing", "one-arm swing", "swing con mancuerna", "pull-through", "pull through",
            "leg curl", "hamstring curl", "curl femoral", "nordic hamstring", "standing leg curl", "curl de pierna de pie")) return "Hinge";
        if (Has(text, "bench press", "press de banca", "push-up", "push up", "flexion de brazos", "flexiones de brazos", "chest press", "floor press",
            "svend press", "tricep kickback", "patada de triceps",
            "dip machine", "dips", "fondos")) return "HorizontalPush";
        if (HasWholeWord(text, "row") || Has(text, "remo", "seated cable row", "bent-over row")) return "HorizontalPull";
        if (Has(text, "pull-up", "pull up", "chin-up", "chin up", "lat pulldown", "jalon al pecho")) return "VerticalPull";
        if (Has(text, "lunge", "zancada", "split squat", "step-up", "step up", "pistol squat", "single-leg")) return "SingleLeg";
        if (Has(text, "crunch", "sit-up", "sit up", "plank", "plancha", "dead bug", "hollow hold", "russian twist", "abdominal",
            "leg raise", "knee raise", "knee tuck", "ball pike", "pike con pelota", "flutter kick", "scissor kick", "windshield wiper", "pallof press", "farmers walk",
            "farmer's walk", "suitcase carry", "overhead carry", "bench leg pull-in", "bench leg pull in",
            "ab rollout", "ab wheel", "rueda abdominal", "v-up", "v up", "tuck hold")) return "Core";

        // Ambiguous exercises stay pending rather than being forced into a compound pattern.

        // Do not infer a movement pattern from muscle group alone: several patterns share muscles.
        return null;
    }

    internal static int GetSelectionPriority(string id, string? nameEs, string nameEn, string? movementPattern)
    {
        var text = Normalize(string.Join(' ', id, nameEs, nameEn));

        if (string.Equals(movementPattern, "Accessory", StringComparison.Ordinal))
        {
            // Prefer loadable isolation work; bodyweight/band variants remain valid options
            // but should not win a tie over scalable machine, cable, barbell or dumbbell work.
            if (Has(text, "bodyweight", "banded", "resistance band", "trx", "ring")) return 42;
            if (Has(text, "machine", "cable", "barbell", "dumbbell", "mancuerna", "polea", "maquina")) return 72;
            return 58;
        }

        // Explicit product curation: stable, measurable basics win over assisted or lightly
        // loadable variants when both satisfy the same pattern, goal, experience and equipment.
        if (Has(text, "bodyweight", "banded", "resistance band", "trx", "ring", "assisted")) return 62;
        if (Has(text, "bench press", "press de banca", "lat pulldown", "jalon al pecho",
            "seated cable row", "remo sentado en polea", "leg press", "prensa de piernas",
            "hack squat", "goblet squat", "sentadilla goblet", "romanian deadlift",
            "peso muerto rumano", "deadlift", "peso muerto", "hip thrust",
            "machine shoulder press", "press de hombros en maquina")) return 96;

        return movementPattern is "Squat" or "Hinge" or "HorizontalPush" or "HorizontalPull" ? 85 : 75;
    }

    private static bool IsAccessoryExercise(string id, string? nameEs, string nameEn)
    {
        var text = Normalize(string.Join(' ', id, nameEs, nameEn));
        return Has(text, "lateral raise", "elevacion lateral", "front raise", "elevacion frontal",
            "shoulder fly", "chest fly", "pec deck", "cable fly", "dumbbell fly", "aperturas de pecho",
            "face pull", "reverse fly", "rear delt fly", "bicep curl", "curl de biceps", "hammer curl", "curl de martillo", "preacher curl",
            "barbell curl", "cable curl", "dumbbell curl", "incline db curl",
            "curl con barra", "curl con cable", "curl con mancuernas",
            "concentration curl", "forearm curl", "wrist curl", "tricep extension", "triceps extension",
            "tricep pushdown", "tricep kickback", "skull crusher", "extension de triceps", "leg extension",
            "quadriceps extension", "extension de cuadriceps", "single-leg-extension", "leg curl", "hamstring curl",
            "curl femoral", "nordic hamstring", "standing leg curl", "curl de pierna de pie", "banded-standing-curl",
            "calf raise", "calf press", "elevacion de talones",
            "hip abduction", "hip adduction", "abduccion de cadera", "aduccion de cadera", "glute kickback",
            "cable kickback", "pullover", "hip bridge", "puente de cadera", "shrug", "encogimiento de hombros", "wrist roller", "plate pinch",
            "straight-arm pulldown", "straight arm pulldown", "jalon con brazos rectos",
            "pinza de discos", "tibialis raise", "elevacion de tibial", "external rotation", "rotacion externa");
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
        if (Has(v, "smith")) return ["smith-machine"];
        if (Has(v, "hip abduction machine", "hip_abduction_machine", "hip adduction machine", "hip_adduction_machine")) return ["machine"];
        if (Has(v, "back extension machine", "back_extension_machine", "bicep curl machine", "bicep_curl_machine",
            "chest fly machine", "chest_fly_machine", "preacher curl machine", "preacher_curl_machine",
            "reverse fly machine", "reverse_fly_machine", "ab crunch machine", "ab_crunch_machine",
            "shoulder press machine", "shoulder_press_machine", "tricep extension machine", "tricep_extension_machine",
            "standing calf raise machine", "standing_calf_raise_machine", "seated calf raise machine", "seated_calf_raise_machine",
            "plate loaded lateral raise machine", "plate_loaded_lateral_raise_machine", "hip thrust machine", "hip_thrust_machine",
            "plate loaded dip machine", "plate_loaded_dip_machine", "shrug machine", "shrug_machine",
            "chest press machine", "chest_press_machine", "leg press", "leg_press", "hack squat", "hack_squat",
            "leg curl", "leg_curl", "leg extension", "leg_extension", "pec deck", "pec_deck")) return ["machine"];
        if (Has(v, "machine", "máquina", "maquina")) return ["machine"];
        if (Has(v, "resistance band", "band", "banda elástica", "banda elastica")) return ["resistance-band"];
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

    public static string GenerateInstructionsEs(string id, string? nameEs, string nameEn)
    {
        var key = id.ToLowerInvariant();
        string[]? instructions = key switch
        {
            "push-up" or "knee-push-ups" =>
                ["Coloca las manos algo más abiertas que los hombros y mantén el cuerpo alineado desde la cabeza hasta las rodillas o los pies.",
                 "Flexiona los codos para acercar el pecho al suelo y empuja después hasta recuperar la posición inicial, sin perder la alineación."],
            "incline-push-ups" =>
                ["Apoya las manos sobre un banco o superficie firme y elevada, a la anchura de los hombros.",
                 "Mantén el cuerpo alineado, baja el pecho hacia el apoyo y empuja hasta extender los brazos sin bloquear los codos."],
            "bench-press" or "db-bench-press" or "incline-bench-press" or "incline-db-press"
                or "smith-machine-bench-press" or "smith-machine-incline-bench-press"
                or "chest-press-machine" =>
                ["Apoya firmemente los pies y la espalda en el banco o respaldo; coloca las manos a una anchura cómoda y controla la posición de los hombros.",
                 "Desciende la carga hacia el pecho de forma controlada y empuja hasta extender los brazos sin bloquear bruscamente los codos."],
            "barbell-row" or "single-arm-db-row" or "chest-supported-db-row"
                or "seated-cable-row" or "chest-supported-machine-row" or "t-bar-row" =>
                ["Adopta una posición estable, mantén la columna neutra y deja que los brazos se extiendan sin perder el control del tronco.",
                 "Lleva los codos hacia atrás acercando la carga al torso y vuelve lentamente a la posición inicial."],
            "ohp" or "dumbbell-shoulder-press" or "machine-shoulder-press" =>
                ["Coloca la carga a la altura de los hombros, con el tronco firme y las muñecas alineadas con los antebrazos.",
                 "Empuja verticalmente hasta extender los brazos de forma controlada y baja de nuevo hasta la posición inicial."],
            "lat-pulldown" or "v-bar-lat-pulldown" or "assisted-pull-ups" or "pull-up" or "chin-ups" =>
                ["Sujeta la barra con el agarre indicado y estabiliza el tronco, evitando impulsarte o balancearte.",
                 "Lleva los codos hacia abajo hasta acercar la barra o el cuerpo al punto previsto y regresa lentamente con control."],
            "squat" or "front-squat" or "goblet-squat" or "leg-press" or "hack-squat" =>
                ["Coloca los pies en una posición estable y ajusta la carga para poder mantener el recorrido con control.",
                 "Flexiona caderas y rodillas siguiendo la dirección de los pies; empuja el suelo para volver a la posición inicial."],
            "bulgarian-split-squat" or "reverse-lunge" or "step-ups" =>
                ["Adopta una posición unilateral estable y reparte el apoyo sin perder el equilibrio ni la alineación de la rodilla.",
                 "Desciende o sube de forma controlada, impulsándote con la pierna de trabajo y evitando rebotes."],
            "deadlift" or "hex-bar-deadlift" or "romanian-deadlift" or "dumbbell-romanian-deadlift"
                or "hip-thrust" or "barbell-glute-bridge" =>
                ["Coloca los pies y la carga de forma estable; mantén el abdomen activo y la columna en posición neutra.",
                 "Extiende la cadera de forma controlada sin hiperextender la zona lumbar y vuelve lentamente al inicio."],
            "cable-fly" or "pec-deck" =>
                ["Ajusta la polea o máquina a una posición cómoda y mantén una ligera flexión de los codos.",
                 "Acerca los brazos delante del pecho sin encoger los hombros y regresa lentamente hasta notar un estiramiento cómodo."],
            "lateral-raise" or "cable-lateral-raise" or "seated-dumbbell-lateral-raise" =>
                ["Mantén el tronco estable y los codos ligeramente flexionados, con la carga a los lados del cuerpo.",
                 "Eleva los brazos hacia los lados hasta una altura cómoda y desciende lentamente sin balancear el cuerpo."],
            "dumbbell-reverse-fly" or "face-pull" =>
                ["Adopta una posición estable y dirige el movimiento con los codos, manteniendo el cuello relajado.",
                 "Separa las manos llevando los codos hacia atrás y vuelve lentamente sin utilizar impulso."],
            "barbell-curl" or "hammer-curl" or "incline-db-curl" or "preacher-curl" or "cable-curl" =>
                ["Mantén los codos cerca del cuerpo o apoyados según la variante, evitando balancear el tronco.",
                 "Flexiona los codos para elevar la carga y bájala lentamente hasta casi extender los brazos."],
            "tricep-pushdown" or "cable-overhead-tricep-extension" or "overhead-tricep-extension" or "skull-crusher" =>
                ["Estabiliza los hombros y mantén los codos en una posición fija durante el movimiento.",
                 "Extiende los codos para mover la carga y regresa lentamente sin abrirlos ni utilizar impulso."],
            "leg-extension" =>
                ["Ajusta el respaldo y el rodillo para que la rodilla quede alineada con el eje de la máquina.",
                 "Extiende las rodillas de forma controlada y baja la carga lentamente sin dejarla caer."],
            "reverse-nordic-curl" =>
                ["Arrodíllate sobre una almohadilla con las rodillas a la anchura de las caderas y el torso erguido.",
                 "Mantén la cadera extendida y el abdomen activo durante todo el movimiento.",
                 "Inclina el torso hacia atrás lentamente manteniendo hombros, caderas y rodillas alineados.",
                 "Desciende solo hasta donde puedas controlar la tensión de los cuádriceps.",
                 "Vuelve a la posición inicial sin doblar la cadera ni impulsarte."],
            "leg-curl" or "seated-leg-curl" or "nordic-hamstring-curl" =>
                ["Ajusta la máquina o la posición para mantener la cadera estable durante todo el recorrido.",
                 "Flexiona las rodillas acercando los talones y regresa lentamente, controlando especialmente la fase de bajada."],
            "ball-leg-curl" =>
                ["Túmbate boca arriba con los talones y la parte inferior de las pantorrillas apoyados sobre una pelota de estabilidad.",
                 "Eleva las caderas formando una línea recta desde los hombros hasta los tobillos.",
                 "Mantén las caderas elevadas y flexiona las rodillas para rodar la pelota hacia los glúteos.",
                 "Extiende las piernas lentamente sin dejar caer la pelvis.",
                 "Baja las caderas al terminar."],
            "banded-standing-curl" =>
                ["Coloca una banda elástica alrededor de un tobillo y ancla el otro extremo bajo el pie de apoyo o en un punto fijo delante de ti.",
                 "Mantente erguido y sujétate a un apoyo si necesitas equilibrio.",
                 "Flexiona la rodilla llevando el talón hacia el glúteo y mantén el muslo quieto.",
                 "Contrae el isquiotibial arriba y baja con control hasta extender la rodilla.",
                 "Completa las repeticiones de un lado y cambia de pierna."],
            "trx-hamstring-curl" =>
                ["Túmbate boca arriba con los talones en los soportes del entrenador de suspensión y las rodillas flexionadas.",
                 "Eleva las caderas manteniendo el tronco alineado.",
                 "Extiende las piernas hacia delante sin dejar caer las caderas.",
                 "Flexiona las rodillas y acerca los talones hacia los glúteos.",
                 "Controla la extensión y evita que las caderas se hundan."],
            "standing-calf-raise" or "seated-calf-raise" =>
                ["Apoya el antepié sobre la plataforma y comienza desde una posición estable, sin rebotes.",
                 "Eleva los talones todo lo posible de forma controlada y baja lentamente hasta un estiramiento cómodo."],
            "cable-external-rotation" =>
                ["Coloca el codo junto al costado y mantenlo flexionado aproximadamente a 90 grados.",
                 "Gira el antebrazo hacia fuera sin separar el codo del cuerpo y vuelve lentamente al inicio."],
            "hip-adduction" =>
                ["Ajusta la máquina para que las almohadillas contacten con la cara interna de los muslos.",
                 "Acerca las piernas de forma controlada y vuelve lentamente sin dejar que la carga golpee."],
            "standing-hip-abduction" =>
                ["Mantén el tronco erguido y sujétate para estabilizar la pelvis.",
                 "Separa una pierna lateralmente sin inclinar el tronco y regresa lentamente a la posición inicial."],
            "hip-abduction" =>
                ["Siéntate en la máquina con los apoyos contra la parte externa de los muslos y la espalda apoyada.",
                 "Separa las piernas contra la resistencia, mantén un instante y vuelve lentamente a la posición inicial."],
            "plate-loaded-glute-drive" =>
                ["Siéntate en la máquina con la parte alta de la espalda apoyada y los pies firmes en la plataforma.",
                 "Coloca la almohadilla sobre el pliegue de la cadera y ajusta la posición antes de iniciar.",
                 "Empuja las caderas hacia arriba contrayendo los glúteos y baja lentamente sin perder el control."],
            "shrug" =>
                ["De pie, sujeta la barra con los brazos extendidos y el tronco estable.",
                 "Eleva los hombros hacia las orejas sin flexionar los codos y mantén brevemente.",
                 "Desciende los hombros de forma controlada sin dejar caer la carga."],
            "straight-arm-pulldown" =>
                ["Colócate frente a la polea alta y sujeta la barra con los brazos casi extendidos.",
                 "Lleva la barra hacia los muslos mediante los hombros, manteniendo el tronco estable.",
                 "Regresa lentamente a la posición inicial sin balancearte."],
            "cable-crunch" =>
                ["Arrodíllate frente a la polea y sujeta la cuerda junto a la cabeza, manteniendo la cadera estable.",
                 "Flexiona el tronco acercando las costillas a la pelvis y vuelve lentamente sin tirar con los brazos."],
            "ab-wheel-rollout" =>
                ["Comienza de rodillas con la rueda bajo los hombros y el abdomen activo para evitar arquear la zona lumbar.",
                 "Desplaza la rueda hacia delante hasta donde puedas mantener el tronco estable y regresa controladamente."],
            "cable-pallof-press" =>
                ["Colócate de lado respecto a la polea y sujeta el agarre delante del pecho con los pies firmes.",
                 "Extiende los brazos sin permitir que el tronco rote y vuelve lentamente al pecho."],
            "side-plank" =>
                ["Apoya el antebrazo bajo el hombro y eleva la pelvis hasta alinear hombros, cadera y tobillos.",
                 "Mantén la posición respirando con normalidad y evita que la pelvis descienda o rote."],
            "dumbbell-farmers-walk" =>
                ["Sujeta una carga en cada mano y adopta una postura erguida con los hombros relajados.",
                 "Camina con pasos cortos y controlados, manteniendo el tronco estable y evitando balancear las cargas."],
            "machine-seated-crunch" =>
                ["Ajusta el asiento y coloca el apoyo de forma cómoda, manteniendo la pelvis estable.",
                 "Flexiona el tronco acercando las costillas a la pelvis y regresa lentamente sin tirar con los brazos."],
            "dead-bug" =>
                ["Túmbate boca arriba con caderas y rodillas flexionadas y la zona lumbar estable.",
                 "Extiende lentamente el brazo y la pierna contrarios sin arquear la espalda y vuelve al centro antes de alternar."],
            "machine-reverse-fly" =>
                ["Ajusta el asiento para que los brazos queden a la altura de los hombros y sujeta los agarres con control.",
                 "Abre los brazos llevando los codos hacia atrás y vuelve lentamente sin encoger los hombros."],
            "machine-chest-fly" =>
                ["Ajusta el respaldo y los brazos de la máquina para comenzar con un estiramiento cómodo del pecho.",
                 "Junta los brazos delante del torso sin golpear los soportes y vuelve lentamente a la posición inicial."],
            "machine-bicep-curl" =>
                ["Ajusta el asiento para alinear los codos con el eje de giro y apoya los brazos en el soporte.",
                 "Flexiona los codos para elevar el agarre y baja la carga lentamente sin despegar los brazos."],
            "single-arm-plate-loaded-lateral-raise" =>
                ["Colócate de lado respecto al apoyo y ajusta el brazo de la máquina a una posición cómoda.",
                 "Eleva el brazo lateralmente sin inclinar el tronco y desciende lentamente manteniendo el control."],
            "machine-calf-raise" =>
                ["Apoya el antepié en la plataforma y coloca los hombros bajo los soportes acolchados.",
                 "Eleva los talones de forma controlada y baja lentamente hasta un estiramiento cómodo."],
            "kettlebell-tibialis-raise" =>
                ["Siéntate en el extremo de un banco con una pierna extendida y el talón apoyado en el suelo.",
                 "Cuelga una pesa rusa por el asa del antepié, sobre los dedos, manteniendo la pierna estable.",
                 "Eleva los dedos hacia la espinilla sin mover la rodilla ni el muslo.",
                 "Baja lentamente el pie y cambia de pierna al completar las repeticiones."],
            "bodyweight-calf-raise" =>
                ["Colócate de pie con apoyo estable y deja que los talones puedan descender libremente.",
                 "Eleva los talones todo lo posible y baja lentamente sin rebotar."],
            "side-lying-hip-abduction" =>
                ["Túmbate de lado con la pierna inferior flexionada y la superior extendida, manteniendo la pelvis estable.",
                 "Eleva la pierna superior sin girar la cadera y bájala lentamente."],
            "side-lying-hip-adduction" =>
                ["Túmbate de lado y cruza la pierna superior por delante para dejar libre la pierna inferior.",
                 "Eleva la pierna inferior hacia la superior sin rotar la pelvis y desciende lentamente."],
            "machine-back-extension" =>
                ["Ajusta la máquina para que el eje de giro quede alineado con la cadera y mantén el tronco estable.",
                 "Extiende la cadera hasta alinear el tronco con las piernas sin hiperextender la zona lumbar y vuelve controladamente."],
            "glute-bridge" =>
                ["Túmbate boca arriba con las rodillas flexionadas y los pies apoyados a una distancia cómoda.",
                 "Eleva la pelvis contrayendo los glúteos sin arquear la zona lumbar y desciende lentamente."],
            "db-squat" or "horizontal-leg-press" =>
                ["Coloca los pies en una posición estable y ajusta la carga para mantener el recorrido con control.",
                 "Flexiona caderas y rodillas siguiendo la dirección de los pies y empuja para volver al inicio."],
            "chest-supported-kettlebell-row" or "kneeling-cable-row" or "one-arm-seated-cable-row" =>
                ["Adopta una posición estable y mantén el tronco y la columna neutros durante el movimiento.",
                 "Lleva el codo hacia atrás acercando la carga al torso y regresa lentamente sin girar el tronco."],
            "hanging-knee-raise" or "captains-chair-knee-raise" =>
                ["Estabiliza el tronco y sujeta los apoyos con los hombros controlados, evitando balancearte.",
                 "Eleva las rodillas hacia el pecho mediante la flexión de cadera y vuelve lentamente al inicio."],
            _ => null
        };

        return instructions is null ? "[]" : JsonSerializer.Serialize(instructions);
    }

    public static bool HasUsefulInstructions(string json)
    {
        var instructions = ReadStrings(json);
        // RepDB commonly ends a valid sequence with the short instruction "Repite.".
        // Require at least two substantive steps instead of rejecting the whole sequence.
        return instructions.Length >= 2 && instructions.Count(x => x.Trim().Length >= 8) >= 2;
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

    private static bool HasWholeWord(string text, string term)
    {
        var normalized = " " + Normalize(text).Replace('-', ' ').Replace('_', ' ') + " ";
        return normalized.Contains(" " + Normalize(term) + " ", StringComparison.OrdinalIgnoreCase);
    }
}
