using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Anguloso.Server.Models;
using Anguloso.Server.Model;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

public class DietValidationService
{
    private static readonly string[] LactoseKeywords = new[] 
    { 
        "leche", "queso", "yogur", "lact", "nata", "mantequilla", "cuajada", "suero", 
        "milk", "cheese", "cream", "butter", "yogurt", "kefir" 
    };

    private static readonly string[] GlutenKeywords = new[] 
    { 
        "trigo", "centeno", "cebada", "avena", "gluten", "pan", "pasta", "harina", 
        "espelta", "sémola", "wheat", "rye", "barley", "oats", "flour", "bread" 
    };

    private static readonly string[] FodmapKeywords = new[] 
    { 
        "ajo", "cebolla", "manzana", "pera", "alcachofa", "legumbre", "garbanzo", "lenteja", 
        "alubia", "miel", "garlic", "onion", "apple", "pear", "artichoke", "lentils", 
        "chickpeas", "beans", "honey" 
    };

    // Diccionario de reglas de Interacción Fármaco-Alimento (IFAF)
    private static readonly List<DrugFoodRule> DrugInteractions = new()
    {
        // Anticoagulantes orales (Sintrom, Warfarina, etc.)
        new DrugFoodRule
        {
            DrugKeywords = new[] { "sintrom", "warfarina", "acenocumarol", "anticoagulante" },
            FoodKeywords = new[] { "espinaca", "acelga", "brócoli", "col", "kale", "repollo", "perejil", "endivia", "canónigo", "berro" },
            Severity = "High",
            Message = "Interacción Fármaco-Alimento (IFAF): El paciente toma anticoagulantes. El consumo elevado o irregular de vegetales ricos en Vitamina K puede alterar el INR y reducir la eficacia del tratamiento."
        },
        // Estatinas (Atorvastatina, Simvastatina) vs Pomelo
        new DrugFoodRule
        {
            DrugKeywords = new[] { "atorvastatina", "simvastatina", "lovastatina", "estatina" },
            FoodKeywords = new[] { "pomelo", "grapefruit", "zumo de pomelo" },
            Severity = "High",
            Message = "Interacción Fármaco-Alimento (IFAF): El pomelo inhibe el citocromo CYP3A4 aumentando peligrosamente la concentración de estatinas en sangre (riesgo de rabdomiólisis)."
        },
        // Antitiroideos / Levotiroxina (Eutirox)
        new DrugFoodRule
        {
            DrugKeywords = new[] { "eutirox", "levotiroxina", "tiroxina", "tiroides" },
            FoodKeywords = new[] { "soja", "tofu", "edamame", "nueces", "café" },
            Severity = "Medium",
            Message = "Interacción Fármaco-Alimento (IFAF): La soja y alimentos ricos en calcio/hierro interfieren con la absorción de levotiroxina. Debe espaciarse la toma al menos 2-4 horas."
        },
        // IMAOs (Antidepresivos) vs Tiramina
        new DrugFoodRule
        {
            DrugKeywords = new[] { "imao", "tranilcipromina", "moclobemida", "selegilina" },
            FoodKeywords = new[] { "queso curado", "queso azul", "embutido", "cerveza", "vino tinto", "arenque" },
            Severity = "High",
            Message = "Interacción Fármaco-Alimento (IFAF): Alimentos ricos en tiramina combinados con IMAOs pueden desencadenar crisis hipertensivas graves."
        },
        // IECA / ARA-II (Enalapril, Losartán) vs Exceso de Potasio
        new DrugFoodRule
        {
            DrugKeywords = new[] { "enalapril", "losartan", "ramipril", "valsartan", "candesartan", "espironolactona" },
            FoodKeywords = new[] { "suplemento de potasio", "sal potásica", "plátano deshidratado" },
            Severity = "Medium",
            Message = "Interacción Fármaco-Alimento (IFAF): Fármacos ahorradores de potasio o IECAs. Vigilar el consumo excesivo de sustitutos de sal con potasio (riesgo de hiperpotasemia)."
        },
        // Metformina vs Alcohol
        new DrugFoodRule
        {
            DrugKeywords = new[] { "metformina", "dianben" },
            FoodKeywords = new[] { "vino", "cerveza", "licor", "alcohol", "vodka", "ron", "ginebra", "whisky" },
            Severity = "High",
            Message = "Interacción Fármaco-Alimento (IFAF): El consumo de alcohol junto a metformina potencia el riesgo de hipoglucemia y acidosis láctica."
        },
        // Antibióticos (Tetraciclinas / Quinolonas) vs Calcio y Lácteos
        new DrugFoodRule
        {
            DrugKeywords = new[] { "doxiciclina", "tetraciclina", "ciprofloxacino", "levofloxacino", "norfloxacino" },
            FoodKeywords = new[] { "leche", "queso", "yogur", "kefir", "calcio", "suplemento de calcio" },
            Severity = "Medium",
            Message = "Interacción Fármaco-Alimento (IFAF): Los lácteos y alimentos ricos en calcio forman quelatos insolubles con estos antibióticos, reduciendo drásticamente su biodisponibilidad. Espaciar al menos 2 horas."
        },
        // Litio vs Fluctuaciones de Sal y Cafeína
        new DrugFoodRule
        {
            DrugKeywords = new[] { "litio", "plenur" },
            FoodKeywords = new[] { "café", "teína", "bebida energética", "sal", "embutido" },
            Severity = "High",
            Message = "Interacción Fármaco-Alimento (IFAF): Las dietas bajas en sodio o con exceso de cafeína alteran significativamente los niveles séricos de litio, provocando toxicidad por litio o pérdida de eficacia."
        },
        // AINEs (Ibuprofeno, Naproxeno) vs Irritantes gástricos / Alcohol
        new DrugFoodRule
        {
            DrugKeywords = new[] { "ibuprofeno", "naproxeno", "diclofenaco", "aspirina", "acido acetilsalicilico" },
            FoodKeywords = new[] { "alcohol", "vino", "cerveza", "picante", "guindilla", "chile" },
            Severity = "Medium",
            Message = "Interacción Fármaco-Alimento (IFAF): El consumo conjunto de AINEs con alcohol o irritantes gástricos aumenta significativamente el riesgo de sangrado y lesiones gastrointestinales."
        }
    };

