using System;
using System.Collections.Generic;
using Anguloso.Server.Model;

namespace Anguloso.Server.Logica;

public class EnergyCalculatorService
{
    private static readonly Dictionary<string, double> ActivityFactors = new()
    {
        { "Sedentario", 1.2 },
        { "Ligero", 1.375 },
        { "Moderado", 1.55 },
        { "Activo", 1.725 },
        { "Muy Activo", 1.9 }
    };

    public EnergyRequirementsDto CalculateEnergyRequirements(double weight, double height, int age, string gender, double? bodyFat)
    {
        var isMale = IsMale(gender);
        
        var dto = new EnergyRequirementsDto
        {
            Weight = weight,
            Height = height,
            Age = age,
            Gender = isMale ? "Masculino" : "Femenino",
            HasBodyFat = bodyFat.HasValue
        };

        // 1. Mifflin-St Jeor
        double mifflinBmr = isMale
            ? (10.0 * weight) + (6.25 * height) - (5.0 * age) + 5.0
            : (10.0 * weight) + (6.25 * height) - (5.0 * age) - 161.0;
        
        dto.MifflinStJeor = new FormulaResultDto
        {
            Bmr = Math.Round(mifflinBmr, 2),
            Tdee = CalculateTdeeForBmr(mifflinBmr)
        };

        // 2. Harris-Benedict (Revised)
        double harrisBmr = isMale
            ? 88.362 + (13.397 * weight) + (4.799 * height) - (5.677 * age)
            : 447.593 + (9.247 * weight) + (3.098 * height) - (4.330 * age);
            
        dto.HarrisBenedict = new FormulaResultDto
        {
            Bmr = Math.Round(harrisBmr, 2),
            Tdee = CalculateTdeeForBmr(harrisBmr)
        };

        // 3. Katch-McArdle (needs body fat %)
        if (bodyFat.HasValue && bodyFat.Value > 0 && bodyFat.Value < 100)
        {
            double lbm = weight * (1.0 - (bodyFat.Value / 100.0));
            double katchBmr = 370.0 + (21.6 * lbm);
            
            dto.KatchMcArdle = new FormulaResultDto
            {
                Bmr = Math.Round(katchBmr, 2),
                Tdee = CalculateTdeeForBmr(katchBmr)
            };
        }

        return dto;
    }

    private Dictionary<string, double> CalculateTdeeForBmr(double bmr)
    {
        var tdeeDict = new Dictionary<string, double>();
        foreach (var kvp in ActivityFactors)
        {
            tdeeDict[kvp.Key] = Math.Round(bmr * kvp.Value, 2);
        }
        return tdeeDict;
    }

    private bool IsMale(string gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return true;
        
        var normalized = gender.Trim().ToLowerInvariant();
        return normalized == "m" || 
               normalized == "masculino" || 
               normalized == "h" || 
               normalized == "hombre" || 
               normalized == "male";
    }
}
