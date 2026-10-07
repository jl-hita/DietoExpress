using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Regeneración localizada de dietas ya creadas. La operación conserva el árbol de la
/// dieta, cambia únicamente el alcance solicitado y valida el resultado antes de persistirlo.
/// </summary>
public sealed class DietRegenerationService
{
    private readonly angulosodbContext _context;

    public DietRegenerationService(angulosodbContext context) => _context = context;

    public async Task<DietRegenerationResponseDto> RegenerateAsync(
        int dietId,
        DietRegenerationRequestDto request,
        int userId,
        int? tenantId,
        bool isSuperAdmin,
        bool canUseTenantLocalFoods,
        CancellationToken cancellationToken = default)
    {
        var response = new DietRegenerationResponseDto { DietId = dietId, Operation = request.Operation };

        if (request.Operation is not ("replace-food" or "regenerate-meal" or "regenerate-day"))
            throw new ArgumentException("La operación debe ser replace-food, regenerate-meal o regenerate-day.");

        var diet = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
            .FirstOrDefaultAsync(d => d.id == dietId && d.archived_at == null &&
                (isSuperAdmin || (tenantId.HasValue && d.tenant_id == tenantId.Value && d.user_id == userId)),
                cancellationToken);

        if (diet == null)
            throw new KeyNotFoundException("La dieta no existe o no pertenece al usuario.");

        if (request.Operation == "replace-food" && !request.MealItemId.HasValue)
            throw new ArgumentException("MealItemId es obligatorio para sustituir un alimento.");
        if (request.Operation == "replace-food" && !request.ReplacementFoodId.HasValue)
            throw new ArgumentException("ReplacementFoodId es obligatorio para sustituir un alimento.");
        if (request.Operation != "replace-food" && !request.MealId.HasValue && !request.DayId.HasValue)
            throw new ArgumentException("Debe indicarse MealId o DayId.");

        var allFoods = await _context.foods.AsNoTracking()
            .Where(f => f.kcal.HasValue && f.kcal > 0 && f.name != null &&
                ((f.source == null || f.source.ToLower() != "local") ||
                 isSuperAdmin ||
                 (tenantId.HasValue && f.tenant_id == tenantId.Value && (canUseTenantLocalFoods || f.created_by_user_id == userId))))
            .Include(f => f.exchange_group)
            .Take(5000)
            .ToListAsync(cancellationToken);

        var client = request.ClientId is int clientId
            ? await _context.clients.AsNoTracking()
                .Include(c => c.food_preferences)
                .Include(c => c.digestive_health)
                .FirstOrDefaultAsync(c => c.id == clientId && c.archived_at == null &&
                    (isSuperAdmin || (tenantId.HasValue && c.tenant_id == tenantId.Value)), cancellationToken)
            : null;

        var forbidden = BuildForbiddenTerms(client);
        var usedFoodIds = diet.diet_days.SelectMany(d => d.meals).SelectMany(m => m.meal_items)
            .Where(i => i.food_id.HasValue).Select(i => i.food_id!.Value).ToHashSet();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var before = Objective(diet);
        meals? selectedMeal = null;
        diet_days? selectedDay = null;

        if (request.Operation == "replace-food")
        {
            var item = diet.diet_days.SelectMany(d => d.meals).SelectMany(m => m.meal_items)
                .FirstOrDefault(i => i.id == request.MealItemId!.Value);
            if (item == null) throw new KeyNotFoundException("El alimento indicado no pertenece a la dieta.");

            selectedMeal = diet.diet_days.SelectMany(d => d.meals).First(m => m.meal_items.Contains(item));
            selectedDay = diet.diet_days.First(d => d.meals.Contains(selectedMeal));
            response.DayId = selectedDay.id;
            response.MealId = selectedMeal.id;
            response.MealItemId = item.id;

            var replacement = allFoods.FirstOrDefault(f => f.id == request.ReplacementFoodId.Value);
            if (replacement == null) throw new KeyNotFoundException("El alimento de sustitución no está disponible.");
            if (!IsAllowed(replacement, forbidden))
                throw new InvalidOperationException("El alimento seleccionado es incompatible con las restricciones del paciente.");

            var previous = Snapshot(item);
            if (request.PreserveWeeklyVariety && usedFoodIds.Contains(replacement.id) && replacement.id != item.food_id)
                response.Warnings.Add("El alimento ya aparece en la semana; se permite porque se solicitó una sustitución explícita.");

            Replace(item, replacement, Math.Max(5, (double)(item.grams ?? 100)));
            RebalanceMeal(selectedMeal, DietMealTarget(diet, selectedDay, selectedMeal), request.PreserveNutritionTargets);
            response.PreviousFoodName = previous.Name;
            response.NewFoodName = replacement.name;
        }
        else if (request.Operation == "regenerate-meal")
        {
            selectedMeal = diet.diet_days.SelectMany(d => d.meals).FirstOrDefault(m => m.id == request.MealId!.Value);
            if (selectedMeal == null) throw new KeyNotFoundException("La comida indicada no pertenece a la dieta.");
            selectedDay = diet.diet_days.First(d => d.meals.Contains(selectedMeal));
            response.DayId = selectedDay.id;
            response.MealId = selectedMeal.id;

            var original = selectedMeal.meal_items.Select(Snapshot).ToList();
            var mealUsed = selectedMeal.meal_items.Where(i => i.food_id.HasValue).Select(i => i.food_id!.Value).ToHashSet();
            foreach (var item in selectedMeal.meal_items)
            {
                var candidates = allFoods.Where(f => IsAllowed(f, forbidden) && IsCompatibleRole(f, item, selectedMeal.name))
                    .Where(f => !request.PreserveWeeklyVariety || !usedFoodIds.Contains(f.id) || mealUsed.Contains(f.id))
                    .OrderBy(f => CandidateScore(f, item, usedFoodIds, mealUsed))
                    .Take(20)
                    .ToList();

                if (candidates.Count == 0)
                    candidates = allFoods.Where(f => IsAllowed(f, forbidden) && IsCompatibleRole(f, item, selectedMeal.Name))
                        .OrderBy(f => CandidateScore(f, item, usedFoodIds, mealUsed)).Take(20).ToList();

                var candidate = candidates.FirstOrDefault();
                if (candidate == null) continue;
                Replace(item, candidate, Math.Max(5, (double)(item.grams ?? 100)));
                mealUsed.Add(candidate.id);
            }

            RebalanceMeal(selectedMeal, DietMealTarget(diet, selectedDay, selectedMeal), request.PreserveNutritionTargets);
            response.PreviousFoodName = string.Join(", ", original.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Take(3));
            response.NewFoodName = string.Join(", ", selectedMeal.meal_items.Select(i => i.food_id.HasValue ? allFoods.FirstOrDefault(f => f.id == i.food_id)?.name : null).Where(x => !string.IsNullOrWhiteSpace(x)).Take(3));
        }
        else
        {
            selectedDay = diet.diet_days.FirstOrDefault(d => d.id == request.DayId!.Value);
            if (selectedDay == null) throw new KeyNotFoundException("El día indicado no pertenece a la dieta.");
            response.DayId = selectedDay.id;

            foreach (var meal in selectedDay.meals)
            {
                var mealUsed = meal.meal_items.Where(i => i.food_id.HasValue).Select(i => i.food_id!.Value).ToHashSet();
                foreach (var item in meal.meal_items)
                {
                    var candidates = allFoods.Where(f => IsAllowed(f, forbidden) && IsCompatibleRole(f, item, meal.name))
                        .Where(f => !request.PreserveWeeklyVariety || !usedFoodIds.Contains(f.id) || mealUsed.Contains(f.id))
                        .OrderBy(f => CandidateScore(f, item, usedFoodIds, mealUsed)).Take(15).ToList();
                    if (candidates.Count == 0)
                        candidates = allFoods.Where(f => IsAllowed(f, forbidden) && IsCompatibleRole(f, item, meal.Name))
                            .OrderBy(f => CandidateScore(f, item, usedFoodIds, mealUsed)).Take(15).ToList();
                    var candidate = candidates.FirstOrDefault();
                    if (candidate == null) continue;
                    Replace(item, candidate, Math.Max(5, (double)(item.grams ?? 100)));
                    mealUsed.Add(candidate.id);
                }
                RebalanceMeal(meal, DietMealTarget(diet, selectedDay, meal), request.PreserveNutritionTargets);
            }
        }

        var after = Objective(diet);
        var maxAllowedWorsening = request.PreserveNutritionTargets ? 0.12m : 0.30m;
        if (after > before * (1 + maxAllowedWorsening))
        {
            await transaction.RollbackAsync(cancellationToken);
            response.Success = false;
            response.RolledBack = true;
            response.ObjectiveBefore = (decimal)Math.Round(before, 4);
            response.ObjectiveAfter = (decimal)Math.Round(after, 4);
            response.Message = "La regeneración empeoraba demasiado el equilibrio global y se ha descartado.";
            response.Warnings.Add("No se ha persistido ningún cambio.");
            return response;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        response.Success = true;
        response.ObjectiveBefore = (decimal)Math.Round(before, 4);
        response.ObjectiveAfter = (decimal)Math.Round(after, 4);
        response.KcalDelta = (decimal)Math.Round(after - before, 4);
        response.Message = request.Operation == "replace-food"
            ? "Sustitución aplicada y comida recalibrada."
            : request.Operation == "regenerate-meal"
                ? "Comida regenerada y recalibrada sin modificar el resto de la dieta."
                : "Día regenerado y recalibrado manteniendo su estructura.";

        response.PreservedConstraints.Add("Aislamiento por tenant y autorización profesional");
        response.PreservedConstraints.Add("Restricciones alimentarias del paciente");
        if (request.PreserveNutritionTargets) response.PreservedConstraints.Add("Objetivos nutricionales recalibrados");
        if (request.PreserveWeeklyVariety) response.PreservedConstraints.Add("Control de variedad semanal");
        return response;
    }

    private static HashSet<string> BuildForbiddenTerms(clients? client)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (client?.food_preferences?.allergies is string allergies)
            foreach (var x in Split(allergies)) result.Add(x);
        if (client?.digestive_health?.gluten_intolerance == true) Add(result, "gluten", "trigo", "cebada", "centeno");
        if (client?.digestive_health?.lactose_intolerance == true) Add(result, "lactosa", "leche", "nata", "suero");
        if (client?.digestive_health?.fodmaps_intolerance == true) Add(result, "fodmap", "cebolla", "ajo");
        return result;
    }

