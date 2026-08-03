using Anguloso.Server.Models;

namespace Anguloso.Server.Model;

public class OpenFoodFactsDto
{
    //Varios DTOs para la API de Open Food Facts
}

public class OffProductResponse
{
    public OffProduct Product { get; set; } = new();
}

public class OffProduct
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Product_name { get; set; } = "";
    public string Brands { get; set; } = "";
    public string? Category { get; set; } = "";
    public string Nutriscore_grade { get; set; } = "";
    public double? ServingSize { get; set; } // gramos
    public string? ServingSizeUnit { get; set; } // g, ml, etc.
    public string? ServingSizeText { get; set; } // “1 cup (195g)” opcional

    public OffNutriments Nutriments { get; set; } = new OffNutriments();
    public OffMicronutrients Micronutrients { get; set; } = new();


    public OffProduct(foods food)
    {
        Id = food.id;
        Code = food.external_id;
        Product_name = food.name;
        Brands = food.brands;
        Category = food.category;
        Nutriscore_grade = food.nutriscore;
        ServingSize = food.serving_size;
        ServingSizeUnit = food.serving_size_unit;
        ServingSizeText = food.serving_size_text;

        //Macros
        Nutriments.Fiber100g = food.fiber;
        Nutriments.Sugars100g = food.sugar;
        Nutriments.SaturatedFat100g = food.saturated_fat;
        Nutriments.Proteins100g = food.protein;
        Nutriments.Carbohydrates100g = food.carbs;
        Nutriments.EnergyKcal100g = food.kcal;
        Nutriments.Salt100g = food.salt;
        Nutriments.Fat100g = food.fat;

        //Micronutrientes
        Micronutrients.VitaminA_ug = food.vitamin_a_ug;
        Micronutrients.VitaminC_mg = food.vitamin_c_mg;
        Micronutrients.VitaminD_ug = food.vitamin_d_ug;
        Micronutrients.VitaminE_mg = food.vitamin_e_mg;
        Micronutrients.VitaminB12_ug = food.vitamin_b12_ug;
        Micronutrients.Folate_ug = food.folate_ug;
        Micronutrients.Calcium_mg = food.calcium_mg;
        Micronutrients.Iron_mg = food.iron_mg;
        Micronutrients.Magnesium_mg = food.magnesium_mg;
        Micronutrients.Potassium_mg = food.potassium_mg;
        Micronutrients.Zinc_mg = food.zinc_mg;
    }

    public OffProduct()
    {
    }
}

public class OffNutriments
{
    public double? EnergyKcal100g { get; set; }
    public double? Fat100g { get; set; }
    public double? SaturatedFat100g { get; set; }
    public double? Carbohydrates100g { get; set; }
    public double? Sugars100g { get; set; }
    public double? Fiber100g { get; set; }
    public double? Proteins100g { get; set; }
    public double? Salt100g { get; set; }
}

/*
 * IDs oficiales USDA (claves)

Estos IDs son bastante estables:

Nutriente	                    NutrientId
Vitamin A (RAE)	                1106
Vitamin C	                    1162
Vitamin D (D2+D3)	            1114
Vitamin E (alpha-tocopherol)	1109
Vitamin B12	                    1178
Folate, total	                1177
Calcium	                        1087
Iron	                        1089
Magnesium	                    1090
Potassium	                    1092
Zinc	                        1095
 */
public class OffMicronutrients
{
    // Vitaminas
    public double? VitaminA_ug { get; set; }
    public double? VitaminC_mg { get; set; }
    public double? VitaminD_ug { get; set; }
    public double? VitaminE_mg { get; set; }
    public double? VitaminB12_ug { get; set; }
    public double? Folate_ug { get; set; }

    // Minerales
    public double? Calcium_mg { get; set; }
    public double? Iron_mg { get; set; }
    public double? Magnesium_mg { get; set; }
    public double? Potassium_mg { get; set; }
    public double? Zinc_mg { get; set; }
}