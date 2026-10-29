using System.Text.Json;

namespace Anguloso.Server.Logica;

/// <summary>
/// Small, explicit curation layer for source records that should not be trusted blindly.
/// Rules are intentionally auditable; uncertain candidates stay in the human review queue.
/// </summary>
internal static class RepDbExerciseCuration
{
    // Curated against the actual RepDB records inspected for DietoExpress. These are
    // product-scope decisions: prefer scalable, loadable movements for general strength and
    // hypertrophy plans; avoid highly technical Olympic skills and redundant low-value variants.
    public static readonly string[] ExcludedIds =
    [
        // Specialist skills with little transfer to general strength/hypertrophy plans.
        "back-lever",
        "ring-dead-hang",
        // Explicit low-value/redundant variants; this is not a blanket exclusion of isolation work.
        "cheat-curl",
        "hammer-curl",
        "cable-hammer-curl",
        "band-assisted-pull-ups",
        "crunches",
        "balance-dome-crunch",
        "cross-body-crunch",
        "bicycle-crunch",
        "decline-crunch",
        "weighted-wall-crunch",
        "cocoons",
        // Rehab/activation drills that do not belong in the general automatic routine generator.
        "side-lying-hip-abduction-hold",
        "side-lying-hip-adduction-hold",
        "banded-seated-hip-abduction",
        "banded-standing-hip-adduction",
        "clamshells",
        "clamshells-hold",
        "banded-clamshell",
        "banded-fire-hydrant",
        "banded-lateral-walk",
        "banded-terminal-knee-extension",
        "tandem-stance",
        "heel-to-toe-walk",
        "cable-external-rotation",
        // Yoga/Pilates-style positions and balance skills are outside the strength routine generator.
        "boat-pose",
        "chair-pose",
        "dolphin-pose",
        "eagle-pose",
        "half-moon-pose",
        "locust-pose",
        "tree-pose",
        "bow-pose",
        "dancer-pose",
        "revolved-chair-pose",
        "pilates-kneeling-side-kick",
        "pilates-leg-pull-back",
        "pilates-leg-pull-front",
        "pilates-roll-over",
        "banded-sumo-walk",
        // Advanced calisthenics skills, balance work and low-transfer conditioning drills.
        "handstand-hold",
        "muscle-ups",
        "front-lever",
        "human-flag",
        "l-sit",
        "ring-l-sit",
        "hanging-l-sit",
        "hanging-pike",
        "flexed-arm-hang",
        "ring-support-hold",
        "rope-climb",
        "downward-dog-to-upward-dog",
        "warrior-two",
        "warrior-three",
        "three-legged-dog",
        "supported-shoulderstand",
        "inchworm",
        "fire-hydrant",
        "bird-dog",
        "chin-tuck-hold",
        "glute-kickback-hold",
        "six-inch-hold",
        "jefferson-curl",
        "bear-crawl",
        "sled-push",
        "wall-ball",
        "medicine-ball-slam",
        "medicine-ball-side-throw",
        "jackknife-sit-up",
        "band-pull-apart",
        "v-sit",
        "thoracic-bridge",
        "superman",
        "double-kettlebell-clean",
        // Highly technical Olympic lifts.
        "snatch",
        "hang-snatch",
        "hang-clean",
        "clean",
        "clean-and-jerk",
        "dumbbell-clean-and-press"
    ];

    private static readonly HashSet<string> Excluded = new(ExcludedIds, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ExcludedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "stretching", "yoga", "mobility", "pilates", "balance", "warmup", "warm_up", "warm-up", "warm up",
        "rehabilitation", "rehab", "prehab", "cardio"
    };

    // Approval is based on complete, interpretable source metadata and supported generator mappings,
    // not a hard-coded ID allowlist. This lets future high-quality RepDB exercises be enriched safely.
    public static bool IsAutoApprovalCandidate(string id) => !ShouldExclude(id) && !RequiresManualReview(id);

