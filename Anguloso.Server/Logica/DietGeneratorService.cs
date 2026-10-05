using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

public class DietGeneratorService
{
    private readonly angulosodbContext _context;
    private readonly SpecializationRulesService _specializationRulesService;

    public DietGeneratorService(angulosodbContext context, SpecializationRulesService specializationRulesService)
    {
        _context = context;
        _specializationRulesService = specializationRulesService;
    }

    // La generación construye una dieta a partir de objetivos nutricionales, alimentos permitidos
    // y restricciones del paciente; los filtros de tenant se aplican antes de optimizar las cantidades.
    public async Task<DietDetailDto> GenerateDietAsync(GenerateDietRequestDto request, int? tenantId, int userId, bool canUseTenantLocalFoods, CancellationToken cancellationToken = default)
    {
        // 1. Resolver Kcal y Macros objetivo diarios
        double targetKcal = request.TargetKcal > 0 ? request.TargetKcal : 2000;
        double targetProtein = request.TargetProtein ?? CalculateDefaultProtein(targetKcal, request.DietType);
        double targetFat = request.TargetFat ?? CalculateDefaultFat(targetKcal, request.DietType);
        double targetCarbs = request.TargetCarbs ?? CalculateDefaultCarbs(targetKcal, targetProtein, targetFat);

        // 2. Extraer exclusiones del paciente
        var exclusions = new HashSet<string>(request.ExcludedFoodKeywords.Select(k => k.ToLowerInvariant()));
        if (request.ClientId.HasValue)
        {
            var client = await _context.clients
                .Include(c => c.digestive_health)
                .Include(c => c.food_preferences)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.id == request.ClientId.Value && c.archived_at == null && (!tenantId.HasValue || c.tenant_id == tenantId.Value), cancellationToken);

            if (client != null)
            {
                if (client.digestive_health?.gluten_intolerance == true)
                    AddGlutenKeywords(exclusions);
                if (client.digestive_health?.lactose_intolerance == true)
                    AddLactoseKeywords(exclusions);
                if (client.digestive_health?.fodmaps_intolerance == true)
                    AddFodmapKeywords(exclusions);
                if (tenantId.HasValue)
            {
                var specializationExclusions = await _specializationRulesService.GetFoodExclusionsAsync(
                    client.id, tenantId.Value, cancellationToken);
                foreach (var keyword in specializationExclusions)
                    exclusions.Add(keyword);
            }

            if (client.food_preferences != null && !string.IsNullOrWhiteSpace(client.food_preferences.allergies))
                {
                    var customAllergies = client.food_preferences.allergies
                        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(a => a.Trim().ToLowerInvariant())
                        .Where(a => a.Length > 2);
                    foreach (var a in customAllergies) exclusions.Add(a);
                }
            }
        }

        // 3. Cargar un catálogo acotado de alimentos desde la BD.
        // No debemos traer toda la tabla a memoria: el catálogo puede crecer mucho
        // con las sincronizaciones de USDA/OpenFoodFacts y bloquear la generación.
        const int maxFoodsToLoad = 5000;
        var allFoods = await _context.foods
            .AsNoTracking()
            .Where(f => f.kcal.HasValue && f.kcal > 0 && f.name != null &&
                ((f.source == null || f.source.ToLower() != "local") ||
                 UserCanUseTenantLocalFood(f, tenantId, userId, canUseTenantLocalFoods)))
            .OrderBy(f => f.id)
            .Take(maxFoodsToLoad)
            .ToListAsync(cancellationToken);

        // Filtrar alimentos válidos (comunes y no excluidos)
        var allowedFoods = allFoods.Where(f => IsCommonFood(f) && !IsExcluded(f, exclusions)).ToList();
        if (allowedFoods.Count < 20)
        {
            // Fallback si la lista común es muy restrictiva
            allowedFoods = allFoods.Where(f => !IsExcluded(f, exclusions)).ToList();
        }

        cancellationToken.ThrowIfCancellationRequested();

