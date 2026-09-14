using System.Collections.Generic;

namespace Anguloso.Server.Model;

public class GenerateDietRequestDto
{
    public double TargetKcal { get; set; } = 2000;
    public double? TargetProtein { get; set; }
    public double? TargetCarbs { get; set; }
    public double? TargetFat { get; set; }
    public int NumberOfDays { get; set; } = 7;
    public int MealsPerDay { get; set; } = 5; // 3, 4, 5
    public string DietType { get; set; } = "Equilibrada"; // Equilibrada, AltaProteina, BajaCarbos, Vegetariana
    public int? ClientId { get; set; } // Si se pasa, se leen automáticamente sus alergias e intolerancias
    public List<string> ExcludedFoodKeywords { get; set; } = new();
}
