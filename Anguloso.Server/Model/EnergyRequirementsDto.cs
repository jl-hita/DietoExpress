using System.Collections.Generic;

namespace Anguloso.Server.Model;

public class EnergyRequirementsDto
{
    public double Weight { get; set; }
    public double Height { get; set; }
    public int Age { get; set; }
    public string Gender { get; set; }
    public bool HasBodyFat { get; set; }

    public FormulaResultDto MifflinStJeor { get; set; }
    public FormulaResultDto HarrisBenedict { get; set; }
    public FormulaResultDto KatchMcArdle { get; set; } // Will be null if no body fat % is available
}

public class FormulaResultDto
{
    public double Bmr { get; set; }
    public Dictionary<string, double> Tdee { get; set; } // Key: Activity level name, Value: calories (kcal)
}
