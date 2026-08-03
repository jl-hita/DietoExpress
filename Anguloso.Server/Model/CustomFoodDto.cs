using System.ComponentModel.DataAnnotations;

namespace Anguloso.Server.Model;

public class CustomFoodDto
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Brands { get; set; }

    [MaxLength(200)]
    public string? Category { get; set; }

    [MaxLength(5)]
    public string? Nutriscore { get; set; }

    public double? Kcal { get; set; }
    public double? Protein { get; set; }
    public double? Carbs { get; set; }
    public double? Fat { get; set; }
    public double? SaturatedFat { get; set; }
    public double? Fiber { get; set; }
    public double? Sugar { get; set; }
    public double? Salt { get; set; }

    public double? ServingSize { get; set; }
    
    [MaxLength(10)]
    public string? ServingSizeUnit { get; set; }
    
    [MaxLength(50)]
    public string? ServingSizeText { get; set; }

    public decimal? DefaultGrams { get; set; }

    [MaxLength(50)]
    public string? Source { get; set; }

    public int? ExchangeGroupId { get; set; }
    public decimal? GramsPerExchange { get; set; }
}
