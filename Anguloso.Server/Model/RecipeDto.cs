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
    public List<RecipeIngredientDto> Ingredients { get; set; } = new();
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
