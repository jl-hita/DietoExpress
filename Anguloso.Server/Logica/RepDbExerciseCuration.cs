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
        "air-bike",
        "back-lever",
        "side-lying-hip-abduction-hold",
        "side-lying-hip-adduction-hold",
        "banded-seated-hip-abduction",
        "banded-standing-hip-adduction",
        "clamshells",
        "clamshells-hold",
        "decline-db-fly",
        "cocoons",
        "balance-dome-crunch",
        "ring-dead-hang",
        "cheat-curl",
        "close-grip-ez-bar-curl",
        "close-grip-barbell-curl",
        "double-kettlebell-bicep-curl",
        "one-arm-kettlebell-bicep-curl",
        "kettlebell-concentration-curl",
        "kettlebell-curl-iso-hold",
        "barbell-wrist-curl",
        "ez-bar-wrist-curl",
        "cable-wrist-curl",
        "kettlebell-wrist-curl",
        "wrist-curl",
        "spider-curl",
        "seated-dumbbell-curl",
        "kettlebell-curl",
        "kettlebell-reverse-wrist-curl",
        "banded-lateral-walk",
        "banded-sumo-walk",
        "snatch",
        "hang-snatch",
        "hang-clean",
        "clean",
        "clean-and-jerk",
        "dumbbell-clean-and-press"
    ];

    private static readonly HashSet<string> Excluded = new(ExcludedIds, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> RequireManualReviewIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "bird-dog-hold",
        "dead-hang",
        "ring-dead-hang",
        "sit-ups",
        "balance-dome-crunch",
        "double-kettlebell-clean"
    };

    public static bool ShouldExclude(string id) => Excluded.Contains(id);

    public static bool RequiresManualReview(string id) => RequireManualReviewIds.Contains(id);

    public static string? CurateCategory(string id, string? sourceCategory) => id switch
    {
        "extended-side-angle" => "stretching",
        "double-kettlebell-clean" => "olympic",
        _ => sourceCategory
    };

    public static string? CurateBodyPart(string id, string? sourceBodyPart) =>
        id.Equals("double-kettlebell-clean", StringComparison.OrdinalIgnoreCase) ? "full_body" : sourceBodyPart;

    public static string CurateGoalsJson(string id, string? category, string sourceGoalsJson)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceGoalsJson) ? "[]" : sourceGoalsJson);
        var goals = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

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
        if (id.Equals("bird-dog-hold", StringComparison.OrdinalIgnoreCase))
            goals = ["rehabilitation", "endurance"];

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