    private class DrugFoodRule
    {
        public string[] DrugKeywords { get; set; } = Array.Empty<string>();
        public string[] FoodKeywords { get; set; } = Array.Empty<string>();
        public string Severity { get; set; } = "Medium";
        public string Message { get; set; } = string.Empty;
    }

    // Valida una dieta ya persistida contra el perfil y las restricciones del paciente, incluyendo
    // reglas de alimentos y comprobaciones de acceso al tenant antes de devolver incompatibilidades.
    // La validación vuelve a cargar paciente y alimentos desde la BD para no confiar en datos
    // enviados por el cliente y poder comprobar restricciones contra el estado actual del tenant.
    public async Task<List<DietValidationResultDto>> ValidateDietCompatibilityAsync(int clientId, diets diet, angulosodbContext context, int? tenantId, int userId, bool canUseTenantLocalFoods)
    {
        var client = await context.clients
            .Include(c => c.digestive_health)
            .Include(c => c.food_preferences)
            .Include(c => c.medical_history)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.id == clientId && (!tenantId.HasValue || c.tenant_id == tenantId.Value));

        if (client == null) return new List<DietValidationResultDto>();

        var foodIds = new List<int>();
        if (diet.diet_days != null)
        {
            foreach (var day in diet.diet_days)
            {
                if (day.meals != null)
                {
                    foreach (var meal in day.meals)
                    {
                        if (meal.meal_items != null)
                        {
                            foreach (var item in meal.meal_items)
                            {
                                if (item.food_id.HasValue)
                                {
                                    foodIds.Add(item.food_id.Value);
                                }
                            }
                        }
                    }
                }
            }
        }

        var foodsMap = await context.foods
            .AsNoTracking()
            .Where(f => foodIds.Contains(f.id) && ((f.source == null || f.source.ToLower() != "local") ||
             (tenantId.HasValue && f.tenant_id == tenantId.Value &&
              (canUseTenantLocalFoods || f.created_by_user_id == userId))))
            .ToDictionaryAsync(f => f.id);

        var warnings = new List<DietValidationResultDto>();

        if (diet.diet_days == null) return warnings;

        foreach (var day in diet.diet_days)
        {
            if (day.meals == null) continue;
            foreach (var meal in day.meals)
            {
                if (meal.meal_items == null) continue;
                foreach (var item in meal.meal_items)
                {
                    if (!item.food_id.HasValue || !foodsMap.TryGetValue(item.food_id.Value, out var food)) 
                        continue;

                    var foodWarnings = CheckFoodCompatibility(client, food, meal.name, day.day_index);
                    warnings.AddRange(foodWarnings);
                }
            }
        }

