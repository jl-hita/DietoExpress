using System.Collections.Generic;

namespace Anguloso.Server.Model;

public class EnergyRequirementsDto
{
    public double Weight { get; set; }
    public double Height { get; set; }
    public int Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public bool HasBodyFat { get; set; }

    public FormulaResultDto MifflinStJeor { get; set; } = new();
    public FormulaResultDto HarrisBenedict { get; set; } = new();
    public FormulaResultDto? KatchMcArdle { get; set; } // Null when no valid body fat % is available
}

public class FormulaResultDto
{
    public double Bmr { get; set; }
    public Dictionary<string, double> Tdee { get; set; } // Key: Activity level name, Value: calories (kcal)
}
