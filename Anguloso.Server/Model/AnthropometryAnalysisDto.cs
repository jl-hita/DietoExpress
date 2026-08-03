namespace Anguloso.Server.Model;

public class AnthropometryAnalysisDto
{
    // Body Fat percentages from different protocols
    public double? BodyFatPercentageJacksonPollock3 { get; set; }
    public double? BodyFatPercentageJacksonPollock4 { get; set; }
    public double? BodyFatPercentageJacksonPollock7 { get; set; }
    public double? BodyFatPercentageFaulkner { get; set; }
    public double? BodyFatPercentageDurninWomersley { get; set; }
    public double? BodyFatPercentageCarter { get; set; }

    // 4-Component Body Composition (kg and %)
    public double? FatMassKg { get; set; }
    public double? FatMassPercentage { get; set; }
    
    public double? MuscleMassKg { get; set; }
    public double? MuscleMassPercentage { get; set; }

    public double? BoneMassKg { get; set; }
    public double? BoneMassPercentage { get; set; }

    public double? ResidualMassKg { get; set; }
    public double? ResidualMassPercentage { get; set; }

    // Heath-Carter Somatotype
    public HeathCarterSomatotypeDto? Somatotype { get; set; }
}

public class HeathCarterSomatotypeDto
{
    public double Endomorphy { get; set; }
    public double Mesomorphy { get; set; }
    public double Ectomorphy { get; set; }
    
    // Coordinates for somatochart plotting (only available when all 3 components are computed)
    public double X { get; set; }
    public double Y { get; set; }
    public bool CoordinatesAvailable { get; set; }
}
