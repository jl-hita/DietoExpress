using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Segunda fase del generador: optimización global de la semana, reglas de frecuencia,
/// recetas, control de compras y regeneración segura. Mantiene las restricciones clínicas
/// resueltas por DietGeneratorService como restricciones duras y usa estas reglas como
/// optimización blanda cuando existen varias soluciones válidas.
/// </summary>
public sealed class AdvancedDietOptimizerService
{
    private readonly angulosodbContext _context;

    public AdvancedDietOptimizerService(angulosodbContext context)
    {
        _context = context;
    }

    public async Task OptimizeWeeklyPlanAsync(
        DietDetailDto diet,
        GenerateDietRequestDto request,
        IReadOnlyCollection<foods> allowedFoods,
        int? tenantId,
        double targetKcal,
        double targetProtein,
        double targetCarbs,
        double targetFat,
        DietTherapyProfile? therapyProfile = null,
        CancellationToken cancellationToken = default)
    {
        if (diet.Days == null || diet.Days.Count == 0 || allowedFoods.Count == 0)
            return;

        var foodMap = allowedFoods
            .GroupBy(f => f.id)
            .ToDictionary(g => g.Key, g => g.First());

        // Primero intentamos introducir recetas reales. Después optimizamos intercambios
        // sobre toda la semana; así la receta compite contra el plan existente en igualdad
        // de condiciones nutricionales y no se convierte en una simple sustitución estética.
        await ApplyRecipeCandidatesAsync(diet, request, foodMap, tenantId, targetKcal, cancellationToken);

        var candidates = allowedFoods
            .Where(IsOptimizerFood)
            .Take(2500)
            .ToList();

        if (candidates.Count == 0)
            return;

        // Búsqueda local global: cada iteración evalúa cambios en cualquier día/comida y
        // conserva solo movimientos que mejoran la función objetivo completa.
        var currentScore = CalculateGlobalScore(
            diet, foodMap, targetKcal, targetProtein, targetCarbs, targetFat, request, therapyProfile);

        for (var pass = 0; pass < 6; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var improved = false;

            foreach (var day in diet.Days.OrderBy(d => d.DayIndex))
            {
                foreach (var meal in day.Meals)
                {
                    var originalItems = meal.Items?.ToList() ?? new List<MealItemDto>();
                    foreach (var item in originalItems)
                    {
                        if (!item.FoodId.HasValue || !foodMap.ContainsKey(item.FoodId.Value))
                            continue;

                        var source = foodMap[item.FoodId.Value];
                        var role = InferRole(meal.Name, source);
                        var mealTarget = targetKcal * MealShare(meal.Name, request.MealsPerDay);

                        var alternatives = candidates
                            .Where(f => f.id != source.id)
                            .Where(f => SameRole(role, f))
                            .Where(f => !meal.Items.Any(i => i.FoodId == f.id))
                            .OrderBy(f => NutrientDistance(f, source))
                            .Take(12)
                            .ToList();

                        foreach (var candidate in alternatives)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var snapshot = CloneItem(item);
                            ReplaceItem(item, candidate, (double)(item.Grams ?? 0));

                            RebalanceMeal(meal, mealTarget, targetProtein * MealShare(meal.Name, request.MealsPerDay),
                                targetCarbs * MealShare(meal.Name, request.MealsPerDay),
                                targetFat * MealShare(meal.Name, request.MealsPerDay));

                            var candidateScore = CalculateGlobalScore(
                                diet, foodMap, targetKcal, targetProtein, targetCarbs, targetFat, request, therapyProfile);

                            if (candidateScore + 0.000001 < currentScore)
                            {
                                currentScore = candidateScore;
                                improved = true;
                                break;
                            }

                            RestoreItem(item, snapshot);
                        }
                    }
                }
            }

            if (!improved)
                break;
        }

        // Repair final de frecuencias: si una frecuencia positiva está claramente por debajo
        // del objetivo, se intenta reemplazar el alimento menos útil de una comida compatible.
        RepairWeeklyFrequencies(diet, candidates, targetKcal, targetProtein, targetCarbs, targetFat, request, therapyProfile);

        // El último reequilibrado se hace sobre cada día, porque las sustituciones inteligentes
        // deben conservar los objetivos diarios aunque hayan cambiado densidades nutricionales.
        foreach (var day in diet.Days)
            RebalanceDay(day, targetKcal, targetProtein, targetCarbs, targetFat);

