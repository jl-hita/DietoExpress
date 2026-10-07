using System.Collections.Generic;

namespace Anguloso.Server.Model;

public class GenerateDietRequestDto
{
    public double TargetKcal { get; set; } = 0;
    public double? TargetProtein { get; set; }
    public double? TargetCarbs { get; set; }
    public double? TargetFat { get; set; }
    public int NumberOfDays { get; set; } = 7;
    public int MealsPerDay { get; set; } = 5; // 3, 4, 5
    public string DietType { get; set; } = "Equilibrada"; // Equilibrada, AltaProteina, BajaCarbos, Vegetariana
    public int? ClientId { get; set; } // Si se pasa, se leen automáticamente sus alergias e intolerancias
    public List<string> ExcludedFoodKeywords { get; set; } = new();

    // Motor avanzado: permite que el generador use recetas reales, optimización global
    // y control de compras sin convertir las preferencias blandas en restricciones clínicas.
    public bool UseRecipes { get; set; } = true;
    public int MaxWeeklyFoodRepetitions { get; set; } = 4;
    public int ShoppingVarietyThreshold { get; set; } = 28;
    public bool EnableGlobalOptimization { get; set; } = true;
    public bool EnableDietTherapyRules { get; set; } = true;
    public bool EnableAutomaticEnergyEstimate { get; set; } = true;
    public bool UseExchangeGroups { get; set; } = true;
    public bool ExplainGeneration { get; set; } = true;
}
