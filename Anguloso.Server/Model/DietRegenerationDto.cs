using System.Collections.Generic;

namespace Anguloso.Server.Model;

public sealed class DietRegenerationRequestDto
{
    public string Operation { get; set; } = "regenerate-meal";
    public int? DayId { get; set; }
    public int? MealId { get; set; }
    public int? MealItemId { get; set; }
    public int? ReplacementFoodId { get; set; }
    public bool PreserveNutritionTargets { get; set; } = true;
    public bool PreserveWeeklyVariety { get; set; } = true;
}

public sealed class DietRegenerationResponseDto
{
    public bool Success { get; set; }
    public string Operation { get; set; } = "";
    public string Message { get; set; } = "";
    public int DietId { get; set; }
    public int? DayId { get; set; }
    public int? MealId { get; set; }
    public int? MealItemId { get; set; }
    public string? PreviousFoodName { get; set; }
    public string? NewFoodName { get; set; }
    public decimal KcalDelta { get; set; }
    public decimal ProteinDelta { get; set; }
    public decimal CarbsDelta { get; set; }
    public decimal FatDelta { get; set; }
    public decimal ObjectiveBefore { get; set; }
    public decimal ObjectiveAfter { get; set; }
    public bool RolledBack { get; set; }
    public List<string> PreservedConstraints { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