        var score = CalculateGlobalScore(diet, foodMap, targetKcal, targetProtein, targetCarbs, targetFat, request, therapyProfile);
        var shopping = CalculateShoppingMetrics(diet);

        diet.Notes = (diet.Notes ?? string.Empty)
            + $" Motor avanzado: optimización global semanal activa; {shopping.UniqueFoods} alimentos únicos,"
            + $" {shopping.ReusedFoods} reutilizados para reducir compras y desperdicio; puntuación global {score:0.000}."
            + " Se han aplicado frecuencias alimentarias, diversidad por familias, recetas compatibles"
            + " y reequilibrado posterior a sustituciones.";
    }

    public DietDetailDto RegenerateMeal(
        DietDetailDto diet,
        int dayIndex,
        int mealIndex,
        IReadOnlyCollection<foods> allowedFoods,
        double targetKcal,
        double targetProtein,
        double targetCarbs,
        double targetFat,
        GenerateDietRequestDto request,
        DietTherapyProfile? therapyProfile = null)
    {
        var day = diet.Days.FirstOrDefault(d => d.DayIndex == dayIndex);
        var meal = day?.Meals.FirstOrDefault(m => m.MealIndex == mealIndex);
        if (day == null || meal == null)
            throw new ArgumentException("No se encontró la comida solicitada.");

        var foodMap = allowedFoods.GroupBy(f => f.id).ToDictionary(g => g.Key, g => g.First());
        var before = meal.Items.ToList();
        var originalScore = CalculateGlobalScore(diet, foodMap, targetKcal, targetProtein, targetCarbs, targetFat, request, therapyProfile);

        foreach (var item in meal.Items.ToList())
        {
            if (!item.FoodId.HasValue || !foodMap.TryGetValue(item.FoodId.Value, out var source))
                continue;

            var role = InferRole(meal.Name, source);
            var candidate = allowedFoods
                .Where(f => f.id != source.id && SameRole(role, f))
                .OrderBy(f => NutrientDistance(f, source))
                .ThenBy(f => WeeklyUseCount(diet, f.id))
                .FirstOrDefault();

            if (candidate != null)
                ReplaceItem(item, candidate, (double)(item.Grams ?? 0));
        }

        RebalanceMeal(
            meal,
            targetKcal * MealShare(meal.Name, request.MealsPerDay),
            targetProtein * MealShare(meal.Name, request.MealsPerDay),
            targetCarbs * MealShare(meal.Name, request.MealsPerDay),
            targetFat * MealShare(meal.Name, request.MealsPerDay));

        var newScore = CalculateGlobalScore(diet, foodMap, targetKcal, targetProtein, targetCarbs, targetFat, request, therapyProfile);
        if (newScore >= originalScore)
            meal.Items = before;

        return diet;
    }

    private async Task ApplyRecipeCandidatesAsync(
        DietDetailDto diet,
        GenerateDietRequestDto request,
        IReadOnlyDictionary<int, foods> foodMap,
        int? tenantId,
        double targetKcal,
        CancellationToken cancellationToken)
    {
        if (!request.UseRecipes || !tenantId.HasValue)
            return;

        var recipes = await _context.recipes
            .AsNoTracking()
            .Include(r => r.recipe_items)
                .ThenInclude(i => i.food)
            .Where(r => r.tenant_id == tenantId.Value)
            .OrderByDescending(r => r.created_at)
            .Take(150)
            .ToListAsync(cancellationToken);

        foreach (var day in diet.Days)
        {
            foreach (var meal in day.Meals.Where(m => m.Name.Contains("comida", StringComparison.OrdinalIgnoreCase)
                                                    || m.Name.Contains("cena", StringComparison.OrdinalIgnoreCase)))
            {
                var mealTarget = targetKcal * MealShare(meal.Name, request.MealsPerDay);
                var currentError = MealEnergyError(meal, mealTarget);

                var best = recipes
                    .Select(recipe => new
                    {
                        Recipe = recipe,
                        Ingredients = recipe.recipe_items
                            .Where(i => i.food != null && i.grams > 0)
                            .ToList()
                    })
                    .Where(x => x.Ingredients.Count >= 2
                             && x.Ingredients.Count == x.Recipe.recipe_items.Count
                             && x.Ingredients.All(i => foodMap.ContainsKey(i.food_id)))
                    .Select(x => new
                    {
                        x.Recipe,
                        x.Ingredients,
                        Error = RecipeEnergyError(x.Ingredients, mealTarget)
                    })
                    .Where(x => x.Error < 0.30)
                    .OrderBy(x => x.Error)
                    .FirstOrDefault();

                if (best != null && best.Error + 0.02 < currentError)
                {
                    var totalKcal = best.Ingredients.Sum(i => (i.food.kcal ?? 0) * (double)i.grams / 100.0);
                    if (totalKcal <= 0) continue;

                    var scale = mealTarget / totalKcal;
                    meal.Items = new List<MealItemDto> { ToRecipeMealItem(best.Recipe, best.Ingredients, scale) };

                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
    }

    private static double CalculateGlobalScore(DietDetailDto diet, IReadOnlyDictionary<int, foods> foodMap, double targetKcal, double targetProtein, double targetCarbs, double targetFat, GenerateDietRequestDto request, DietTherapyProfile? therapyProfile = null)
    {
        var days = diet.Days.ToList();
        if (days.Count == 0) return double.MaxValue;
        var dailyError = days.Sum(day => MacroError(day.Meals.SelectMany(m => m.Items), targetKcal, targetProtein, targetCarbs, targetFat)) / days.Count;
        var signatures = days.SelectMany(d => d.Meals).Select(CreateSignature).ToList();
        var duplicateMeals = signatures.Count - signatures.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var repeatedFoods = days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Where(i => i.FoodId.HasValue).GroupBy(i => i.FoodId!.Value).Sum(g => Math.Max(0, g.Count() - request.MaxWeeklyFoodRepetitions));
        var familyCounts = days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Where(i => i.FoodId.HasValue && foodMap.ContainsKey(i.FoodId.Value)).GroupBy(i => GetFamily(foodMap[i.FoodId!.Value])).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var frequencyPenalty = FrequencyPenalty(familyCounts, days.Count, therapyProfile);
        var therapyPenalty = CalculateTherapyNutrientPenalty(days, foodMap, therapyProfile);
        var shopping = CalculateShoppingMetrics(diet);
        return dailyError * 1000 + duplicateMeals * 7 + repeatedFoods * 1.5 + frequencyPenalty * 5 + therapyPenalty * 8 + Math.Max(0, shopping.UniqueFoods - request.ShoppingVarietyThreshold) * 0.20;
    }

    private static double FrequencyPenalty(Dictionary<string, int> counts, int days, DietTherapyProfile? therapyProfile)
    {
        var weekly = therapyProfile?.FrequencyTargets ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["fruta"] = 1, ["verdura"] = 2, ["legumbre"] = 3.0 / 7.0, ["pescado_marisco"] = 2.0 / 7.0, ["frutos_secos"] = 4.0 / 7.0 };
        var targets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["fruta"] = Math.Max(1, (int)Math.Ceiling(days * weekly.GetValueOrDefault("fruta"))),
            ["verdura"] = Math.Max(2, (int)Math.Ceiling(days * weekly.GetValueOrDefault("verdura"))),
            ["legumbre"] = Math.Max(1, (int)Math.Ceiling(days * weekly.GetValueOrDefault("legumbre"))),
            ["pescado_marisco"] = Math.Max(1, (int)Math.Ceiling(days * weekly.GetValueOrDefault("pescado_marisco"))),
            ["frutos_secos"] = Math.Max(1, (int)Math.Ceiling(days * weekly.GetValueOrDefault("frutos_secos")))
        };
        return targets.Sum(target => Math.Max(0, target.Value - counts.GetValueOrDefault(target.Key)) * 2.0);
    }

    private static double CalculateTherapyNutrientPenalty(IEnumerable<DietDayDto> days, IReadOnlyDictionary<int, foods> foodMap, DietTherapyProfile? profile)
    {
        if (profile == null || profile.NutrientWeights.Count == 0) return 0;
        var items = days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Where(i => i.FoodId.HasValue && foodMap.ContainsKey(i.FoodId.Value) && (i.Grams ?? 0) > 0).ToList();
        if (items.Count == 0) return 0;
        double penalty = 0;
        foreach (var item in items)
        {
            var food = foodMap[item.FoodId!.Value];
            var grams = (double)item.Grams!.Value / 100.0;
            foreach (var rule in profile.NutrientWeights)
            {
                var value = rule.Key.ToLowerInvariant() switch { "sugar" => food.sugar ?? 0, "salt" => food.salt ?? 0, "fiber" => food.fiber ?? 0, "potassium" => food.potassium_mg ?? 0, _ => 0 };
                if (value <= 0) continue;
                var normalized = rule.Key.Equals("salt", StringComparison.OrdinalIgnoreCase) ? value / 1.0 : rule.Key.Equals("sugar", StringComparison.OrdinalIgnoreCase) ? value / 10.0 : rule.Key.Equals("fiber", StringComparison.OrdinalIgnoreCase) ? value / 3.0 : value / 100.0;
                penalty += Math.Max(0, rule.Value) * normalized * grams;
                if (rule.Value < 0) penalty -= Math.Min(1, normalized) * Math.Abs(rule.Value) * grams * 0.1;
            }
        }
        return Math.Max(0, penalty / Math.Max(1, items.Count));
    }

    private static void RepairWeeklyFrequencies(DietDetailDto diet, IReadOnlyCollection<foods> candidates, double targetKcal, double targetProtein, double targetCarbs, double targetFat, GenerateDietRequestDto request, DietTherapyProfile? therapyProfile = null)
    {
        var weekly = therapyProfile?.FrequencyTargets ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var familyTargets = new[] { ("fruta", Math.Max(1, (int)Math.Ceiling(diet.Days.Count * weekly.GetValueOrDefault("fruta", 1)))), ("verdura", Math.Max(2, (int)Math.Ceiling(diet.Days.Count * weekly.GetValueOrDefault("verdura", 2)))), ("legumbre", Math.Max(1, (int)Math.Ceiling(diet.Days.Count * weekly.GetValueOrDefault("legumbre", 3.0 / 7.0)))), ("pescado_marisco", Math.Max(1, (int)Math.Ceiling(diet.Days.Count * weekly.GetValueOrDefault("pescado_marisco", 2.0 / 7.0)))) };
        foreach (var (family, target) in familyTargets)
        {
            for (var i = 0; i < target; i++)
            {
                var current = diet.Days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Count(item => item.FoodId.HasValue && candidates.Any(f => f.id == item.FoodId.Value && GetFamily(f) == family));
                if (current >= target) break;
                var targetFood = candidates.FirstOrDefault(f => GetFamily(f) == family);
                if (targetFood == null) break;
                var candidateMeal = diet.Days.SelectMany(d => d.Meals).Where(m => m.Name.Contains("comida", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("cena", StringComparison.OrdinalIgnoreCase) || family == "fruta").Select(m => new { Meal = m, Item = m.Items.FirstOrDefault(i => i.FoodId.HasValue && candidates.Any(f => f.id == i.FoodId.Value && SameRole(InferRole(m.Name, f), targetFood))) }).Where(x => x.Item != null).OrderBy(x => x.Meal.Items.Count(i => i.FoodId == targetFood.id)).FirstOrDefault();
                if (candidateMeal?.Item == null) continue;
                ReplaceItem(candidateMeal.Item, targetFood, Math.Max(20, (double)(candidateMeal.Item.Grams ?? 0)));
            }
        }
    }

    private static void RebalanceDay(DietDayDto day, double targetKcal, double targetProtein, double targetCarbs, double targetFat)
    {
        var items = day.Meals.SelectMany(m => m.Items).Where(i => (i.Grams ?? 0) > 0).ToList();
        if (items.Count == 0) return;
        var bounds = items.ToDictionary(item => item, item => (
            Min: Math.Max(5.0, (double)(item.Grams ?? 0) * 0.75),
            Max: Math.Min(350.0, (double)(item.Grams ?? 0) * 1.25)));

        for (var pass = 0; pass < 18; pass++)
        {
            var current = MacroError(items, targetKcal, targetProtein, targetCarbs, targetFat);
            var changed = false;
            foreach (var item in items)
            {
                var grams = (double)(item.Grams ?? 0);
                var best = grams;
                var bestError = current;
                foreach (var candidate in new[] {
                    Math.Clamp(grams - 5, bounds[item].Min, bounds[item].Max),
                    Math.Clamp(grams + 5, bounds[item].Min, bounds[item].Max)
                }.Distinct())
                {
                    ApplyItemRatio(item, candidate);
                    var error = MacroError(items, targetKcal, targetProtein, targetCarbs, targetFat);
                    if (error < bestError) { bestError = error; best = candidate; }
                }
                ApplyItemRatio(item, best);
                if (bestError + 0.000001 < current) { changed = true; current = bestError; }
            }
            if (!changed || current < 0.0025) break;
        }
    }

    private static void RebalanceMeal(MealDto meal, double targetKcal, double targetProtein, double targetCarbs, double targetFat)
    {
        var items = meal.Items.Where(i => (i.Grams ?? 0) > 0).ToList();
        if (items.Count == 0) return;
        var bounds = items.ToDictionary(item => item, item => (
            Min: Math.Max(5.0, (double)(item.Grams ?? 0) * 0.75),
            Max: Math.Min(350.0, (double)(item.Grams ?? 0) * 1.25)));

        for (var pass = 0; pass < 10; pass++)
        {
            var current = MacroError(items, targetKcal, targetProtein, targetCarbs, targetFat);
            var improved = false;
            foreach (var item in items)
            {
                var grams = (double)(item.Grams ?? 0);
                var best = grams;
                var bestError = current;
                foreach (var candidate in new[] {
                    Math.Clamp(grams - 5, bounds[item].Min, bounds[item].Max),
                    Math.Clamp(grams + 5, bounds[item].Min, bounds[item].Max)
                }.Distinct())
                {
                    ApplyItemRatio(item, candidate);
                    var error = MacroError(items, targetKcal, targetProtein, targetCarbs, targetFat);
                    if (error < bestError) { best = candidate; bestError = error; }
                }
                ApplyItemRatio(item, best);
                if (bestError + 0.000001 < current) { current = bestError; improved = true; }
            }
            if (!improved) break;
        }
    }

    private static double MacroError(IEnumerable<MealItemDto> items, double kcal, double protein, double carbs, double fat)
    {
        var list = items.ToList();
        var ak = list.Sum(i => (double)(i.Kcal ?? 0));
        var ap = list.Sum(i => (double)(i.Protein ?? 0));
        var ac = list.Sum(i => (double)(i.Carbs ?? 0));
        var af = list.Sum(i => (double)(i.Fat ?? 0));
        return 0.35 * Math.Pow(RelativeError(ak, kcal), 2) + 0.25 * Math.Pow(RelativeError(ap, protein), 2) + 0.20 * Math.Pow(RelativeError(ac, carbs), 2) + 0.20 * Math.Pow(RelativeError(af, fat), 2);
    }

    private static double RelativeError(double actual, double target) => target <= 0 ? 0 : (actual - target) / target;
    private static double MealEnergyError(MealDto meal, double target) => target <= 0 ? 0 : Math.Abs(meal.Items.Sum(i => (double)(i.Kcal ?? 0)) - target) / target;
    private static double RecipeEnergyError(IEnumerable<recipe_items> ingredients, double target) { var kcal = ingredients.Sum(i => (i.food.kcal ?? 0) * (double)i.grams / 100.0); return target <= 0 ? 0 : Math.Abs(kcal - target) / target; }

    private static void ApplyItemRatio(MealItemDto item, double grams)
    {
        var old = (double)(item.Grams ?? 0);
        if (old <= 0) return;
        var ratio = grams / old;
        item.Grams = (decimal)grams;
        item.Kcal = (decimal)Math.Round((double)(item.Kcal ?? 0) * ratio, 1);
        item.Protein = (decimal)Math.Round((double)(item.Protein ?? 0) * ratio, 1);
        item.Carbs = (decimal)Math.Round((double)(item.Carbs ?? 0) * ratio, 1);
        item.Fat = (decimal)Math.Round((double)(item.Fat ?? 0) * ratio, 1);
    }

    private static MealItemDto ToMealItem(foods food, double grams)
    {
        var item = new MealItemDto();
        ReplaceItem(item, food, grams);
        return item;
    }

    private static MealItemDto ToRecipeMealItem(recipes recipe, IEnumerable<recipe_items> ingredients, double scale)
    {
        var recipeServings = recipe.servings <= 0 ? 1m : recipe.servings;
        var totalKcal = ingredients.Sum(i => (i.food?.kcal ?? 0) * (double)i.grams / 100.0);
        var totalProtein = ingredients.Sum(i => (i.food?.protein ?? 0) * (double)i.grams / 100.0);
        var totalCarbs = ingredients.Sum(i => (i.food?.carbs ?? 0) * (double)i.grams / 100.0);
        var totalFat = ingredients.Sum(i => (i.food?.fat ?? 0) * (double)i.grams / 100.0);
        return new MealItemDto
        {
            RecipeId = recipe.id,
            RecipeName = recipe.name,
            RecipeServings = (decimal)scale,
            Kcal = (decimal)Math.Round(totalKcal / (double)recipeServings * scale, 1),
            Protein = (decimal)Math.Round(totalProtein / (double)recipeServings * scale, 1),
            Carbs = (decimal)Math.Round(totalCarbs / (double)recipeServings * scale, 1),
            Fat = (decimal)Math.Round(totalFat / (double)recipeServings * scale, 1),
            Grams = recipe.yield_grams.HasValue ? Math.Round(recipe.yield_grams.Value / recipeServings * (decimal)scale, 0) : Math.Round(ingredients.Sum(i => i.grams) / recipeServings * (decimal)scale, 0)
        };
    }

    private static void ReplaceItem(MealItemDto item, foods food, double grams)
    {
        item.FoodId = food.id;
        item.FoodName = food.name;
        item.Grams = (decimal)Math.Round(Math.Clamp(grams, 5, 350), 0);
        var ratio = (double)item.Grams / 100.0;
        item.Kcal = (decimal)Math.Round((food.kcal ?? 0) * ratio, 1);
        item.Protein = (decimal)Math.Round((food.protein ?? 0) * ratio, 1);
        item.Carbs = (decimal)Math.Round((food.carbs ?? 0) * ratio, 1);
        item.Fat = (decimal)Math.Round((food.fat ?? 0) * ratio, 1);
        item.ExchangeGroupId = food.exchange_group_id;
    }

    private static MealItemDto CloneItem(MealItemDto source) => new()
    {
        Id = source.Id, FoodId = source.FoodId, Grams = source.Grams, Kcal = source.Kcal, Protein = source.Protein, Carbs = source.Carbs, Fat = source.Fat, FoodName = source.FoodName, ExchangeGroupId = source.ExchangeGroupId, ExchangeGroupName = source.ExchangeGroupName, ExchangeCount = source.ExchangeCount
    };

    private static void RestoreItem(MealItemDto target, MealItemDto source)
    {
        target.FoodId = source.FoodId; target.Grams = source.Grams; target.Kcal = source.Kcal; target.Protein = source.Protein; target.Carbs = source.Carbs; target.Fat = source.Fat; target.FoodName = source.FoodName; target.ExchangeGroupId = source.ExchangeGroupId; target.ExchangeGroupName = source.ExchangeGroupName; target.ExchangeCount = source.ExchangeCount;
    }

    private static string CreateSignature(MealDto meal) => string.Join("|", meal.Items.Select(i => i.FoodId ?? 0).OrderBy(id => id));
    private static int WeeklyUseCount(DietDetailDto diet, int foodId) => diet.Days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Count(i => i.FoodId == foodId);

    private static double NutrientDistance(foods a, foods b)
    {
        double Rel(double x, double y) => Math.Abs((x - y) / Math.Max(1, Math.Abs(y)));
        var nutrientDistance = Rel(a.kcal ?? 0, b.kcal ?? 0) + Rel(a.protein ?? 0, b.protein ?? 0) + Rel(a.carbs ?? 0, b.carbs ?? 0) + Rel(a.fat ?? 0, b.fat ?? 0);
        var exchangeBonus = a.exchange_group_id.HasValue && a.exchange_group_id == b.exchange_group_id ? 0.35 : 0;
        return Math.Max(0, nutrientDistance - exchangeBonus);
    }

    private static bool IsOptimizerFood(foods food) => food.kcal.HasValue && food.kcal > 0 && !string.IsNullOrWhiteSpace(food.name);

    private static string InferRole(string mealName, foods food)
    {
        var n = NormalizeFoodTerm(food.name ?? string.Empty);
        var c = NormalizeFoodTerm(food.category ?? string.Empty);
        var meal = NormalizeFoodTerm(mealName);
        bool isVegetable = c.Contains("verdura") || c.Contains("hortal") || new[] { "tomate","lechuga","espinaca","calabacin","berenjena","brocoli","zanahoria","pepino","pimiento","judia verde","champiñon","calabaza" }.Any(n.Contains);
        bool isFruit = c.Contains("fruta") || new[] { "manzana","platano","naranja","pera","fresa","kiwi","mandarina","sandia","melon","melocoton","albaricoque","ciruela","uva" }.Any(n.Contains);
        bool isFat = n.Contains("aceite") || new[] { "nuez","almendra","avellana","anacardo","pistacho" }.Any(n.Contains);
        bool isCarb = new[] { "arroz","pasta","patata","boniato","pan","avena","quinoa","macarron","espagueti","lenteja","garbanzo","alubia","cuscus","cuscús","tostada" }.Any(n.Contains);
        bool isDairyOrEgg = new[] { "yogur","leche","queso","kefir","huevo","clara" }.Any(n.Contains);
        bool isProtein = isDairyOrEgg || new[] { "pollo","pavo","ternera","cerdo","conejo","atun","salmon","merluza","bacalao","dorada","lubina","sardina","caballa","gamba","tofu","tempeh" }.Any(n.Contains);

        if (meal.Contains("desayuno") || meal.Contains("media") || meal.Contains("merienda"))
        {
            if (isFruit) return "fruit";
            if (isCarb) return "carb";
            if (isDairyOrEgg || isProtein) return "protein";
            if (isFat) return "fat";
        }
        if (isVegetable) return "vegetable";
        if (isFruit) return "fruit";
        if (isFat) return "fat";
        if (isCarb) return "carb";
        if (isDairyOrEgg || isProtein) return "protein";
        return "other";
    }

    private static bool SameRole(string role, foods food)
        => InferRole("comida", food) == role;

    private static double MealShare(string mealName, int mealsPerDay)
    {
        var name = mealName.ToLowerInvariant();
        if (mealsPerDay <= 3) return name.Contains("desayuno") ? 0.28 : name.Contains("comida") ? 0.42 : 0.30;
        if (mealsPerDay == 4) return name.Contains("desayuno") ? 0.25 : name.Contains("comida") ? 0.38 : name.Contains("merienda") ? 0.12 : 0.25;
        return name.Contains("desayuno") ? 0.22 : name.Contains("media") ? 0.10 : name.Contains("comida") ? 0.36 : name.Contains("merienda") ? 0.10 : 0.22;
    }

    private static string GetFamily(foods food)
    {
        var text = ((food.name ?? string.Empty) + " " + (food.category ?? string.Empty) + " " + (food.exchange_group?.name ?? string.Empty)).ToLowerInvariant();
        if (text.Contains("fruta") || new[] { "manzana", "pera", "naranja", "plátano", "platano", "kiwi", "fresa", "mandarina", "melón", "melon" }.Any(text.Contains)) return "fruta";
        if (text.Contains("verdura") || text.Contains("hortal") || new[] { "tomate", "lechuga", "espinaca", "brócoli", "brocoli", "calabacín", "calabacin", "zanahoria", "pimiento" }.Any(text.Contains)) return "verdura";
        if (new[] { "lenteja", "garbanzo", "alubia", "judía", "judia", "legumbre" }.Any(text.Contains)) return "legumbre";
        if (new[] { "salmón", "salmon", "atún", "atun", "merluza", "bacalao", "sardina", "caballa", "pescado", "marisco", "gamba" }.Any(text.Contains)) return "pescado_marisco";
        if (new[] { "nuez", "almendra", "avellana", "anacardo", "pistacho", "frutos secos" }.Any(text.Contains)) return "frutos_secos";
        if (new[] { "huevo", "tortilla" }.Any(text.Contains)) return "huevo";
        if (new[] { "pollo", "pavo", "ternera", "cerdo", "conejo", "carne" }.Any(text.Contains)) return "carne";
        if (new[] { "leche", "yogur", "queso", "kéfir", "kefir", "lácteo", "lacteo" }.Any(text.Contains)) return "lacteo";
        if (new[] { "arroz", "pasta", "pan", "avena", "patata", "quinoa", "cereal" }.Any(text.Contains)) return "cereal";
        return "otro";
    }

    private static (int UniqueFoods, int ReusedFoods) CalculateShoppingMetrics(DietDetailDto diet)
    {
        var counts = diet.Days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Where(i => i.FoodId.HasValue).GroupBy(i => i.FoodId!.Value).Select(g => g.Count()).ToList();
        return (counts.Count, counts.Sum(c => Math.Max(0, c - 1)));
    }
}
