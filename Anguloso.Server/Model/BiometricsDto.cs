using System.ComponentModel.DataAnnotations;

namespace Anguloso.Server.Model;

public class BiometricsDto
{
    public int Id { get; set; }
    public DateTime MeasurementDate { get; set; } // date only, use yyyy-MM-dd
    public double? Weight { get; set; }
    public double? Height { get; set; }
    public double? BodyFat { get; set; }
    public double? MuscleMass { get; set; }
    public double? VisceralFat { get; set; }
    public double? Waist { get; set; }
    public double? Hip { get; set; }
    public double? Neck { get; set; }
    public double? Triceps { get; set; }
    public double? Abdomen { get; set; }
    public double? Thigh { get; set; }
    public double? Subscapular { get; set; }
    public double? Suprailiac { get; set; }
    public double? Biceps { get; set; }
    public double? Chest { get; set; }
    public double? Axilla { get; set; }
    public double? CalfSkinfold { get; set; }
    public double? ArmPerimeter { get; set; }
    public double? CalfPerimeter { get; set; }
    public double? WristDiameter { get; set; }
    public double? FemurDiameter { get; set; }
    public double? HumerusDiameter { get; set; }
    public double? Bmi { get; set; }
    public string? Notes { get; set; }
    public AnthropometryAnalysisDto? Analysis { get; set; }
}

public class CreateBiometricDto
{
    [Required] public DateTime MeasurementDate { get; set; } // required
    public double? Weight { get; set; }
    public double? Height { get; set; }
    public double? BodyFat { get; set; }
    public double? MuscleMass { get; set; }
    public double? VisceralFat { get; set; }
    public double? Waist { get; set; }
    public double? Hip { get; set; }
    public double? Neck { get; set; }
    public double? Triceps { get; set; }
    public double? Abdomen { get; set; }
    public double? Thigh { get; set; }
    public double? Subscapular { get; set; }
    public double? Suprailiac { get; set; }
    public double? Biceps { get; set; }
    public double? Chest { get; set; }
    public double? Axilla { get; set; }
    public double? CalfSkinfold { get; set; }
    public double? ArmPerimeter { get; set; }
    public double? CalfPerimeter { get; set; }
    public double? WristDiameter { get; set; }
    public double? FemurDiameter { get; set; }
    public double? HumerusDiameter { get; set; }
    public string? Notes { get; set; }
}

public class UpdateBiometricDto : CreateBiometricDto
{
    // same fields as create
}