        // La generación separa el catálogo en grupos antes de construir las comidas para poder
        // buscar candidatos adecuados a cada franja y repartir el uso de alimentos entre días.
        var foodPools = CategorizeFoods(allowedFoods, request.DietType);
        var mealSplits = GetMealSplits(request.MealsPerDay);

        var weeklyUsageCount = new Dictionary<int, int>();
        var daysList = new List<DietDayDto>();
        var rnd = new Random();

        for (int dayIndex = 0; dayIndex < request.NumberOfDays; dayIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var todayMeals = new List<MealDto>();
            var dayUsedFoodIds = new HashSet<int>();

            for (int mIndex = 0; mIndex < mealSplits.Count; mIndex++)
            {
                var split = mealSplits[mIndex];
                double mealTargetKcal = targetKcal * split.KcalPct;
                double mealTargetP = targetProtein * split.KcalPct;
                double mealTargetC = targetCarbs * split.KcalPct;
                double mealTargetF = targetFat * split.KcalPct;

                var mealItems = BuildAndOptimizeMeal(
                    split.MealName, 
                    mealTargetKcal, 
                    mealTargetP, 
                    mealTargetC, 
                    mealTargetF, 
                    foodPools, 
                    weeklyUsageCount, 
                    dayUsedFoodIds, 
                    rnd
                );

                todayMeals.Add(new MealDto
                {
                    Name = split.MealName,
                    MealIndex = mIndex,
                    Items = mealItems
                });
            }

            // Normalización final del día para calibración exacta de Kcal y Macros
            NormalizeDay(todayMeals, targetKcal, targetProtein, targetCarbs, targetFat);

            daysList.Add(new DietDayDto
            {
                DayIndex = dayIndex,
                Meals = todayMeals
            });
        }

