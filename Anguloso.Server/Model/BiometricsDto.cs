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

public class ImportedBiometricRowDto
{
    public DateTime MeasurementDate { get; set; }
    public double? Weight { get; set; }
    public double? Height { get; set; }
    public double? BodyFat { get; set; }
    public double? MuscleMass { get; set; }
    public double? VisceralFat { get; set; }
    public double? Waist { get; set; }
    public double? Hip { get; set; }
    public string? SourceDevice { get; set; }
    public string? Notes { get; set; }
    public bool AlreadyExists { get; set; }
}

public class BioimpedancePreviewResponseDto
{
    public string DetectedBrand { get; set; } = "Desconocido"; // "Tanita", "InBody", "Genérico"
    public int TotalRowsFound { get; set; }
    public List<ImportedBiometricRowDto> Rows { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class ConfirmImportBiometricsDto
{
    public List<ImportedBiometricRowDto> Rows { get; set; } = new();
}