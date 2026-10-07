using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

public sealed class DietSemanticValidationService
{
    private readonly DietValidationService _dietValidationService;

    public DietSemanticValidationService(DietValidationService dietValidationService)
    {
        _dietValidationService = dietValidationService;
    }

    /// <summary>
    /// Última barrera del generador. Una dieta no se acepta aunque sus macros cuadren
    /// si su estructura, porciones, variedad, alimentos o recetas son semánticamente incoherentes.
    /// </summary>
    public async Task ValidateOrThrowAsync(
        DietDetailDto diet,
        GenerateDietRequestDto request,
        IReadOnlyCollection<foods> allowedFoods,
        angulosodbContext context,
        int? tenantId = null,
        int userId = 0,
        bool canUseTenantLocalFoods = false,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateStructure(diet, request, allowedFoods);
        var foodMap = allowedFoods.GroupBy(f => f.id).ToDictionary(g => g.Key, g => g.First());

        var recipeIds = diet.Days.SelectMany(d => d.Meals).SelectMany(m => m.Items)
            .Where(i => i.RecipeId.HasValue).Select(i => i.RecipeId!.Value).Distinct().ToArray();

        if (recipeIds.Length > 0)
        {
            var recipes = await context.recipes.AsNoTracking().Include(r => r.recipe_items).ThenInclude(i => i.food)
                .Where(r => recipeIds.Contains(r.id)).ToDictionaryAsync(r => r.id, cancellationToken);

            foreach (var item in diet.Days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Where(i => i.RecipeId.HasValue))
            {
                if (!recipes.TryGetValue(item.RecipeId!.Value, out var recipe))
                {
                    errors.Add($"La receta {item.RecipeId.Value} no existe.");
                    continue;
                }

                if (recipe.recipe_items.Count < 2)
                    errors.Add($"La receta «{recipe.name}» no tiene una estructura culinaria suficiente.");

                foreach (var ingredient in recipe.recipe_items)
                {
                    if (ingredient.food == null || ingredient.grams <= 0)
                        errors.Add($"La receta «{recipe.name}» contiene un ingrediente inválido.");
                    else if (!foodMap.ContainsKey(ingredient.food.id) || !IsProfessionalFood(ingredient.food))
                        errors.Add($"La receta «{recipe.name}» contiene un alimento no permitido por el motor.");
                }
            }
        }

        var weeklyFoodUses = new Dictionary<int, int>();
        var weeklySignatures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var day in diet.Days)
        foreach (var meal in day.Meals)
        {
            var signature = string.Join("|", meal.Items.Where(i => i.FoodId.HasValue)
                .Select(i => i.FoodId!.Value).OrderBy(id => id));
            if (!string.IsNullOrWhiteSpace(signature))
                weeklySignatures[signature] = weeklySignatures.GetValueOrDefault(signature) + 1;

            foreach (var item in meal.Items.Where(i => i.FoodId.HasValue))
            {
                if (!foodMap.TryGetValue(item.FoodId!.Value, out var food))
                {
                    errors.Add($"Día {day.DayIndex + 1}, {meal.Name}: alimento {item.FoodId.Value} no pertenece al catálogo permitido.");
                    continue;
                }

                weeklyFoodUses[item.FoodId.Value] = weeklyFoodUses.GetValueOrDefault(item.FoodId.Value) + 1;
                ValidateFoodItem(errors, day.DayIndex, meal, item, food);
            }

            ValidateMealSemantics(errors, day.DayIndex, meal, request.DietType);
        }

        foreach (var pair in weeklyFoodUses)
        {
            if (!foodMap.TryGetValue(pair.Key, out var food) || IsStapleFood(food)) continue;
            if (pair.Value > Math.Max(1, request.MaxWeeklyFoodRepetitions))
                errors.Add($"El alimento «{food.name}» aparece {pair.Value} veces en la semana; supera el máximo de repetición configurado.");
        }

        foreach (var pair in weeklySignatures.Where(p => p.Value > 3))
            errors.Add($"La misma combinación de alimentos se repite {pair.Value} veces en la semana; la variedad culinaria mínima no se cumple.");

        ValidateDailyNutrition(errors, diet, request);
        ValidateDietTypeSemantics(errors, diet, request.DietType);
        await ValidatePatientTherapySemanticsAsync(errors, diet, request, context, cancellationToken);