    // For very large source variation groups, retain useful, scalable options across common
    // equipment/access levels and prune near-identical variants. Unlisted groups are not filtered.
    // Empty lists intentionally remove specialist groups that do not fit general strength/hypertrophy plans.
    public static readonly Dictionary<string, string[]> PreferredIdsByVariationGroup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["squat"] = ["barbell-box-squat", "bodyweight-squat", "box-squat", "bulgarian-split-squat", "db-squat", "front-squat", "goblet-squat", "hack-squat", "heel-elevated-squat", "smith-machine-squat", "squat", "sumo-squat", "zercher-squat"],
        ["bicep-curl"] = ["banded-standing-curl", "barbell-curl", "barbell-preacher-curl", "bicep-curl", "cable-curl", "cable-hammer-curl", "concentration-curl", "ez-bar-curl", "hammer-curl", "incline-db-curl", "machine-bicep-curl", "preacher-curl", "trx-bicep-curl", "reverse-curl", "zottman-curl"],
        ["row"] = ["barbell-row", "bent-over-db-row", "chest-supported-db-row", "chest-supported-kettlebell-row", "chest-supported-machine-row", "inverted-row", "one-arm-kettlebell-row", "one-arm-seated-cable-row", "seated-cable-row", "single-arm-db-row", "t-bar-row", "trx-row"],
        ["bench-press"] = ["bench-press", "cable-chest-press", "chest-press-machine", "close-grip-bench-press", "db-bench-press", "decline-bench-press", "dumbbell-floor-press", "incline-bench-press", "incline-db-press", "smith-machine-bench-press", "smith-machine-incline-bench-press", "trx-chest-press"],
        ["deadlift"] = ["banded-romanian-deadlift", "bodyweight-good-morning", "cable-pull-through", "deadlift", "dumbbell-deadlift", "dumbbell-romanian-deadlift", "good-morning", "hex-bar-deadlift", "kettlebell-deadlift", "romanian-deadlift", "single-leg-romanian-deadlift", "sumo-deadlift"],
        ["overhead-press"] = ["arnold-press", "double-kettlebell-overhead-press", "dumbbell-shoulder-press", "machine-shoulder-press", "ohp", "one-arm-dumbbell-shoulder-press", "one-arm-kettlebell-shoulder-press", "seated-barbell-overhead-press", "seated-db-press", "smith-machine-shoulder-press"],
        ["plank"] = ["copenhagen-plank", "copenhagen-plank-raises", "high-plank", "incline-plank-hold", "plank", "plank-knee-tuck", "reverse-plank", "side-plank", "side-plank-leg-lift", "trx-plank", "trx-side-plank"],
        ["push-up"] = ["archer-push-ups", "close-grip-push-ups", "decline-push-up", "diamond-push-ups", "incline-push-ups", "knee-push-ups", "pike-push-ups", "push-up", "ring-push-up", "wide-grip-push-ups"],
        ["pull-up"] = ["assisted-pull-ups", "band-assisted-pull-ups", "chin-ups", "close-grip-pull-ups", "lat-pulldown", "negative-pull-ups", "neutral-grip-pull-ups", "pull-up", "scapular-pull-ups", "weighted-pull-up", "wide-grip-pull-ups"],
        ["lunge"] = ["barbell-lunge", "barbell-reverse-lunge", "bodyweight-reverse-lunge", "db-lunge", "kettlebell-goblet-lunge", "kettlebell-reverse-lunge", "lunge", "reverse-lunge", "side-lunge", "split-squat", "walking-lunge", "smith-machine-reverse-lunge"],
        ["hip-thrust"] = ["barbell-glute-bridge", "banded-hip-thrust", "dumbbell-hip-thrust", "glute-bridge", "hip-thrust", "plate-loaded-glute-drive", "single-leg-glute-bridge", "single-leg-hip-thrust", "smith-machine-hip-thrust"],
        ["crunch"] = ["cable-crunch", "crunches", "dead-bug", "machine-seated-crunch", "reverse-crunches", "russian-twist", "sit-ups"],
        ["calf-raise"] = ["bodyweight-calf-raise", "barbell-calf-raise", "donkey-calf-raise", "dumbbell-calf-raise", "hack-squat-calf-raise", "machine-calf-raise", "seated-calf-raise", "single-leg-calf-raise", "standing-calf-raise"],
        ["hip-abduction"] = ["banded-clamshell", "banded-fire-hydrant", "banded-standing-hip-abduction", "fire-hydrant", "hip-abduction", "side-lying-hip-abduction", "standing-hip-abduction"],
        ["dips"] = ["assisted-dips", "dips", "plate-loaded-seated-dip-machine", "ring-dips", "seated-dip-machine", "straight-bar-dips", "weighted-dips"],
        ["overhead-tricep-extension"] = ["cable-overhead-tricep-extension", "dumbbell-tricep-extension", "ez-bar-overhead-extension", "overhead-tricep-extension", "seated-dumbbell-tricep-extension", "single-arm-dumbbell-overhead-tricep-extension", "trx-tricep-extension"],
        ["pullover"] = ["barbell-pullover", "db-pullover", "kettlebell-pullover", "straight-arm-pulldown"],
        ["reverse-fly"] = ["cable-reverse-fly", "dumbbell-reverse-fly", "face-pull", "machine-reverse-fly", "rear-delt-fly", "trx-face-pull"],
        ["shrug"] = ["chest-supported-dumbbell-shrug", "db-shrug", "plate-loaded-shrug", "shrug", "smith-machine-shrug"],
        ["back-extension"] = ["back-extension", "bird-dog", "machine-back-extension", "superman"],
        ["leg-curl"] = ["ball-leg-curl", "leg-curl", "nordic-hamstring-curl", "seated-leg-curl", "single-leg-lying-leg-curl", "trx-hamstring-curl"],
        ["chest-fly"] = ["cable-fly", "db-fly", "incline-dumbbell-fly", "machine-chest-fly", "pec-deck"],
        ["leg-press"] = ["close-stance-leg-press", "horizontal-leg-press", "leg-press", "single-leg-press"],
        ["step-up"] = ["step-ups"],
        ["front-raise"] = ["cable-front-raise"],
        ["upright-row"] = [],
        ["skull-crusher"] = ["db-skull-crusher", "ez-bar-lying-tricep-extension", "skull-crusher"],
        ["clean"] = [],
        ["snatch"] = []
    };

    private static readonly HashSet<string> RequireManualReviewIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "dead-hang",
        "sit-ups"
    };

    public static bool ShouldExclude(string id) => Excluded.Contains(id);

    public static bool ShouldExclude(string id, string? category, string? equipment, string? variationGroup)
    {
        if (ShouldExclude(id)) return true;

        // The automatic generator builds progressive strength/hypertrophy routines, not warm-ups,
        // mobility sessions, yoga flows, rehabilitation plans, or conditioning-only sessions.
        if (category is not null && ExcludedCategories.Contains(category.Trim())) return true;
        if (string.Equals(category, "olympic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "plyometrics", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(equipment, "bosu_ball", StringComparison.OrdinalIgnoreCase)) return true;

        // Keep the curation of near-identical variants local to known variation groups only.
        return variationGroup is not null
            && PreferredIdsByVariationGroup.TryGetValue(variationGroup, out var preferred)
            && !preferred.Contains(id, StringComparer.OrdinalIgnoreCase);
    }

    public static bool RequiresManualReview(string id) => RequireManualReviewIds.Contains(id);

    public static string? CurateCategory(string id, string? sourceCategory) =>
        id.Equals("extended-side-angle", StringComparison.OrdinalIgnoreCase) ? "stretching" : sourceCategory;

    public static string CurateGoalsJson(string id, string? category, string sourceGoalsJson)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceGoalsJson) ? "[]" : sourceGoalsJson);
        List<string> goals = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        if (string.Equals(category, "cardio", StringComparison.OrdinalIgnoreCase))
        {
            // Cardio is not a hypertrophy/strength exercise category. Preserve valid endurance
            // and power conditioning goals rather than flattening every cardio activity to endurance.
            goals.RemoveAll(x => x.Equals("hypertrophy", StringComparison.OrdinalIgnoreCase)
                              || x.Equals("strength", StringComparison.OrdinalIgnoreCase));
            if (goals.Count == 0) goals.Add("endurance");
        }

        if (id.Equals("extended-side-angle", StringComparison.OrdinalIgnoreCase))
            goals = ["mobility"];
        return JsonSerializer.Serialize(goals);
    }

    public static string CurateMusclesJson(string id, bool primary, string sourceMusclesJson)
    {
        if (id.Equals("sit-ups", StringComparison.OrdinalIgnoreCase))
            return primary ? """["rectus_abdominis"]""" : """["hip_flexors"]""";

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceMusclesJson) ? "[]" : sourceMusclesJson);
        return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetRawText() : "[]";
    }
}
