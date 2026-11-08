using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Anguloso.Server.Logica;

/// <summary>
/// Reglas compartidas para interpretar restricciones textuales y contrastarlas con
/// banderas estructuradas de alimentos. Las alergias compuestas no se separan por espacios.
/// </summary>
public static class DietaryRestrictionRules
{
    private static readonly Dictionary<string, string[]> AllergenAliases = new(StringComparer.Ordinal)
    {
        ["cacahuete"] = new[] { "cacahuete", "cacahuetes", "peanut", "peanuts", "peanut allergy" },
        ["frutos secos"] = new[] { "frutos secos", "fruto seco", "nut", "nuts", "tree nut", "tree nuts", "hazelnut", "hazelnuts", "almond", "almonds", "walnut", "walnuts", "cashew", "cashews", "pecan", "pecans", "pistachio", "pistachios", "brazil nut", "brazil nuts", "macadamia", "almendra", "almendras", "avellana", "avellanas", "nuez", "nueces", "anacardo", "anacardos", "pistacho", "pistachos", "nuez pecana" },
        ["leche"] = new[] { "leche", "lacteos", "lacteo", "milk", "dairy", "dairy allergy", "lactosa", "lactose" },
        ["huevo"] = new[] { "huevo", "huevos", "egg", "eggs" },
        ["soja"] = new[] { "soja", "soya", "soy", "soybean", "soybeans" },
        ["gluten"] = new[] { "gluten", "trigo", "centeno", "cebada", "espelta", "wheat", "rye", "barley" },
        ["pescado"] = new[] { "pescado", "fish" },
        ["crustaceos"] = new[] { "crustaceos", "crustaceo", "crustacean", "crustaceans", "marisco", "mariscos", "shellfish" },
        ["moluscos"] = new[] { "moluscos", "molusco", "mollusc", "molluscs", "mollusk", "mollusks", "marisco", "mariscos", "shellfish" },
        ["sesamo"] = new[] { "sesamo", "sesame" },
        ["mostaza"] = new[] { "mostaza", "mustard" },
        ["apio"] = new[] { "apio", "celery" },
        ["altramuces"] = new[] { "altramuz", "altramuces", "lupin", "lupine" },
        ["sulfitos"] = new[] { "sulfito", "sulfitos", "sulphites", "sulfites", "dioxido de azufre", "sulfur dioxide" }
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var raw in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;
            var character = char.ToLowerInvariant(raw);
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                pendingSpace = false;
                builder.Append(character);
            }
            else pendingSpace = true;
        }
        return builder.ToString();
    }

    public static HashSet<string> BuildForbiddenTerms(string? allergies, bool glutenIntolerance, bool lactoseIntolerance, bool fodmapsIntolerance)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        // Only declaration separators split allergies. Spaces remain part of each phrase.
        foreach (var raw in Regex.Split(allergies ?? string.Empty, @"[,;|\r\n]+"))
        {
            var term = Normalize(raw);
            if (term.Length < 3) continue;
            result.Add(term);

            // Match complete words/phrases, not arbitrary substrings (e.g. "nut" in
            // "nutrition"). Do not stop at the first group: "almond milk" may describe both.
            foreach (var pair in AllergenAliases)
            {
                var matchesGroup = ContainsPhrase(term, Normalize(pair.Key)) ||
                    pair.Value.Any(alias => ContainsPhrase(term, Normalize(alias)));
                if (!matchesGroup) continue;

                foreach (var alias in pair.Value) result.Add(Normalize(alias));
                if (pair.Key == "frutos secos") result.Add("tree nut");
            }
        }

        if (glutenIntolerance) AddAliases(result, "gluten");
        if (lactoseIntolerance) AddAliases(result, "leche");
        if (fodmapsIntolerance) Add(result, "fodmap", "cebolla", "ajo", "onion", "garlic");
        return result;
    }

    public static string[]? MergeKnownAllergenTags(IEnumerable<string>? existingTags, IEnumerable<string>? incomingTags)
    {
        var incoming = incomingTags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (incoming is { Length: > 0 }) return incoming;

        // Empty/missing provider data is unknown, not a declaration that known allergens disappeared.
        var existing = existingTags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return existing is { Length: > 0 } ? existing : existingTags?.ToArray();
    }

    public static bool HasKnownAllergenTags(IEnumerable<string>? allergenTags) =>
        allergenTags?.Any(tag => !string.IsNullOrWhiteSpace(tag)) == true;

    public static bool HasUnknownAllergenData(string? declaredAllergies, IEnumerable<string>? allergenTags) =>
        !string.IsNullOrWhiteSpace(declaredAllergies) && !HasKnownAllergenTags(allergenTags);

    public static bool HasUnknownAllergenData(string? declaredAllergies, IEnumerable<string>? allergenTags, string? dataStatus)
    {
        if (string.IsNullOrWhiteSpace(declaredAllergies)) return false;
        // "reported" means the source completed its allergen declaration; an empty tag list
        // can therefore mean that no allergens/traces were declared, not missing data.
        // Unknown, incomplete, or stale records remain unknown for allergic patients.
        return !string.Equals(dataStatus, "reported", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFoodAllowed(string? name, string? category, IEnumerable<string>? dietaryFlags, IEnumerable<string> forbiddenTerms)
        => new ExclusionMatcher(forbiddenTerms).IsAllowed(name, category, dietaryFlags);

    /// <summary>
    /// Versión precompilada de <see cref="IsFoodAllowed"/> para filtrar un catálogo completo: los términos se normalizan
    /// una sola vez en lugar de una vez por alimento. Mantiene exactamente la misma semántica de coincidencia.
    /// </summary>
    public sealed class ExclusionMatcher
    {
        private readonly string[] _terms;
        private readonly string[]?[] _flagGroups;

        public ExclusionMatcher(IEnumerable<string> forbiddenTerms)
        {
            _terms = forbiddenTerms.Select(Normalize).Where(term => term.Length >= 2).Distinct().ToArray();
            _flagGroups = _terms.Select(FlagGroupFor).ToArray();
        }

        public int TermCount => _terms.Length;

        public bool IsAllowed(string? name, string? category, IEnumerable<string>? dietaryFlags)
        {
            if (_terms.Length == 0) return true;
            var flagList = dietaryFlags as IReadOnlyCollection<string> ?? (dietaryFlags ?? Array.Empty<string>()).ToArray();
            var text = Normalize(string.Join(" ", new[] { name, category }.Where(value => !string.IsNullOrWhiteSpace(value)).Concat(flagList)));
            HashSet<string>? normalizedFlags = null;

            for (var i = 0; i < _terms.Length; i++)
            {
                var term = _terms[i];
                if (ContainsPhrase(text, term)) return false;
                // Preserve the legacy lactose-prefix keyword used by the generator, but keep
                // all other exclusions on whole-word/whole-phrase boundaries.
                if (term == "lact" && text.Contains("lact", StringComparison.Ordinal)) return false;
                if (flagList.Count == 0) continue;
                normalizedFlags ??= flagList.Select(Normalize).ToHashSet(StringComparer.Ordinal);
                var group = _flagGroups[i];
                if (group != null ? normalizedFlags.Overlaps(group) : normalizedFlags.Contains(term)) return false;
            }
            return true;
        }
    }

    private static string[]? FlagGroupFor(string term)
    {
        if (term is "peanut" or "peanuts" or "cacahuete" or "cacahuetes")
            return new[] { "peanut", "peanuts", "peanut allergy", "cacahuete", "cacahuetes" };
        if (term is "tree nut" or "tree nuts" or "frutos secos" or "fruto seco" ||
            term is "almendra" or "almendras" or "avellana" or "avellanas" or "nuez" or "nueces")
            return new[] { "tree nut", "tree nuts", "tree nut allergy", "tree nut allergen", "nuts", "frutos secos" };
        if (term is "leche" or "milk" or "lactosa" or "lactose" or "dairy" or "lacteos")
            return new[] { "milk", "dairy", "dairy allergy", "lactose", "lactose intolerance", "leche" };
        if (term is "soja" or "soya" or "soy" or "soybean")
            return new[] { "soy", "soya", "soybean", "soy allergy" };
        if (term is "gluten" or "trigo" or "wheat" or "cebada" or "barley" or "centeno" or "rye")
            return new[] { "gluten", "wheat", "barley", "rye", "celiac" };
        return null;
    }

    private static bool ContainsPhrase(string text, string phrase) =>
        !string.IsNullOrWhiteSpace(phrase) &&
        (" " + text + " ").Contains(" " + phrase + " ", StringComparison.Ordinal);

    private static void AddAliases(HashSet<string> target, string key)
    {
        if (AllergenAliases.TryGetValue(key, out var aliases))
            foreach (var alias in aliases) target.Add(Normalize(alias));
        else target.Add(Normalize(key));
    }

    private static void Add(HashSet<string> target, params string[] terms)
    {
        foreach (var term in terms) target.Add(Normalize(term));
    }
}