        return new DietDetailDto
        {
            Name = $"Plan {request.DietType} ({Math.Round(targetKcal)} kcal)",
            TargetKcal = (decimal)Math.Round(targetKcal),
            TargetProtein = (decimal)Math.Round(targetProtein),
            TargetCarbs = (decimal)Math.Round(targetCarbs),
            TargetFat = (decimal)Math.Round(targetFat),
            Notes = $"Plan generado automáticamente por el motor heurístico el {DateTime.Now:dd/MM/yyyy}. {request.MealsPerDay} comidas al día.",
            Days = daysList
        };
    }

    private static bool UserCanUseTenantLocalFood(foods food, int? tenantId, int userId, bool canUseTenantLocalFoods)
    {
        if (!tenantId.HasValue || food.tenant_id != tenantId.Value) return false;
        return canUseTenantLocalFoods || food.created_by_user_id == userId;
    }

    #region Filtro de Alimentos Comunes y Culinarios

    private static readonly HashSet<string> RareKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "grasa", "sebo", "manteca", "harina de soja", "harina de trigo sarraceno", "concentrado", "aislado",
        "suplemento", "espesante", "edulcorante", "almidón", "fécula", "salsa de soja", "pastilla de caldo",
        "cubito", "gelatina en polvo", "levadura", "licor", "bebida alcohólica", "vino", "cerveza", "vodka",
        "anís", "aguardiente", "oporto", "brandy", "whisky", "ron", "ginebra", "sidra", "sirope", "jarabe",
        "snack", "gusanito", "patatas fritas de bolsa", "palomitas", "gofre", "croissant", "donut", "bollería",
        "chuchería", "caramelo", "chicle", "malvavisco", "bombón", "chocolate blanco", "cacao en polvo azucarado"
    };

    private bool IsCommonFood(foods f)
    {
        var name = (f.name ?? "").ToLowerInvariant();
        var cat = (f.category ?? "").ToLowerInvariant();

        // Evitar alimentos extremadamente procesados o ingredientes no culinarios
        if (RareKeywords.Any(k => name.Contains(k) || cat.Contains(k)))
            return false;

        // Evitar nombres con códigos o rarezas industriales
        if (name.Length > 80 || name.Contains("deshidratado") || name.Contains("liofilizado") || name.Contains("polvo"))
            return false;

        return true;
    }

    #endregion

    #region Algoritmo de Selección y Optimización Matemática

    // Selecciona alimentos compatibles con la comida y reparte la cantidad objetivo mediante OptimizeGrams.
    // La aleatoriedad favorece variedad entre candidatos con puntuaciones similares.
    private List<MealItemDto> BuildAndOptimizeMeal(
        string mealName, 
        double targetKcal, 
        double targetP, 
        double targetC, 
        double targetF, 
        FoodPools pools, 
        Dictionary<int, int> weeklyUsage, 
        HashSet<int> dayUsage, 
        Random rnd)
    {
        var slots = GetTemplateForMeal(mealName);
        var chosenFoods = new List<(foods food, SlotConfig slot)>();

        foreach (var slot in slots)
        {
            var candidates = pools.GetFoodsForSlot(slot.Role);
            if (!candidates.Any()) continue;

            // Puntuación de variedad
            var scoredCandidates = candidates
                .Select(f => new
                {
                    Food = f,
                    Score = (dayUsage.Contains(f.id) ? 2000 : 0) + (weeklyUsage.GetValueOrDefault(f.id, 0) * 20) + rnd.Next(0, 10)
                })
                .OrderBy(x => x.Score)
                .Take(6)
                .ToList();

            if (scoredCandidates.Any())
            {
                var pick = scoredCandidates[rnd.Next(scoredCandidates.Count)].Food;
                chosenFoods.Add((pick, slot));
                dayUsage.Add(pick.id);
                weeklyUsage[pick.id] = weeklyUsage.GetValueOrDefault(pick.id, 0) + 1;
            }
        }

        if (!chosenFoods.Any()) return new List<MealItemDto>();

        // Resolver gramos por optimización cuadrática acotada
        var grams = OptimizeGrams(chosenFoods, targetKcal, targetP, targetC, targetF);

        var result = new List<MealItemDto>();
        for (int i = 0; i < chosenFoods.Count; i++)
        {
            var f = chosenFoods[i].food;
            double g = Math.Round(grams[i], 0);
            double ratio = g / 100.0;

            result.Add(new MealItemDto
            {
                FoodId = f.id,
                FoodName = f.name,
                Grams = (decimal)g,
                Kcal = (decimal)Math.Round((f.kcal ?? 0) * ratio, 1),
                Protein = (decimal)Math.Round((f.protein ?? 0) * ratio, 1),
                Carbs = (decimal)Math.Round((f.carbs ?? 0) * ratio, 1),
                Fat = (decimal)Math.Round((f.fat ?? 0) * ratio, 1)
            });
        }

        return result;
    }

    // Ajuste iterativo acotado: reduce el error de kcal y macronutrientes respetando los límites de cada alimento.
    // Es una heurística, no un solver matemático exacto; por eso se acota también el número de iteraciones.
    // Ajusta las cantidades de los alimentos buscando acercarse simultáneamente a energía y
    // macronutrientes objetivo sin sustituir las restricciones ya aplicadas a la selección.
    private double[] OptimizeGrams(List<(foods food, SlotConfig slot)> items, double tKcal, double tP, double tC, double tF)
    {
        int n = items.Count;
        double[] grams = items.Select(x => x.slot.DefaultGrams).ToArray();

        // 80 iteraciones de ajuste acotado
        for (int iter = 0; iter < 80; iter++)
        {
            double curKcal = 0, curP = 0, curC = 0, curF = 0;
            for (int i = 0; i < n; i++)
            {
                double r = grams[i] / 100.0;
                curKcal += (items[i].food.kcal ?? 0) * r;
                curP += (items[i].food.protein ?? 0) * r;
                curC += (items[i].food.carbs ?? 0) * r;
                curF += (items[i].food.fat ?? 0) * r;
            }

            double errKcal = tKcal - curKcal;
            double errP = tP - curP;
            double errC = tC - curC;
            double errF = tF - curF;

            for (int i = 0; i < n; i++)
            {
                var f = items[i].food;
                var s = items[i].slot;

                double step = 0;
                if (s.Role == "Protein" || s.Role == "DairyOrEgg" || s.Role == "SnackProtein")
                {
                    double p100 = f.protein ?? 1;
                    step = (errP / Math.Max(p100, 5)) * 15.0;
                }
                else if (s.Role == "Carbs" || s.Role == "BreakfastCarb" || s.Role == "LightCarb")
                {
                    double c100 = f.carbs ?? 1;
                    step = (errC / Math.Max(c100, 5)) * 15.0;
                }
                else if (s.Role == "Oil")
                {
                    double f100 = f.fat ?? 1;
                    step = (errF / Math.Max(f100, 10)) * 6.0;
                }
                else
                {
                    step = (errKcal / Math.Max(f.kcal ?? 50, 20)) * 8.0;
                }

                grams[i] = Math.Clamp(grams[i] + step, s.MinGrams, s.MaxGrams);
            }
        }

        return grams;
    }

    // Hace una segunda pasada sobre el día generado para corregir desviaciones acumuladas entre
    // comidas y dejar los totales dentro de la tolerancia esperada por el generador.
    private void NormalizeDay(List<MealDto> meals, double targetKcal, double targetP, double targetC, double targetF)
    {
        var allItems = meals.SelectMany(m => m.Items).ToList();
        if (!allItems.Any()) return;

        double totalKcal = (double)allItems.Sum(i => i.Kcal ?? 0);
        if (totalKcal <= 0) return;

        double factor = targetKcal / totalKcal;

        // Solo ajustar si la desviación es notable (> 3%)
        if (Math.Abs(factor - 1.0) > 0.03)
        {
            foreach (var item in allItems)
            {
                double oldGrams = (double)(item.Grams ?? 100);
                double newGrams = Math.Round(oldGrams * factor, 0);

                // Evitar porciones irrisorias o gigantescas
                newGrams = Math.Clamp(newGrams, 5, 350);

                double ratio = newGrams / Math.Max(oldGrams, 1);
                item.Grams = (decimal)newGrams;
                item.Kcal = (decimal)Math.Round((double)(item.Kcal ?? 0) * ratio, 1);
                item.Protein = (decimal)Math.Round((double)(item.Protein ?? 0) * ratio, 1);
                item.Carbs = (decimal)Math.Round((double)(item.Carbs ?? 0) * ratio, 1);
                item.Fat = (decimal)Math.Round((double)(item.Fat ?? 0) * ratio, 1);
            }
        }
    }

    #endregion

    #region Plantillas de Comidas y Distribución

    private class MealSplit
    {
        public string MealName { get; set; } = string.Empty;
        public double KcalPct { get; set; }
    }

    private List<MealSplit> GetMealSplits(int mealsCount)
    {
        return mealsCount switch
        {
            3 => new List<MealSplit>
            {
                new() { MealName = "Desayuno", KcalPct = 0.28 },
                new() { MealName = "Comida", KcalPct = 0.42 },
                new() { MealName = "Cena", KcalPct = 0.30 }
            },
            4 => new List<MealSplit>
            {
                new() { MealName = "Desayuno", KcalPct = 0.25 },
                new() { MealName = "Comida", KcalPct = 0.38 },
                new() { MealName = "Merienda", KcalPct = 0.12 },
                new() { MealName = "Cena", KcalPct = 0.25 }
            },
            _ => new List<MealSplit> // 5 comidas
            {
                new() { MealName = "Desayuno", KcalPct = 0.22 },
                new() { MealName = "Media Mañana", KcalPct = 0.10 },
                new() { MealName = "Comida", KcalPct = 0.36 },
                new() { MealName = "Merienda", KcalPct = 0.10 },
                new() { MealName = "Cena", KcalPct = 0.22 }
            }
        };
    }

    private class SlotConfig
    {
        public string Role { get; set; } = string.Empty;
        public double MinGrams { get; set; }
        public double MaxGrams { get; set; }
        public double DefaultGrams { get; set; }
    }

    private List<SlotConfig> GetTemplateForMeal(string mealName)
    {
        var m = mealName.ToLowerInvariant();
        if (m.Contains("desayuno"))
        {
            return new List<SlotConfig>
            {
                new() { Role = "BreakfastCarb", MinGrams = 30, MaxGrams = 80, DefaultGrams = 50 },
                new() { Role = "DairyOrEgg", MinGrams = 80, MaxGrams = 200, DefaultGrams = 125 },
                new() { Role = "Fruit", MinGrams = 80, MaxGrams = 180, DefaultGrams = 120 }
            };
        }
        if (m.Contains("media") || m.Contains("merienda"))
        {
            return new List<SlotConfig>
            {
                new() { Role = "SnackProtein", MinGrams = 80, MaxGrams = 180, DefaultGrams = 125 },
                new() { Role = "FruitOrNuts", MinGrams = 15, MaxGrams = 120, DefaultGrams = 30 }
            };
        }
        if (m.Contains("cena"))
        {
            return new List<SlotConfig>
            {
                new() { Role = "Protein", MinGrams = 100, MaxGrams = 200, DefaultGrams = 130 },
                new() { Role = "Vegetable", MinGrams = 100, MaxGrams = 250, DefaultGrams = 150 },
                new() { Role = "LightCarb", MinGrams = 30, MaxGrams = 120, DefaultGrams = 60 },
                new() { Role = "Oil", MinGrams = 5, MaxGrams = 15, DefaultGrams = 10 }
            };
        }

        // Comida Principal
        return new List<SlotConfig>
        {
            new() { Role = "Protein", MinGrams = 110, MaxGrams = 220, DefaultGrams = 140 },
            new() { Role = "Carbs", MinGrams = 50, MaxGrams = 180, DefaultGrams = 100 },
            new() { Role = "Vegetable", MinGrams = 80, MaxGrams = 200, DefaultGrams = 120 },
            new() { Role = "Oil", MinGrams = 5, MaxGrams = 18, DefaultGrams = 10 }
        };
    }

    #endregion

    #region Clasificación de Pools de Alimentos

    private class FoodPools
    {
        public List<foods> Proteins { get; set; } = new();
        public List<foods> Carbs { get; set; } = new();
        public List<foods> Vegetables { get; set; } = new();
        public List<foods> Fruits { get; set; } = new();
        public List<foods> DairyAndEggs { get; set; } = new();
        public List<foods> BreakfastCarbs { get; set; } = new();
        public List<foods> OilsAndFats { get; set; } = new();
        public List<foods> Nuts { get; set; } = new();

        public List<foods> GetFoodsForSlot(string role)
        {
            return role switch
            {
                "Protein" => Proteins,
                "Carbs" => Carbs,
                "LightCarb" => Carbs.Where(c => (c.kcal ?? 0) < 200).DefaultIfEmpty(Carbs.FirstOrDefault()).Where(x => x != null).ToList()!,
                "Vegetable" => Vegetables,
                "Fruit" => Fruits,
                "DairyOrEgg" => DairyAndEggs,
                "BreakfastCarb" => BreakfastCarbs,
                "SnackProtein" => DairyAndEggs.Concat(Proteins).ToList(),
                "FruitOrNuts" => Fruits.Concat(Nuts).ToList(),
                "Oil" => OilsAndFats,
                "Fat" => OilsAndFats.Concat(Nuts).ToList(),
                _ => Proteins
            };
        }
    }

    // Clasifica el catálogo en pools funcionales. Las exclusiones por dieta se aplican antes de clasificar
    // y los fallbacks evitan que una categoría vacía deje al generador sin opciones.
    private FoodPools CategorizeFoods(List<foods> all, string dietType)
    {
        var pools = new FoodPools();
        bool isVeg   = dietType.Equals("Vegetariana", StringComparison.OrdinalIgnoreCase);
        bool isVegan = dietType.Equals("Vegana",      StringComparison.OrdinalIgnoreCase);
        bool isKeto  = dietType.Equals("Cetogenica",  StringComparison.OrdinalIgnoreCase);

        foreach (var f in all)
        {
            var name = (f.name ?? "").ToLowerInvariant();
            var cat = (f.category ?? "").ToLowerInvariant();
            double p = f.protein ?? 0;
            double c = f.carbs ?? 0;
            double fat = f.fat ?? 0;
            double kcal = f.kcal ?? 0;

            // Vegetariana y Vegana: excluyen carne y pescado
            if ((isVeg || isVegan) && (
                cat.Contains("carne") || cat.Contains("pescado") ||
                name.Contains("pollo") || name.Contains("ternera") || name.Contains("atún") ||
                name.Contains("salmon") || name.Contains("pavo") || name.Contains("merluza")))
                continue;

            // Vegana: además excluye lácteos y huevos
            if (isVegan && (
                name.Contains("huevo") || name.Contains("clara") ||
                name.Contains("yogur") || name.Contains("queso") || name.Contains("leche") || name.Contains("kéfir") ||
                cat.Contains("lácteo") || cat.Contains("lacteo") || cat.Contains("huevo")))
                continue;

            // Cetogénica: excluye carbohidratos altos (>10g/100g) y frutas azucaradas
            if (isKeto && (
                name.Contains("arroz") || name.Contains("pasta") || name.Contains("patata") || name.Contains("pan") ||
                name.Contains("avena") || name.Contains("copos") || name.Contains("macarrones") || name.Contains("espaguetis") ||
                name.Contains("lenteja") || name.Contains("garbanzo") || name.Contains("alubia") || name.Contains("quinoa") ||
                name.Contains("plátano") || name.Contains("platano") || name.Contains("uva") || name.Contains("mango") || name.Contains("cereza") ||
                (c > 10 && c > fat)))
                continue;

            // Aceites y Grasas (Priorizar Aceite de Oliva)
            if (name.Contains("aceite de oliva") || name.Contains("aceite de girasol"))
            {
                pools.OilsAndFats.Add(f);
            }
            // Frutos secos
            else if (name.Contains("nuez") || name.Contains("nueces") || name.Contains("almendra") || name.Contains("avellana") || name.Contains("anacardo") || name.Contains("pistacho") || cat.Contains("frutos secos"))
            {
                pools.Nuts.Add(f);
            }
            // Verduras y Hortalizas comunes
            else if (cat.Contains("verdura") || cat.Contains("hortaliz") || name.Contains("tomate") || name.Contains("lechuga") || name.Contains("espinaca") || name.Contains("calabacín") || name.Contains("berenjena") || name.Contains("brócoli") || name.Contains("zanahoria") || name.Contains("pepino") || name.Contains("pimiento") || name.Contains("judías verdes") || name.Contains("champiñon"))
            {
                pools.Vegetables.Add(f);
            }
            // Frutas comunes
            else if (cat.Contains("fruta") || name.Contains("manzana") || name.Contains("plátano") || name.Contains("platano") || name.Contains("naranja") || name.Contains("pera") || name.Contains("fresa") || name.Contains("kiwi") || name.Contains("mandarina") || name.Contains("sandía") || name.Contains("melón") || name.Contains("melocotón"))
            {
                pools.Fruits.Add(f);
            }
            // Desayuno (Panes, Avena, Tostadas)
            else if (name.Contains("avena") || name.Contains("pan integral") || name.Contains("pan de molde") || name.Contains("pan blanco") || name.Contains("tostada") || name.Contains("copos"))
            {
                pools.BreakfastCarbs.Add(f);
            }
            // Lácteos y Huevos comunes
            else if (name.Contains("huevo") || name.Contains("clara") || name.Contains("yogur") || name.Contains("queso fresco") || name.Contains("queso batido") || name.Contains("leche desnatada") || name.Contains("leche semidesnatada") || name.Contains("leche entera") || name.Contains("kéfir"))
            {
                pools.DairyAndEggs.Add(f);
            }
            // Proteínas Principales Magras/Pescados
            else if (name.Contains("pechuga") || name.Contains("pollo") || name.Contains("pavo") || name.Contains("lomo") || name.Contains("ternera") || name.Contains("atún") || name.Contains("salmon") || name.Contains("merluza") || name.Contains("bacalao") || name.Contains("dorada") || name.Contains("lubina") || name.Contains("gambas") || name.Contains("tofu") || (p >= 16 && p > c && fat < 18))
            {
                pools.Proteins.Add(f);
            }
            // Carbohidratos Base Comunes
            else if (name.Contains("arroz") || name.Contains("pasta") || name.Contains("patata") || name.Contains("macarrones") || name.Contains("espaguetis") || name.Contains("lenteja") || name.Contains("garbanzo") || name.Contains("alubia") || name.Contains("quinoa") || (c >= 18 && c > p))
            {
                pools.Carbs.Add(f);
            }
        }

        // Fallbacks
        if (!pools.OilsAndFats.Any()) pools.OilsAndFats = all.Where(f => (f.fat ?? 0) > 60).Take(5).ToList();
        if (!pools.BreakfastCarbs.Any()) pools.BreakfastCarbs = pools.Carbs;
        if (!pools.DairyAndEggs.Any()) pools.DairyAndEggs = pools.Proteins;

        return pools;
    }

    #endregion

    #region Helpers de Cálculo y Restricciones

    private double CalculateDefaultProtein(double kcal, string dietType)
    {
        double pct = dietType switch
        {
            "AltaProteina" => 0.30,
            "BajaCarbos"   => 0.28,
            "Cetogenica"   => 0.25, // moderada para evitar gluconeogénesis
            _              => 0.24  // Equilibrada / Vegetariana / Vegana (~120g para 2000 kcal)
        };
        return (kcal * pct) / 4.0;
    }

    private double CalculateDefaultFat(double kcal, string dietType)
    {
        double pct = dietType switch
        {
            "Cetogenica"   => 0.70, // >70% grasa para mantener cetosis
            "BajaCarbos"   => 0.40,
            "AltaProteina" => 0.25,
            _              => 0.26  // Equilibrada / Vegetariana / Vegana (~58g para 2000 kcal)
        };
        return (kcal * pct) / 9.0;
    }

    private double CalculateDefaultCarbs(double kcal, double proteinGrams, double fatGrams)
    {
        double usedKcal = (proteinGrams * 4.0) + (fatGrams * 9.0);
        return Math.Max(0, (kcal - usedKcal) / 4.0);
    }

    private bool IsExcluded(foods food, HashSet<string> exclusions)
    {
        if (!exclusions.Any()) return false;
        var name = (food.name ?? "").ToLowerInvariant();
        var cat = (food.category ?? "").ToLowerInvariant();
        return exclusions.Any(e => name.Contains(e) || cat.Contains(e));
    }

    private void AddGlutenKeywords(HashSet<string> ex)
    {
        foreach (var k in new[] { "trigo", "centeno", "cebada", "avena", "gluten", "pan", "pasta", "harina", "espelta", "sémola" })
            ex.Add(k);
    }

    private void AddLactoseKeywords(HashSet<string> ex)
    {
        foreach (var k in new[] { "leche", "queso", "yogur", "lact", "nata", "mantequilla", "cuajada", "suero", "kefir" })
            ex.Add(k);
    }

    private void AddFodmapKeywords(HashSet<string> ex)
    {
        foreach (var k in new[] { "ajo", "cebolla", "manzana", "pera", "alcachofa", "legumbre", "garbanzo", "lenteja", "alubia", "miel" })
            ex.Add(k);
    }

    #endregion
}
