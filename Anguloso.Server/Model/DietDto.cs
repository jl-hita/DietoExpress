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

    public ICollection<DietDayDto> Days { get; set; } = new List<DietDayDto>();
}

// Sub-DTOs
public class DietDayDto
{
    public int Id { get; set; }
    public int DayIndex { get; set; }
    public ICollection<MealDto> Meals { get; set; } = new List<MealDto>();
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

    // Para la vista en el FrontEnd en detalle:
    public string? FoodName { get; set; }

    // Sistema de Intercambios
    public int? ExchangeGroupId { get; set; }
    public string? ExchangeGroupName { get; set; }
    public decimal? ExchangeCount { get; set; }
}