    private static IEnumerable<string> Split(string value) =>
        value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 2);

    private static void Add(HashSet<string> set, params string[] values) { foreach (var value in values) set.Add(value); }

    private static bool IsAllowed(foods food, HashSet<string> forbidden)
    {
        var text = ((food.name ?? "") + " " + (food.category ?? "") + " " + string.Join(" ", food.dietary_flags ?? Array.Empty<string>())).ToLowerInvariant();
        return !forbidden.Any(text.Contains);
    }

    private static bool IsCompatibleRole(foods food, meal_items source, string mealName)
    {
        if (source.exchange_group_id.HasValue && food.exchange_group_id == source.exchange_group_id) return true;
        var text = ((food.name ?? "") + " " + (food.category ?? "")).ToLowerInvariant();
        var sourceText = source.food_id.HasValue ? source.exchange_group_id?.ToString() ?? "" : "";
        if (mealName.Contains("desayuno", StringComparison.OrdinalIgnoreCase))
            return text.Contains("fruta") || text.Contains("pan") || text.Contains("cereal") || text.Contains("leche") || text.Contains("yogur") || text.Contains("huevo");
        if (text.Contains("verdura") || text.Contains("hortal")) return source.grams.GetValueOrDefault() > 0;
        return true;
    }

    private static double CandidateScore(foods food, meal_items source, HashSet<int> weekly, HashSet<int> mealUsed)
    {
        double Relative(double a, double b) => Math.Abs(a - b) / Math.Max(1, Math.Abs(b));
        var grams = Math.Max(5, (double)(source.grams ?? 100));
        var kcal = (food.kcal ?? 0) * grams / 100.0;
        var p = (food.protein ?? 0) * grams / 100.0;
        var c = (food.carbs ?? 0) * grams / 100.0;
        var f = (food.fat ?? 0) * grams / 100.0;
        var score = Relative(kcal, (double)(source.kcal ?? 0))
                  + Relative(p, (double)(source.protein ?? 0))
                  + Relative(c, (double)(source.carbs ?? 0))
                  + Relative(f, (double)(source.fat ?? 0));
        if (weekly.Contains(food.id)) score += 0.8;
        if (mealUsed.Contains(food.id)) score += 1.2;
        return score;
    }

    private static double DietMealTarget(diets diet, diet_days day, meals meal)
    {
        var count = Math.Max(1, day.meals.Count);
        var name = (meal.name ?? "").ToLowerInvariant();
        var share = count switch
        {
            3 => name.Contains("desayuno") ? .28 : name.Contains("comida") ? .42 : .30,
            4 => name.Contains("desayuno") ? .25 : name.Contains("comida") ? .38 : name.Contains("merienda") ? .12 : .25,
            _ => name.Contains("desayuno") ? .22 : name.Contains("media") ? .10 : name.Contains("comida") ? .36 : name.Contains("merienda") ? .10 : .22
        };
        return (double)(diet.target_kcal ?? 2000) * share;
    }

    private static void RebalanceMeal(meals meal, double targetKcal, bool preserveTargets)
    {
        if (!preserveTargets) return;
        var items = meal.meal_items.Where(i => i.grams.GetValueOrDefault() > 0).ToList();
        if (items.Count == 0) return;

        for (var pass = 0; pass < 12; pass++)
        {
            var current = Error(items, targetKcal);
            var changed = false;
            foreach (var item in items)
            {
                var grams = (double)item.grams!.Value;
                var best = grams;
                var bestError = current;
                foreach (var candidate in new[] { Math.Max(5, grams - 5), Math.Min(350, grams + 5) })
                {
                    SetRatio(item, candidate);
                    var error = Error(items, targetKcal);
                    if (error < bestError) { best = candidate; bestError = error; }
                }
                SetRatio(item, best);
                if (bestError < current) { current = bestError; changed = true; }
            }
            if (!changed) break;
        }
    }

    private static double Error(IEnumerable<meal_items> items, double targetKcal)
    {
        var actual = items.Sum(i => (double)(i.kcal ?? 0));
        return targetKcal <= 0 ? 0 : Math.Pow((actual - targetKcal) / targetKcal, 2);
    }

    private static void SetRatio(meal_items item, double grams)
    {
        var old = Math.Max(.001, (double)item.grams!.Value);
        var ratio = grams / old;
        item.grams = (decimal)Math.Round(Math.Clamp(grams, 5, 350), 0);
        item.kcal = (decimal)Math.Round((double)(item.kcal ?? 0) * ratio, 1);
        item.protein = (decimal)Math.Round((double)(item.protein ?? 0) * ratio, 1);
        item.carbs = (decimal)Math.Round((double)(item.carbs ?? 0) * ratio, 1);
        item.fat = (decimal)Math.Round((double)(item.fat ?? 0) * ratio, 1);
    }

    private static void Replace(meal_items item, foods food, double grams)
    {
        item.food_id = food.id;
        item.grams = (decimal)Math.Round(Math.Clamp(grams, 5, 350), 0);
        var ratio = (double)item.grams.Value / 100;
        item.kcal = (decimal)Math.Round((food.kcal ?? 0) * ratio, 1);
        item.protein = (decimal)Math.Round((food.protein ?? 0) * ratio, 1);
        item.carbs = (decimal)Math.Round((food.carbs ?? 0) * ratio, 1);
        item.fat = (decimal)Math.Round((food.fat ?? 0) * ratio, 1);
        item.exchange_group_id = food.exchange_group_id;
    }

    private static SnapshotDto Snapshot(meal_items item) =>
        new(item.food_id, item.kcal, item.protein, item.carbs, item.fat, null);

    private static double Objective(diets diet)
    {
        var targetKcal = (double)(diet.target_kcal ?? 2000);
        var targetP = (double)(diet.target_protein ?? 0);
        var targetC = (double)(diet.target_carbs ?? 0);
        var targetF = (double)(diet.target_fat ?? 0);
        double Relative(double actual, double target) => target <= 0 ? 0 : Math.Abs(actual - target) / target;

        var dailyErrors = diet.diet_days.Select(day =>
        {
            var items = day.meals.SelectMany(m => m.meal_items).ToList();
            return Relative(items.Sum(i => (double)(i.kcal ?? 0)), targetKcal) * .45
                 + Relative(items.Sum(i => (double)(i.protein ?? 0)), targetP) * .20
                 + Relative(items.Sum(i => (double)(i.carbs ?? 0)), targetC) * .15
                 + Relative(items.Sum(i => (double)(i.fat ?? 0)), targetF) * .20;
        });
        return dailyErrors.DefaultIfEmpty().Average();
    }

    private sealed record SnapshotDto(int? FoodId, decimal? Kcal, decimal? Protein, decimal? Carbs, decimal? Fat, string? Name);
}

