namespace Anguloso.Server.Model;

using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

public class DietListDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal? TargetKcal { get; set; }
    public decimal? TargetProtein { get; set; }
    public decimal? TargetCarbs { get; set; }
    public decimal? TargetFat { get; set; }
    public string? Notes { get; set; }
    public DateTime? CreatedAt { get; set; }
    public bool IsShared { get; set; }
    public bool IsTemplate { get; set; }
    public bool IsMine { get; set; }
    public string? AuthorName { get; set; }
}

public class DietDetailDto : DietListDto 
{ 
    public ICollection<DietDayDto> Days { get; set; } = new List<DietDayDto>();
}

public class CreateDietDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = "";
    public decimal? TargetKcal { get; set; }
    public decimal? TargetProtein { get; set; }
    public decimal? TargetCarbs { get; set; }
    public decimal? TargetFat { get; set; }
    public string? Notes { get; set; }
    public bool IsShared { get; set; } = false;
    public bool IsTemplate { get; set; } = false;

    // Si se informa, la dieta se crea y se asigna al paciente en la misma transacción.
    public int? ClientId { get; set; }

    public ICollection<DietDayDto> Days { get; set; } = new List<DietDayDto>();
}

public class UpdateDietDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = "";
    public decimal? TargetKcal { get; set; }
    public decimal? TargetProtein { get; set; }
    public decimal? TargetCarbs { get; set; }
    public decimal? TargetFat { get; set; }
    public string? Notes { get; set; }
    public bool IsShared { get; set; } = false;
    public bool IsTemplate { get; set; } = false;

    public ICollection<DietDayDto> Days { get; set; } = new List<DietDayDto>();
}

// Sub-DTOs
public class DietDayDto
{
    public int Id { get; set; }
    public int DayIndex { get; set; }
    public ICollection<MealDto> Meals { get; set; } = new List<MealDto>();
    public MicronutrientDailySummaryDto? Micronutrients { get; set; }
}

public class MicronutrientDailySummaryDto
{
    // Minerales y electrolitos (valores absolutos y % CDR)
    public decimal CalciumMg { get; set; }
    public decimal CalciumPctRda { get; set; }
    public decimal IronMg { get; set; }
    public decimal IronPctRda { get; set; }
    public decimal MagnesiumMg { get; set; }
    public decimal MagnesiumPctRda { get; set; }
    public decimal PotassiumMg { get; set; }
    public decimal PotassiumPctRda { get; set; }
    public decimal ZincMg { get; set; }
    public decimal ZincPctRda { get; set; }

    // Vitaminas (valores absolutos y % CDR)
    public decimal VitaminAUg { get; set; }
    public decimal VitaminAPctRda { get; set; }
    public decimal VitaminCMg { get; set; }
    public decimal VitaminCPctRda { get; set; }
    public decimal VitaminDUg { get; set; }
    public decimal VitaminDPctRda { get; set; }
    public decimal VitaminEMg { get; set; }
    public decimal VitaminEPctRda { get; set; }
    public decimal VitaminB12Ug { get; set; }
    public decimal VitaminB12PctRda { get; set; }
    public decimal FolateUg { get; set; }
    public decimal FolatePctRda { get; set; }

    // Fibra, Azúcar y Sal
    public decimal FiberG { get; set; }
    public decimal FiberPctRda { get; set; }
    public decimal SugarG { get; set; }
    public decimal SaltG { get; set; }
}

public class MealDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int MealIndex { get; set; }
    public ICollection<MealItemDto> Items { get; set; } = new List<MealItemDto>();
}

public class MealItemDto
{
    public int Id { get; set; }
    public int? FoodId { get; set; }
    public decimal? Grams { get; set; }

    // Campos precalculados que enviamos al cliente pero que también enviamos al back.
    public decimal? Kcal { get; set; }
    public decimal? Protein { get; set; }
    public decimal? Carbs { get; set; }
    public decimal? Fat { get; set; }

    public int? RecipeId { get; set; }
    public string? RecipeName { get; set; }
    public decimal? RecipeServings { get; set; }

    // Para la vista en el FrontEnd en detalle:
    public string? FoodName { get; set; }

    // Sistema de Intercambios
    public int? ExchangeGroupId { get; set; }
    public string? ExchangeGroupName { get; set; }
    public decimal? ExchangeCount { get; set; }
}

public class ValidateDietRequestDto
{
    [Required]
    public int ClientId { get; set; }

    [Required]
    public DietDetailDto Diet { get; set; } = new DietDetailDto();
}