        // La barrera semántica final también consolida las incompatibilidades clínicas
        // de la misma forma que la validación de borradores. Así ninguna optimización,
        // receta o sustitución puede sortear alergias, intolerancias, especializaciones
        // o interacciones farmacológicas justo antes de aceptar el plan.
        if (request.ClientId.HasValue)
        {
            var compatibilityWarnings = await _dietValidationService.ValidateDietDraftCompatibilityAsync(
                request.ClientId.Value,
                diet,
                context,
                tenantId,
                userId,
                canUseTenantLocalFoods);

            foreach (var warning in compatibilityWarnings.Where(w =>
                string.Equals(w.Severity, "High", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Incompatibilidad clínica de alta severidad: {warning.Message}");
            }
        }

        if (errors.Count > 0)
        {
            var message = new StringBuilder("El generador rechazó la dieta porque no supera la validación semántica final. ");
            message.Append(string.Join(" | ", errors.Take(12)));
            if (errors.Count > 12) message.Append($" | ... y {errors.Count - 12} errores más.");
            throw new InvalidOperationException(message.ToString());
        }
    }

    private static List<string> ValidateStructure(DietDetailDto diet, GenerateDietRequestDto request, IReadOnlyCollection<foods> allowedFoods)
    {
        var errors = new List<string>();
        if (request.NumberOfDays is < 1 or > 31) errors.Add("El número de días debe estar entre 1 y 31.");
        if (request.MealsPerDay is < 3 or > 5) errors.Add("El generador solo admite entre 3 y 5 comidas al día.");
        if (diet.Days.Count != request.NumberOfDays) errors.Add($"La dieta contiene {diet.Days.Count} días y se solicitaron {request.NumberOfDays}.");
        if (request.TargetKcal > 0 && (request.TargetKcal < 1200 || request.TargetKcal > 4500)) errors.Add("El objetivo energético queda fuera del rango operativo del generador (1200–4500 kcal/día).");
        if (allowedFoods.Count == 0) errors.Add("El catálogo permitido está vacío.");
        return errors;
    }

    private static void ValidateMealSemantics(List<string> errors, int dayIndex, MealDto meal, string dietType)
    {
        var name = Normalize(meal.Name);
        var items = meal.Items.Where(i => (i.Grams ?? 0) > 0).ToList();
        if (items.Count == 0) { errors.Add($"Día {dayIndex + 1}, {meal.Name}: comida vacía."); return; }

        if (items.GroupBy(i => i.FoodId ?? -i.RecipeId.GetValueOrDefault()).Any(g => g.Key != 0 && g.Count() > 1))
            errors.Add($"Día {dayIndex + 1}, {meal.Name}: un mismo componente aparece duplicado.");

        var small = name.Contains("desayuno") || name.Contains("media") || name.Contains("merienda");
        if (small)
        {
            if (items.Any(i => i.FoodId.HasValue && IsMainProteinName(i.FoodName)))
                errors.Add($"Día {dayIndex + 1}, {meal.Name}: una ingesta pequeña contiene carne o pescado como proteína principal.");
            if (items.Any(i => i.FoodId.HasValue && IsLegumeName(i.FoodName)))
                errors.Add($"Día {dayIndex + 1}, {meal.Name}: las legumbres no pueden sustituir al carbohidrato de una ingesta pequeña.");
            if (!items.Any(i => i.FoodId.HasValue && (IsBreakfastCarb(i.FoodName) || IsSnackProtein(i.FoodName) || IsNut(i.FoodName))))
                errors.Add($"Día {dayIndex + 1}, {meal.Name}: falta un componente principal de desayuno/colación.");
            return;
        }

        var type = Normalize(dietType);
        var keto = type.Contains("cetogen");
        var lowCarb = type.Contains("baja") && type.Contains("carb");
        var hasProtein = items.Any(i => i.FoodId.HasValue && (IsMainProteinName(i.FoodName) || IsEgg(i.FoodName) || IsLegumeName(i.FoodName)));
        var hasVegetable = items.Any(i => i.FoodId.HasValue && IsVegetable(i.FoodName));
        var hasCarb = items.Any(i => i.FoodId.HasValue && IsCarb(i.FoodName));
        var hasFat = items.Any(i => i.FoodId.HasValue && (IsOil(i.FoodName) || IsNut(i.FoodName) || IsFattyProtein(i.FoodName)));

        if (!hasProtein) errors.Add($"Día {dayIndex + 1}, {meal.Name}: comida principal sin fuente proteica reconocible.");
        if (!hasVegetable) errors.Add($"Día {dayIndex + 1}, {meal.Name}: comida principal sin verdura/hortaliza reconocible.");
        if (!keto && !lowCarb && !hasCarb) errors.Add($"Día {dayIndex + 1}, {meal.Name}: comida principal sin fuente de hidratos reconocible.");
        if ((keto || lowCarb) && !hasFat) errors.Add($"Día {dayIndex + 1}, {meal.Name}: pauta baja en hidratos sin fuente de grasa reconocible.");
    }

    private static void ValidateFoodItem(List<string> errors, int dayIndex, MealDto meal, MealItemDto item, foods food)
    {
        var grams = (double)(item.Grams ?? 0);
        if (grams <= 0) { errors.Add($"Día {dayIndex + 1}, {meal.Name}: «{food.name}» tiene una cantidad no válida."); return; }
        if (!IsProfessionalFood(food)) errors.Add($"Día {dayIndex + 1}, {meal.Name}: «{food.name}» no es apto para selección automática profesional.");

        var bounds = GetCulinaryBounds(meal.Name, food.name);
        if (grams < bounds.Min || grams > bounds.Max)
            errors.Add($"Día {dayIndex + 1}, {meal.Name}: «{food.name}» está en {grams:0} g, fuera del rango culinario {bounds.Min:0}–{bounds.Max:0} g.");
        if (IsLegumeName(food.name) && IsRawOrDry(food.name))
            errors.Add($"Día {dayIndex + 1}, {meal.Name}: «{food.name}» está en formato seco/crudo.");
        if (IsHighGlycemicHeuristic(food) && IsCarb(food.name))
            errors.Add($"Día {dayIndex + 1}, {meal.Name}: «{food.name}» es una fuente de hidratos de respuesta glucémica elevada no priorizada.");
        if (IsRareOil(food.name))
            errors.Add($"Día {dayIndex + 1}, {meal.Name}: «{food.name}» es un aceite no estándar.");
    }

    private static async Task ValidatePatientTherapySemanticsAsync(
        List<string> errors,
        DietDetailDto diet,
        GenerateDietRequestDto request,
        angulosodbContext context,
        CancellationToken cancellationToken)
    {
        if (!request.ClientId.HasValue) return;

        var client = await context.clients
            .AsNoTracking()
            .Include(c => c.medical_history)
            .FirstOrDefaultAsync(c => c.id == request.ClientId.Value && c.archived_at == null, cancellationToken);

        if (client?.medical_history == null) return;

        foreach (var day in diet.Days)
        foreach (var meal in day.Meals)
        foreach (var item in meal.Items.Where(i => i.FoodId.HasValue && (i.Grams ?? 0) > 0))
        {
            var food = Normalize(item.FoodName ?? string.Empty);

            if (client.medical_history.diabetes == true &&
                new[] { "refresco", "bebida azucarada", "zumo", "caramelo", "mermelada", "azucar", "bolleria industrial" }
                    .Any(food.Contains))
            {
                errors.Add($"Día {day.DayIndex + 1}, {meal.Name}: «{item.FoodName}» no se admite como componente automático en un paciente con diabetes.");
            }

            if (client.medical_history.hypertension == true &&
                new[] { "patatas fritas", "chips", "snack salado", "sopa instantanea", "caldo concentrado" }
                    .Any(food.Contains))
            {
                errors.Add($"Día {day.DayIndex + 1}, {meal.Name}: «{item.FoodName}» no se admite como componente automático en una pauta con hipertensión.");
            }
        }
    }

    private static void ValidateDietTypeSemantics(List<string> errors, DietDetailDto diet, string dietType)
    {
        var type = Normalize(dietType);
        var vegan = type.Contains("vegana");
        var vegetarian = type.Contains("vegetariana") && !vegan;
        var keto = type.Contains("cetogen");
        var lowCarb = type.Contains("baja") && type.Contains("carb");

        foreach (var day in diet.Days)
        foreach (var meal in day.Meals)
        foreach (var item in meal.Items.Where(i => i.FoodId.HasValue && (i.Grams ?? 0) > 0))
        {
            var food = Normalize(item.FoodName ?? string.Empty);

            if ((vegan || vegetarian) && IsAnimalFood(food))
                errors.Add($"Día {day.DayIndex + 1}, {meal.Name}: «{item.FoodName}» no es compatible con una pauta {(vegan ? "vegana" : "vegetariana")}.");

            if (keto && IsKetoForbiddenCarb(food))
                errors.Add($"Día {day.DayIndex + 1}, {meal.Name}: «{item.FoodName}» es incompatible con la estructura de una pauta cetogénica.");

            if (lowCarb && IsLowCarbForbiddenStaple(food))
                errors.Add($"Día {day.DayIndex + 1}, {meal.Name}: «{item.FoodName}» es un hidrato concentrado no compatible con la pauta baja en hidratos.");

            if (food.Contains("zumo") || food.Contains("refresco") || food.Contains("bebida azucarada") ||
                food.Contains("mermelada") || food.Contains("caramelo") || food.Contains("bolleria") ||
                food.Contains("azucar"))
            {
                // Estos productos no son una prohibición universal, pero sí una señal dura
                // cuando el paciente tiene diabetes: el motor no los introduce como base automática.
                // La comprobación clínica final se aplica abajo solo si el paciente tiene diabetes.
            }
        }
    }

    private static bool IsAnimalFood(string food) =>
        new[] { "carne", "pollo", "pavo", "ternera", "cerdo", "conejo", "cordero", "pescado",
                "atun", "salmon", "merluza", "bacalao", "sardina", "caballa", "marisco", "gamba",
                "huevo", "clara", "leche", "yogur", "queso", "kefir", "mantequilla", "miel",
                "gelatina" }.Any(food.Contains);

    private static bool IsKetoForbiddenCarb(string food) =>
        new[] { "pan", "arroz", "pasta", "patata", "boniato", "avena", "cuscus", "quinoa",
                "lenteja", "garbanzo", "alubia", "guisante", "arroz inflado", "cereal",
                "platano", "uva", "mango", "cereza" }.Any(food.Contains);

    private static bool IsLowCarbForbiddenStaple(string food) =>
        new[] { "pan blanco", "pan de molde", "arroz blanco", "pasta", "macarron", "espagueti",
                "patata", "boniato", "cuscus", "arroz inflado", "cereal azucarado" }.Any(food.Contains);

    private static void ValidateDailyNutrition(List<string> errors, DietDetailDto diet, GenerateDietRequestDto request)
    {
        var kcalTarget = diet.TargetKcal.HasValue ? (double)diet.TargetKcal.Value : request.TargetKcal;
        var proteinTarget = diet.TargetProtein.HasValue ? (double)diet.TargetProtein.Value : request.TargetProtein ?? 0;
        var carbsTarget = diet.TargetCarbs.HasValue ? (double)diet.TargetCarbs.Value : request.TargetCarbs ?? 0;
        var fatTarget = diet.TargetFat.HasValue ? (double)diet.TargetFat.Value : request.TargetFat ?? 0;
        if (kcalTarget <= 0) return;

        foreach (var day in diet.Days)
        {
            var items = day.Meals.SelectMany(m => m.Items);
            var kcal = items.Sum(i => (double)(i.Kcal ?? 0));
            var protein = items.Sum(i => (double)(i.Protein ?? 0));
            var carbs = items.Sum(i => (double)(i.Carbs ?? 0));
            var fat = items.Sum(i => (double)(i.Fat ?? 0));
            if (RelativeError(kcal, kcalTarget) > .15) errors.Add($"Día {day.DayIndex + 1}: energía fuera del ±15 % del objetivo.");
            if (proteinTarget > 0 && RelativeError(protein, proteinTarget) > .20) errors.Add($"Día {day.DayIndex + 1}: proteína fuera del ±20 % del objetivo.");
            if (carbsTarget > 0 && RelativeError(carbs, carbsTarget) > .20) errors.Add($"Día {day.DayIndex + 1}: hidratos fuera del ±20 % del objetivo.");
            if (fatTarget > 0 && RelativeError(fat, fatTarget) > .20) errors.Add($"Día {day.DayIndex + 1}: grasa fuera del ±20 % del objetivo.");
        }
    }

    private static double RelativeError(double actual, double target) => target <= 0 ? 0 : Math.Abs(actual - target) / target;
    private static (double Min, double Max) GetCulinaryBounds(string mealName, string? foodName)
    {
        var meal = Normalize(mealName); var food = Normalize(foodName ?? string.Empty);
        var small = meal.Contains("desayuno") || meal.Contains("media") || meal.Contains("merienda");
        if (small)
        {
            if (food.Contains("yogur")) return (125, 125);
            if (food.Contains("leche")) return (100, 300);
            if (IsEgg(food)) return (50, 180);
            if (IsFruit(food)) return (100, 250);
            if (IsOil(food)) return (5, 18);
            return (30, 180);
        }
        if (IsOil(food)) return (5, 18);
        if (IsVegetable(food) || IsFruit(food)) return (100, 250);
        if (IsCarb(food)) return (50, 180);
        if (IsMainProteinName(food) || IsEgg(food)) return (100, 220);
        if (food.Contains("yogur")) return (125, 125);
        return (20, 250);
    }

    private static bool IsProfessionalFood(foods food)
    {
        var n = Normalize(food.name ?? string.Empty);
        var forbidden = new[] { "flan","petit suisse","natillas","pudding","mousse","postre lacteo","crema de postre","yogur liquido","yogur con ","yogur azucarado","lenteja seca","lentejas secas","garbanzo seco","garbanzos secos","alubia seca","alubias secas","huevo de pato","huevo de codorniz","huevo de pavo","huevo de gallina de guinea" };
        return !forbidden.Any(n.Contains) && !IsRareOil(n);
    }

    private static bool IsStapleFood(foods food) { var n = Normalize(food.name ?? string.Empty); return IsOil(n) || n.Contains("sal") || n.Contains("agua"); }
    private static bool IsRareOil(string? name) { var n = Normalize(name ?? string.Empty); return IsOil(n) && !n.Contains("aceite de oliva") && !n.Contains("aceite de girasol"); }
    private static bool IsOil(string? name) => Normalize(name ?? string.Empty).StartsWith("aceite ");
    private static bool IsFruit(string? name) => new[] { "manzana","naranja","pera","mandarina","platano","fresa","kiwi","melocoton","melon","sandia","uva","albaricoque","ciruela" }.Any(Normalize(name ?? string.Empty).Contains);
    private static bool IsVegetable(string? name) => new[] { "tomate","lechuga","espinaca","calabacin","berenjena","brocoli","zanahoria","pepino","pimiento","judia verde","champiñon","calabaza","cebolla","puerro","coliflor","esparrago" }.Any(Normalize(name ?? string.Empty).Contains);
    private static bool IsCarb(string? name) => new[] { "arroz","pasta","patata","boniato","pan","avena","quinoa","cuscus","tostada","cereal" }.Any(Normalize(name ?? string.Empty).Contains);
    private static bool IsLegumeName(string? name) => new[] { "lenteja","garbanzo","alubia","judia","guisante" }.Any(Normalize(name ?? string.Empty).Contains);
    private static bool IsMainProteinName(string? name) => new[] { "pollo","pavo","ternera","cerdo","conejo","atun","salmon","merluza","bacalao","dorada","lubina","sardina","caballa","gamba","tofu","tempeh" }.Any(Normalize(name ?? string.Empty).Contains);
    private static bool IsFattyProtein(string? name) => new[] { "salmon","sardina","caballa","aguacate" }.Any(Normalize(name ?? string.Empty).Contains);
    private static bool IsEgg(string? name) => Normalize(name ?? string.Empty).Contains("huevo") || Normalize(name ?? string.Empty).Contains("clara");
    private static bool IsNut(string? name) => new[] { "nuez","almendra","avellana","anacardo","pistacho" }.Any(Normalize(name ?? string.Empty).Contains);
    private static bool IsBreakfastCarb(string? name) => new[] { "avena","pan","tostada","copos","cereal" }.Any(Normalize(name ?? string.Empty).Contains);
    private static bool IsSnackProtein(string? name) => new[] { "yogur","leche","kefir","queso","huevo","clara" }.Any(Normalize(name ?? string.Empty).Contains);
    private static bool IsRawOrDry(string? name) { var n = Normalize(name ?? string.Empty); return n.Contains("seca") || n.Contains("seco") || n.Contains("cruda") || n.Contains("crudo"); }
    private static bool IsHighGlycemicHeuristic(foods food) { var n = Normalize(food.name ?? string.Empty); return n.Contains("pan blanco") || n.Contains("pan de molde blanco") || n.Contains("arroz blanco") || n.Contains("arroz inflado") || n.Contains("pure de patata") || n.Contains("patata instantanea"); }
    private static string Normalize(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray();
        return new string(chars).Normalize(System.Text.NormalizationForm.FormC);
    }
}