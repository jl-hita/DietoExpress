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
    public void Generator_PrioritizesWeeklyVarietyAndSlotNutrition()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("CalculateWeeklyRepetitionPenalty", source);
        Assert.Contains("uses * uses", source);
        Assert.Contains("CalculateSlotNutritionPenalty", source);
        Assert.Contains("targetProteinShare", source);
        Assert.Contains("targetCarbsShare", source);
        Assert.Contains("targetFatShare", source);
        Assert.Contains(".Take(8)", source);
    }

    [Fact]
    public void Generator_OptimizesWeeklyDiversityByFoodFamilies()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("weeklyRoleFamilyUsage", source);
        Assert.Contains("CalculateRoleFamilyRepetitionPenalty", source);
        Assert.Contains("GetFoodFamily", source);
        Assert.Contains("return uses == 0 ? 0 : 12 * uses * uses;", source);
        Assert.Contains("pescado_marisco", source);
        Assert.Contains("legumbre", source);
    }

    [Fact]
    public void Generator_VeganFilter_ExcludesAnimalDerivedHoneyAndGelatin()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("name.Contains(\"miel\")", source);
        Assert.Contains("name.Contains(\"gelatina\")", source);
        Assert.Contains("if (isVegan", source);
    }

    [Fact]
    public void Generator_ControlsCompleteMealRepetitionWithSmartSubstitution()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("weeklyMealSignatures", source);
        Assert.Contains("CreateMealSignature", source);
        Assert.Contains("TryReplaceRepeatedMealItem", source);
        Assert.Contains("CalculateSubstitutionScore", source);
        Assert.Contains("DecrementUsage", source);
        Assert.Contains("mismo rol", source);
    }

    [Fact]
    public void Generator_SubstitutionPreservesNutritionAndWeeklyDiversitySignals()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("densityError", source);
        Assert.Contains("candidate.kcal", source);
        Assert.Contains("candidate.protein", source);
        Assert.Contains("candidate.carbs", source);
        Assert.Contains("candidate.fat", source);
        Assert.Contains("CalculateWeeklyRepetitionPenalty(candidate, weeklyUsage)", source);
        Assert.Contains("CalculateRoleFamilyRepetitionPenalty(candidate, role, weeklyRoleFamilyUsage)", source);
        Assert.Contains("CalculateSlotNutritionPenalty(candidate, role", source);
    }

    [Fact]
    public void Generator_UsesGlobalWeeklyOptimizerWithFrequencyAndShoppingSignals()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        Assert.Contains("CalculateGlobalScore", source);
        Assert.Contains("FrequencyPenalty", source);
        Assert.Contains("RepairWeeklyFrequencies", source);
        Assert.Contains("CalculateShoppingMetrics", source);
        Assert.Contains("ShoppingVarietyThreshold", source);
        Assert.Contains("MaxWeeklyFoodRepetitions", source);
    }

    [Fact]
    public void Generator_UsesRecipesOnlyWhenAllIngredientsAreAllowed()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        Assert.Contains("ApplyRecipeCandidatesAsync", source);
        Assert.Contains("x.Ingredients.Count == x.Recipe.recipe_items.Count", source);
        Assert.Contains("x.Ingredients.All(i => foodMap.ContainsKey(i.food_id))", source);
        Assert.Contains("request.UseRecipes", source);
    }

    [Fact]
    public void Generator_RebalancesAfterGlobalSubstitutions()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        Assert.Contains("RebalanceMeal", source);
        Assert.Contains("RebalanceDay", source);
        Assert.Contains("ReplaceItem(item, candidate", source);
        Assert.Contains("currentScore", source);
    }

    [Fact]
    public void Generator_ExposesAdvancedOptimizationControls()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Model", "GenerateDietRequestDto.cs"));

        Assert.Contains("UseRecipes", source);
        Assert.Contains("EnableGlobalOptimization", source);
        Assert.Contains("MaxWeeklyFoodRepetitions", source);
        Assert.Contains("ShoppingVarietyThreshold", source);
    }

    [Fact]
    public void Generator_UsesStructuredDietTherapyAndEnergyRules()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietTherapyRuleEngine.cs"));
        Assert.Contains("EstimateEnergy", source);
        Assert.Contains("diabetes", source);
        Assert.Contains("hypertension", source);
        Assert.Contains("hypothyroidism", source);
        Assert.Contains("NutrientWeights", source);
        Assert.Contains("FrequencyTargets", source);
    }

    [Fact]
    public void Generator_UsesLatestBiometricsAndProfessionalTemplates()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));
        Assert.Contains("_context.biometrics", source);
        Assert.Contains("OrderByDescending(b => b.measurement_date)", source);
        Assert.Contains("TemplateDietId", source);
        Assert.Contains("VariantOfDietId", source);
        Assert.Contains("d.is_template", source);
    }

    [Fact]
    public void Generator_UsesExchangeGroupsForSemanticSubstitution()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));
        Assert.Contains("exchange_group_id", source);
        Assert.Contains("exchangeBonus", source);
        Assert.Contains("exchange_group?.name", source);
    }

    [Fact]
    public void Generator_ExplainsAppliedExpertRules()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));
        Assert.Contains("request.ExplainGeneration", source);
        Assert.Contains("Reglas aplicadas", source);
        Assert.Contains("motor experto", source);
    }

    [Fact]
    public void Generator_ExposesLocalizedRegenerationOperations()
    {
        var dto = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Model", "DietRegenerationDto.cs"));
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietRegenerationService.cs"));
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "DietController.cs"));

        Assert.Contains("replace-food", service);
        Assert.Contains("regenerate-meal", service);
        Assert.Contains("regenerate-day", service);
        Assert.Contains("PreserveNutritionTargets", dto);
        Assert.Contains("PreserveWeeklyVariety", dto);
        Assert.Contains("BeginTransactionAsync", service);
        Assert.Contains("RollbackAsync", service);
        Assert.Contains("IsAllowed", service);
        Assert.Contains("RebalanceMeal", service);
        Assert.Contains("[HttpPost(\"{id:int}/regenerate\")]", controller);
    }

    [Fact]
    public void Generator_RegenerationIsPatientAndTenantAware()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietRegenerationService.cs"));
        var dto = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Model", "DietRegenerationDto.cs"));

        Assert.Contains("ClientId", dto);
        Assert.Contains("tenant_id == tenantId.Value", service);
        Assert.Contains("food_preferences", service);
        Assert.Contains("digestive_health", service);
        Assert.Contains("d.tenant_id == tenantId.Value", service);
    }


}
