using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Anguloso.Server.Model;

public class RecipeListDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class RecipeDetailDto : RecipeListDto
{
    public decimal Servings { get; set; } = 1;
    public decimal? YieldGrams { get; set; }
    public RecipeNutritionDto Nutrition { get; set; } = new();
    public List<string> DietaryFlags { get; set; } = new();
    public List<RecipeIngredientDto> Ingredients { get; set; } = new();
}

public class RecipeNutritionDto
{
    public double? TotalKcal { get; set; }
    public double? TotalProtein { get; set; }
    public double? TotalCarbs { get; set; }
    public double? TotalFat { get; set; }
    public double? PerServingKcal { get; set; }
    public double? PerServingProtein { get; set; }
    public double? PerServingCarbs { get; set; }
    public double? PerServingFat { get; set; }
}

public class RecipeIngredientDto
{
    public int FoodId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public string? Brands { get; set; }
    public decimal Grams { get; set; }

    // Macros calculados para la cantidad especificada
    public double? Kcal { get; set; }
    public double? Protein { get; set; }
    public double? Carbs { get; set; }
    public double? Fat { get; set; }
}

public class CreateRecipeDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    public string? Instructions { get; set; }

    [Range(0.1, 1000)]
    public decimal Servings { get; set; } = 1;

    [Range(0.1, 100000)]
    public decimal? YieldGrams { get; set; }

    [Required]
    public List<CreateRecipeIngredientDto> Ingredients { get; set; } = new();
}

public class CreateRecipeIngredientDto
{
    [Required]
    public int FoodId { get; set; }

    [Required]
    [Range(0.1, 10000)]
    public decimal Grams { get; set; }
}

public class SubstituteRecipeIngredientDto
{
    [Required]
    public int IngredientFoodId { get; set; }

    [Required]
    public int ReplacementFoodId { get; set; }

    public bool PreserveCalories { get; set; } = true;
}
