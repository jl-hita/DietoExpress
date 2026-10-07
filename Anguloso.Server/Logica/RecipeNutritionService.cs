using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Fuente única de verdad para la nutrición de recetas.
/// Recalcula siempre desde gramos y alimentos persistidos y propaga dietary_flags
/// como señales de restricciones/alérgenos a la receta y a sus usos en dietas.
/// </summary>
public sealed class RecipeNutritionService
{
    private readonly angulosodbContext _context;

    public RecipeNutritionService(angulosodbContext context) => _context = context;

    public Task<RecipeDetailDto> BuildDetailAsync(
        recipes recipe,
        IReadOnlyDictionary<int, foods> foodMap,
        CancellationToken cancellationToken = default)
    {
        var servings = recipe.servings <= 0 ? 1 : recipe.servings;
        var ingredients = recipe.recipe_items
            .Where(i => i.grams > 0 && foodMap.ContainsKey(i.food_id))
            .Select(i => BuildIngredient(i, foodMap[i.food_id]))
            .ToList();

        var totalKcal = Sum(ingredients.Select(i => i.Kcal));
        var totalProtein = Sum(ingredients.Select(i => i.Protein));
        var totalCarbs = Sum(ingredients.Select(i => i.Carbs));
        var totalFat = Sum(ingredients.Select(i => i.Fat));
        var flags = ingredients
            .SelectMany(i => i.DietaryFlags)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f)
            .ToList();

        var result = new RecipeDetailDto
        {
            Id = recipe.id,
            Name = recipe.name,
            Instructions = recipe.instructions,
            CreatedAt = recipe.created_at,
            Servings = servings,
            YieldGrams = recipe.yield_grams,
            Ingredients = ingredients,
            DietaryFlags = flags,
            Nutrition = new RecipeNutritionDto
            {
                TotalKcal = totalKcal,
                TotalProtein = totalProtein,
                TotalCarbs = totalCarbs,
                TotalFat = totalFat,
                PerServingKcal = Divide(totalKcal, servings),
                PerServingProtein = Divide(totalProtein, servings),
                PerServingCarbs = Divide(totalCarbs, servings),
                PerServingFat = Divide(totalFat, servings)
            }
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// Actualiza el caché nutricional de todos los meal_items que utilizan la receta.
    /// Así una sustitución de ingrediente se propaga receta → comida → día sin perder
    /// los objetivos almacenados en la dieta.
    /// </summary>
    public async Task RefreshRecipeUsagesAsync(int recipeId, CancellationToken cancellationToken = default)
    {
        var recipe = await _context.recipes.AsNoTracking()
            .Include(r => r.recipe_items)
                .ThenInclude(i => i.food)
            .FirstOrDefaultAsync(r => r.id == recipeId, cancellationToken);

        if (recipe == null) return;

        var servings = recipe.servings <= 0 ? 1 : recipe.servings;
        var totalKcal = recipe.recipe_items.Sum(i => Value(i.food?.kcal, i.grams));
        var totalProtein = recipe.recipe_items.Sum(i => Value(i.food?.protein, i.grams));
        var totalCarbs = recipe.recipe_items.Sum(i => Value(i.food?.carbs, i.grams));
        var totalFat = recipe.recipe_items.Sum(i => Value(i.food?.fat, i.grams));

        var usages = await _context.meal_items
            .Where(i => i.recipe_id == recipeId)
            .ToListAsync(cancellationToken);

        foreach (var item in usages)
        {
            var multiplier = item.recipe_servings.GetValueOrDefault(1);
            item.kcal = (decimal?)Math.Round(totalKcal / (double)servings * (double)multiplier, 2);
            item.protein = (decimal?)Math.Round(totalProtein / (double)servings * (double)multiplier, 2);
            item.carbs = (decimal?)Math.Round(totalCarbs / (double)servings * (double)multiplier, 2);
            item.fat = (decimal?)Math.Round(totalFat / (double)servings * (double)multiplier, 2);

            if (recipe.yield_grams.HasValue)
                item.grams = Math.Round(recipe.yield_grams.Value / servings * (decimal)multiplier, 2);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static RecipeIngredientDto BuildIngredient(recipe_items item, foods food)
    {
        var factor = (double)item.grams / 100.0;
        return new RecipeIngredientDto
        {
            FoodId = food.id,
            FoodName = food.name,
            Brands = food.brands,
            Grams = item.grams,
            Kcal = Scale(food.kcal, factor),
            Protein = Scale(food.protein, factor),
            Carbs = Scale(food.carbs, factor),
            Fat = Scale(food.fat, factor),
            DietaryFlags = food.dietary_flags ?? Array.Empty<string>()
        };
    }

    private static double? Scale(double? value, double factor)
        => value.HasValue ? Math.Round(value.Value * factor, 2) : null;

    private static double? Sum(IEnumerable<double?> values)
    {
        var concrete = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return concrete.Count == 0 ? null : Math.Round(concrete.Sum(), 2);
    }

    private static double? Divide(double? value, decimal divisor)
        => value.HasValue && divisor > 0 ? Math.Round(value.Value / (double)divisor, 2) : null;

    private static double Value(double? per100, decimal grams)
        => per100.GetValueOrDefault() * (double)grams / 100.0;
}
