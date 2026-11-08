using System.Text.Json;
using System.Text.Json.Serialization;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Aplica en <c>food_dietary_profile</c> la curación versionada en <c>FoodCuration/food-dietary-profiles.json</c>.
/// Idempotente: inserta los perfiles que faltan, actualiza los de versión inferior y nunca toca filas con
/// <c>origin = 'manual'</c>. Los alimentos del fichero que ya no existan en el catálogo se ignoran.
/// </summary>
public static class FoodDietaryProfileSeeder
{
    public const string FileName = "food-dietary-profiles.json";
    private const int BatchSize = 1000;

    public static readonly string[] AllowedAllergens =
    {
        "gluten", "crustaceos", "huevo", "pescado", "cacahuete", "soja", "lacteos",
        "frutos_cascara", "apio", "mostaza", "sesamo", "sulfitos", "altramuces", "moluscos"
    };

    public sealed class Entry
    {
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("externalId")] public string ExternalId { get; set; } = "";
        [JsonPropertyName("vegan")] public bool Vegan { get; set; }
        [JsonPropertyName("vegetarian")] public bool Vegetarian { get; set; }
        [JsonPropertyName("allergens")] public string[] Allergens { get; set; } = Array.Empty<string>();
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
    }

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "FoodCuration", FileName);

    /// <summary>Devuelve un error descriptivo si la entrada rompe el contrato (vegano sin ser vegetariano, alérgeno desconocido...).</summary>
    public static string? Validate(Entry e)
    {
        if (string.IsNullOrWhiteSpace(e.Source) || string.IsNullOrWhiteSpace(e.ExternalId)) return "source/externalId vacíos";
        if (e.Vegan && !e.Vegetarian) return "vegano implica vegetariano";
        if (e.Allergens == null) return "allergens nulo";
        var bad = e.Allergens.FirstOrDefault(a => !AllowedAllergens.Contains(a));
        if (bad != null) return $"alérgeno desconocido '{bad}'";
        if (e.Vegan && e.Allergens.Any(a => a is "huevo" or "lacteos" or "pescado" or "crustaceos" or "moluscos"))
            return "vegano incompatible con alérgenos de origen animal";
        return null;
    }

    public static async Task<int> EnsureSeededAsync(angulosodbContext context, ILogger logger, string? path = null, CancellationToken cancellationToken = default)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
        {
            logger.LogWarning("No se encontró el fichero de curación dietética {Path}; se omite.", path);
            return 0;
        }

        List<Entry> entries;
        await using (var stream = File.OpenRead(path))
            entries = await JsonSerializer.DeserializeAsync<List<Entry>>(stream, cancellationToken: cancellationToken) ?? new();

        var valid = new List<Entry>(entries.Count);
        foreach (var e in entries)
        {
            var error = Validate(e);
            if (error != null) { logger.LogWarning("Entrada de curación dietética ignorada ({Source}/{Id}): {Error}", e.Source, e.ExternalId, error); continue; }
            valid.Add(e);
        }
        if (valid.Count == 0) return 0;

        var idByKey = new Dictionary<(string, string), int>();
        foreach (var source in valid.Select(v => v.Source).Distinct())
        {
            var rows = await context.foods.AsNoTracking()
                .Where(f => f.source == source && f.external_id != null)
                .Select(f => new { f.id, f.external_id })
                .ToListAsync(cancellationToken);
            foreach (var r in rows) idByKey[(source, r.external_id)] = r.id;
        }

        var existing = await context.food_dietary_profile.ToDictionaryAsync(p => p.food_id, cancellationToken);
        var changed = 0;
        var pending = 0;
        foreach (var e in valid)
        {
            if (!idByKey.TryGetValue((e.Source, e.ExternalId), out var foodId)) continue;
            if (existing.TryGetValue(foodId, out var row))
            {
                if (row.origin == "manual" || row.profile_version >= e.Version) continue;
                Apply(row, e);
            }
            else
            {
                row = new food_dietary_profile { food_id = foodId };
                Apply(row, e);
                context.food_dietary_profile.Add(row);
                existing[foodId] = row;
            }
            changed++; pending++;
            if (pending >= BatchSize) { await context.SaveChangesAsync(cancellationToken); pending = 0; }
        }
        if (pending > 0) await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Perfil dietético de alimentos: {Entries} entradas en el fichero; {Changed} creadas o actualizadas.", valid.Count, changed);
        return changed;
    }

    private static void Apply(food_dietary_profile row, Entry e)
    {
        row.is_vegan = e.Vegan;
        row.is_vegetarian = e.Vegetarian;
        row.allergens = e.Allergens.Distinct().OrderBy(a => a, StringComparer.Ordinal).ToArray();
        row.origin = "ai-curated";
        row.profile_version = e.Version;
        row.updated_at = DateTime.UtcNow;
    }
}
