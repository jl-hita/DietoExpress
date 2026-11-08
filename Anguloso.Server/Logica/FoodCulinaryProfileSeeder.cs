using System.Text;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Materializa en <c>food_culinary_profile</c> el resultado del clasificador culinario para los alimentos del catálogo.
/// Es idempotente: no toca filas con <c>origin = 'manual'</c> y solo recalcula las automáticas cuando cambia la versión
/// del clasificador. No modifica la tabla <c>foods</c>.
/// </summary>
public static class FoodCulinaryProfileSeeder
{
    public static async Task<int> EnsureSeededAsync(angulosodbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        var existing = await context.food_culinary_profile.ToDictionaryAsync(p => p.food_id, cancellationToken);
        var foodRows = await context.foods.AsNoTracking()
            .Select(f => new { f.id, f.name, f.category, f.kcal, f.protein, f.carbs, f.fat, f.brands, f.source })
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var f in foodRows)
        {
            var data = FoodCulinaryClassifier.Classify(f.name, f.category, f.kcal, f.protein, f.carbs, f.fat);
            if (data != null && !string.IsNullOrWhiteSpace(f.brands) && !string.Equals(f.source, "bedca", StringComparison.OrdinalIgnoreCase))
                data = null; // productos de marca: no son alimentos genéricos de plantilla
            if (existing.TryGetValue(f.id, out var row))
            {
                if (row.origin == "manual" || row.classifier_version >= FoodCulinaryClassifier.Version) continue;
                if (data == null) { context.food_culinary_profile.Remove(row); changed++; continue; }
                data.CopyTo(row); changed++;
            }
            else if (data != null)
            {
                var entity = new food_culinary_profile { food_id = f.id, origin = "auto" };
                data.CopyTo(entity);
                context.food_culinary_profile.Add(entity);
                changed++;
            }
        }

        if (changed > 0) await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Perfiles culinarios de alimentos: {Total} en total; {Changed} creados o actualizados.", existing.Count + changed, changed);
        return changed;
    }

}
