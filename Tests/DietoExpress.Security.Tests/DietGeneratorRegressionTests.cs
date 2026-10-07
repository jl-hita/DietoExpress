using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Regresiones del generador orientadas a personalización y calidad nutricional.
/// Estas pruebas de arquitectura verifican que la generación no pierda las señales
/// de adherencia ni el reequilibrado de macronutrientes en futuras refactorizaciones.
/// </summary>
public sealed class DietGeneratorRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    [Fact]
    public void Generator_UsesPatientFoodPreferencesAsSoftAdherenceSignals()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("preferredFoods = ParseFoodTerms(client.food_preferences?.preferred_foods)", source);
        Assert.Contains("dislikedFoods = ParseFoodTerms(client.food_preferences?.disliked_foods)", source);
        Assert.Contains("MatchesFoodTerms(f, dislikedFoods)", source);
        Assert.Contains("MatchesFoodTerms(f, preferredFoods)", source);
        Assert.Contains("alergias, intolerancias, exclusiones clínicas", source);
    }

    [Fact]
    public void Generator_UsesRecentCheckinAdherence()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("patient_checkins", source);
        Assert.Contains("Take(6)", source);
        Assert.Contains("recentAdherence", source);
        Assert.Contains("recentAdherence.Value <= 5", source);
        Assert.Contains("adherenceBoost", source);
        Assert.Contains("c.tenant_id == tenantId.Value", source);
    }

    [Fact]
    public void Generator_RebalancesEnergyAndMacros()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("RebalanceDay(todayMeals, targetKcal, targetProtein, targetCarbs, targetFat)", source);
        Assert.Contains("targetProtein", source);
        Assert.Contains("targetCarbs", source);
        Assert.Contains("targetFat", source);
        Assert.Contains("RelativeError(totalProtein, targetProtein)", source);
        Assert.Contains("RelativeError(totalCarbs, targetCarbs)", source);
        Assert.Contains("RelativeError(totalFat, targetFat)", source);
        Assert.Contains("CalculateRebalanceError", source);
        Assert.Contains("ApplyItemRatio", source);
        Assert.DoesNotContain("NormalizeDay(todayMeals", source);
    }

    [Fact]
    public void Generator_VeganFilter_ExcludesAnimalDerivedHoneyAndGelatin()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("name.Contains(\"miel\")", source);
        Assert.Contains("name.Contains(\"gelatina\")", source);
        Assert.Contains("if (isVegan", source);
    }
}