        return warnings;
    }

    // Variante para borradores: ejecuta las mismas reglas sobre el DTO sin exigir que la dieta
    // exista todavía en la base de datos.
    public async Task<List<DietValidationResultDto>> ValidateDietDraftCompatibilityAsync(int clientId, DietDetailDto dietDto, angulosodbContext context, int? tenantId, int userId, bool canUseTenantLocalFoods)
    {
        var client = await context.clients
            .Include(c => c.digestive_health)
            .Include(c => c.food_preferences)
            .Include(c => c.medical_history)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.id == clientId && (!tenantId.HasValue || c.tenant_id == tenantId.Value));

        if (client == null) return new List<DietValidationResultDto>();

        var foodIds = new List<int>();
        if (dietDto.Days != null)
        {
            foreach (var day in dietDto.Days)
            {
                if (day.Meals != null)
                {
                    foreach (var meal in day.Meals)
                    {
                        if (meal.Items != null)
                        {
                            foreach (var item in meal.Items)
                            {
                                if (item.FoodId.HasValue)
                                {
                                    foodIds.Add(item.FoodId.Value);
                                }
                            }
                        }
                    }
                }
            }
        }

        var foodsMap = await context.foods
            .AsNoTracking()
            .Where(f => foodIds.Contains(f.id) && ((f.source == null || f.source.ToLower() != "local") ||
             (tenantId.HasValue && f.tenant_id == tenantId.Value &&
              (canUseTenantLocalFoods || f.created_by_user_id == userId))))
            .ToDictionaryAsync(f => f.id);

        var warnings = new List<DietValidationResultDto>();

        if (dietDto.Days == null) return warnings;

        foreach (var day in dietDto.Days)
        {
            if (day.Meals == null) continue;
            foreach (var meal in day.Meals)
            {
                if (meal.Items == null) continue;
                foreach (var item in meal.Items)
                {
                    if (!item.FoodId.HasValue || !foodsMap.TryGetValue(item.FoodId.Value, out var food)) 
                        continue;

                    var foodWarnings = CheckFoodCompatibility(client, food, meal.Name, day.DayIndex);
                    warnings.AddRange(foodWarnings);
                }
            }
        }

        return warnings;
    }

    // Centraliza las reglas alimento-paciente y devuelve todas las incompatibilidades detectadas
    // para que la interfaz pueda mostrarlas sin detenerse en el primer problema.
    private List<DietValidationResultDto> CheckFoodCompatibility(clients client, foods food, string mealName, int dayIndex)
    {
        var list = new List<DietValidationResultDto>();
        var nameLower = (food.name ?? "").ToLowerInvariant();
        var catLower = (food.category ?? "").ToLowerInvariant();

        // 1. Lactose Check
        if (client.digestive_health?.lactose_intolerance == true)
        {
            if (LactoseKeywords.Any(k => nameLower.Contains(k) || catLower.Contains(k)))
            {
                list.Add(new DietValidationResultDto
                {
                    FoodId = food.id,
                    FoodName = food.name ?? string.Empty,
                    MealName = mealName ?? string.Empty,
                    DayIndex = dayIndex,
                    AlertType = "Lactose",
                    Severity = "High",
                    Message = "Contiene lácteos/lactosa y el paciente tiene intolerancia declarada."
                });
            }
        }

        // 2. Gluten Check
        if (client.digestive_health?.gluten_intolerance == true)
        {
            if (GlutenKeywords.Any(k => nameLower.Contains(k) || catLower.Contains(k)))
            {
                list.Add(new DietValidationResultDto
                {
                    FoodId = food.id,
                    FoodName = food.name ?? string.Empty,
                    MealName = mealName ?? string.Empty,
                    DayIndex = dayIndex,
                    AlertType = "Gluten",
                    Severity = "High",
                    Message = "Contiene gluten y el paciente tiene intolerancia declarada."
                });
            }
        }

        // 3. Fodmap Check
        if (client.digestive_health?.fodmaps_intolerance == true)
        {
            if (FodmapKeywords.Any(k => nameLower.Contains(k) || catLower.Contains(k)))
            {
                list.Add(new DietValidationResultDto
                {
                    FoodId = food.id,
                    FoodName = food.name ?? string.Empty,
                    MealName = mealName ?? string.Empty,
                    DayIndex = dayIndex,
                    AlertType = "Fodmap",
                    Severity = "Medium",
                    Message = "Alimento alto en FODMAPs, inadecuado para la sensibilidad del paciente."
                });
            }
        }

        // 4. Custom Allergies Check
        if (client.food_preferences != null && !string.IsNullOrWhiteSpace(client.food_preferences.allergies))
        {
            var allergens = client.food_preferences.allergies
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim().ToLowerInvariant())
                .Where(a => a.Length > 2);

            foreach (var allergen in allergens)
            {
                if (nameLower.Contains(allergen) || catLower.Contains(allergen) || allergen.Contains(nameLower))
                {
                    list.Add(new DietValidationResultDto
                    {
                        FoodId = food.id,
                        FoodName = food.name ?? string.Empty,
                        MealName = mealName ?? string.Empty,
                        DayIndex = dayIndex,
                        AlertType = "Allergy",
                        Severity = "High",
                        Message = $"Alerta de Alergia: Coincidencia detectada con '{allergen}'."
                    });
                    break;
                }
            }
        }

        // 5. Interacciones Fármaco-Alimento (IFAF)
        if (client.medical_history != null && !string.IsNullOrWhiteSpace(client.medical_history.routine_medication))
        {
            var medText = client.medical_history.routine_medication.ToLowerInvariant();
            foreach (var rule in DrugInteractions)
            {
                if (rule.DrugKeywords.Any(dk => medText.Contains(dk)))
                {
                    if (rule.FoodKeywords.Any(fk => nameLower.Contains(fk) || catLower.Contains(fk)))
                    {
                        list.Add(new DietValidationResultDto
                        {
                            FoodId = food.id,
                            FoodName = food.name ?? string.Empty,
                            MealName = mealName ?? string.Empty,
                            DayIndex = dayIndex,
                            AlertType = "IFAF",
                            Severity = rule.Severity,
                            Message = rule.Message
                        });
                        break;
                    }
                }
            }
        }

        return list;
    }
}

public class DietValidationResultDto
{
    public int? FoodId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public string MealName { get; set; } = string.Empty;
    public int DayIndex { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
