using System.Text.Json;

namespace Anguloso.Server.Logica;

/// <summary>
/// Small, explicit curation layer for source records that should not be trusted blindly.
/// Rules are intentionally auditable; uncertain candidates stay in the human review queue.
/// </summary>
internal static class RepDbExerciseCuration
{
    // Air bike is deliberately excluded from DietoExpress's general-purpose exercise library:
    // it is a niche conditioning machine and not a useful resistance-training selection.
    public static readonly string[] ExcludedIds = ["air-bike"];

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

    public static string? CurateCategory(string id, string? sourceCategory) =>
        id.Equals("extended-side-angle", StringComparison.OrdinalIgnoreCase) ? "stretching" : sourceCategory;

    public static string? CurateBodyPart(string id, string? sourceBodyPart) =>
        id.Equals("double-kettlebell-clean", StringComparison.OrdinalIgnoreCase) ? "full_body" : sourceBodyPart;

    public static string CurateGoalsJson(string id, string? category, string sourceGoalsJson)
    {
        if (string.Equals(category, "cardio", StringComparison.OrdinalIgnoreCase))
            return """["endurance"]""";
        if (id.Equals("extended-side-angle", StringComparison.OrdinalIgnoreCase))
            return """["mobility"]""";
        if (id.Equals("bird-dog-hold", StringComparison.OrdinalIgnoreCase))
            return """["core_stability"]""";

        // Keep the source vocabulary and order for records that do not need a known correction.
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceGoalsJson) ? "[]" : sourceGoalsJson);
        return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetRawText() : "[]";
    }

    public static string CurateMusclesJson(string id, bool primary, string sourceMusclesJson)
    {
        if (id.Equals("sit-ups", StringComparison.OrdinalIgnoreCase))
            return primary ? """["rectus_abdominis"]""" : """["hip_flexors"]""";

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceMusclesJson) ? "[]" : sourceMusclesJson);
        return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetRawText() : "[]";
    }
}
