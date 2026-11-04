using System.ComponentModel.DataAnnotations;

namespace Anguloso.Server.Model;

/// <summary>Campos nutricionales que SuperAdmin puede completar en el catálogo global.</summary>
public sealed class FoodDataQualityUpdateDto
{
    [Range(0, 100000)] public double? Kcal { get; set; }
    [Range(0, 100000)] public double? Protein { get; set; }
    [Range(0, 100000)] public double? Carbs { get; set; }
    [Range(0, 100000)] public double? Fat { get; set; }
    [Range(0, 100000)] public double? Sugar { get; set; }
    [Range(0, 100000)] public double? SaturatedFat { get; set; }
    [Range(0, 100000)] public double? MonounsaturatedFat { get; set; }
    [Range(0, 100000)] public double? PolyunsaturatedFat { get; set; }
    [Range(0, 100000)] public double? Fiber { get; set; }
    [Range(0, 100000)] public double? Salt { get; set; }
    [Range(0, 100000)] public double? VitaminAUg { get; set; }
    [Range(0, 100000)] public double? VitaminCMg { get; set; }
    [Range(0, 100000)] public double? VitaminDUg { get; set; }
    [Range(0, 100000)] public double? VitaminEMg { get; set; }
    [Range(0, 100000)] public double? VitaminB12Ug { get; set; }
    [Range(0, 100000)] public double? FolateUg { get; set; }
    [Range(0, 100000)] public double? CalciumMg { get; set; }
    [Range(0, 100000)] public double? IronMg { get; set; }
    [Range(0, 100000)] public double? MagnesiumMg { get; set; }
    [Range(0, 100000)] public double? PotassiumMg { get; set; }
    [Range(0, 100000)] public double? ZincMg { get; set; }
}
