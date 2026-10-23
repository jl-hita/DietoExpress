using System.Reflection;
using Anguloso.Server.Controllers;
using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
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
        Assert.Contains("x.Ingredients.All(i => foodMap.ContainsKey(i.food_id) && DietSemanticValidationService.IsFoodCompatibleWithDietType", source);
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
        var optimizerSource = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));
        var generatorSource = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("exchange_group_id", optimizerSource);
        Assert.Contains("exchangeBonus", optimizerSource);
        Assert.Contains("exchange_group?.name", optimizerSource);
        Assert.Contains("ExchangeGroupId = f.exchange_group_id", generatorSource);
        Assert.Contains("ExchangeGroupName = f.exchange_group?.name", generatorSource);
        Assert.Contains("ExchangeCount = exchangeCount", generatorSource);
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


    [Fact]
    public void Generator_EnforcesHardCulinaryMinimumsAndMealScopedYogurt()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("var yogurtUsedInMeal = false", source);
        Assert.DoesNotContain("var yogurtUsedToday = false", source);
        Assert.Contains("return (125, 125)", source + File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietSemanticValidationService.cs")));
        Assert.Contains("return (100, 250)", source + File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietSemanticValidationService.cs")));
        Assert.Contains("Math.Max(snapped, minimum)", source);
    }

    [Fact]
    public void Generator_PreventsLegumesFromReplacingBreakfastCarbohydrates()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        Assert.Contains("return \"breakfast_carb\"", source);
        Assert.Contains("var isLegume = new[] { \"lenteja\", \"garbanzo\", \"alubia\", \"guisante\" }", source);
        Assert.Contains("if (isLegume) return \"other\"", source);
    }

    [Fact]
    public void Generator_GlobalOptimizerUsesCulinaryBoundsInsteadOfRelativeDrift()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        Assert.Contains("GetCulinaryBounds(meal.Name, item)", source);
        Assert.DoesNotContain("Math.Max(5.0, (double)(item.Grams ?? 0) * 0.75)", source);
    }


    [Fact]
    public void Generator_HasHardSemanticFinalGate()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietSemanticValidationService.cs"));
        var generator = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));
        Assert.Contains("ValidateOrThrowAsync", service);
        Assert.Contains("ValidateMealSemantics", service);
        Assert.Contains("ValidateDailyNutrition", service);
        Assert.Contains("GetCulinaryBounds", service);
        Assert.Contains("MaxWeeklyFoodRepetitions", service);
        Assert.Contains("_semanticValidationService.ValidateOrThrowAsync", generator);
    }

    [Fact]
    public void Generator_SemanticGateCoversKnownAbsurdCases()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietSemanticValidationService.cs"));
        Assert.Contains("IsLegumeName", service);
        Assert.Contains("IsRawOrDry", service);
        Assert.Contains("IsRareOil", service);
        Assert.Contains("IsHighGlycemicHeuristic", service);
        Assert.Contains("La misma combinación de alimentos", service);
        Assert.Contains("±15 %", service);
    }


    [Fact]
    public void Generator_ValidatesRecipeIngredientsAsPartOfTheMeal()
    {
        var semantic = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietSemanticValidationService.cs"));
        var optimizer = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        Assert.Contains("var recipeMap = new Dictionary<int, recipes>()", semantic);
        Assert.Contains("var semanticFoods = directFoods.Concat(recipeFoods).ToList()", semantic);
        Assert.Contains("semanticFoods.Any(food => IsVegetable(food.name, food.category))", semantic);
        Assert.Contains("RecipeHasRequiredMealStructure(x.Ingredients, request.DietType)", optimizer);
    }

    [Fact]
    public void Generator_RepairsMissingMealComponentsBeforeFinalValidation()
    {
        var semantic = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietSemanticValidationService.cs"));
        var optimizer = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        Assert.Contains("internal static bool IsCarb(string? name, string? category = null)", semantic);
        Assert.Contains("normalizedCategory.Contains", semantic);
        Assert.Contains("RepairMealSemanticStructure(diet, candidates, foodMap, request.DietType)", optimizer);
        Assert.Contains("DietSemanticValidationService.IsVegetable(f.name, f.category)", optimizer);
        Assert.Contains("DietSemanticValidationService.IsCarb(f.name, f.category)", optimizer);
        Assert.Contains("currentFoods.Any(f => DietSemanticValidationService.IsLegumeName(f.name)) && !hasBreakfastCarb", optimizer);
    }

    [Fact]
    public void Generator_CentralizesClinicalSafetyInTheFinalSemanticGate()
    {
        var semantic = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietSemanticValidationService.cs"));
        var generator = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));

        Assert.Contains("ValidateDietDraftCompatibilityAsync", semantic);
        Assert.Contains("StringComparison.OrdinalIgnoreCase", semantic);
        Assert.Contains("ValidatePatientTherapySemanticsAsync", semantic);
        Assert.Contains("ValidateDietTypeSemantics", semantic);
        Assert.Contains("semanticFoods.Any(food => IsLegumeName(food.name))", semantic);
        Assert.Contains("IsFruit(food.name)", semantic);
        Assert.Contains("IsAnimalFood", semantic);
        Assert.Contains("IsKetoForbiddenCarb", semantic);
        Assert.DoesNotContain("_dietValidationService", generator);
        Assert.Contains("canUseTenantLocalFoods", generator);
    }


    [Theory]
    [InlineData(0, true, true)]
    [InlineData(0, false, false)]
    [InlineData(499, true, false)]
    [InlineData(500, false, true)]
    [InlineData(10000, false, true)]
    [InlineData(10001, true, false)]
    public void GenerationKcalValidation_AllowsZeroOnlyForAutomaticEstimation(double kcal, bool automatic, bool expected)
    {
        var method = typeof(DietController).GetMethod("IsValidTargetKcal", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No se encontró la validación de kcal del endpoint.");
        Assert.Equal(expected, (bool)(method.Invoke(null, new object[] { kcal, automatic }) ?? false));
    }

    [Theory]
    [InlineData("Nutrition bar", "nut", false)]
    [InlineData("Nut bar", "nut", true)]
    [InlineData("BEBIDA DE SOJA", " soja ", true)]
    [InlineData("Sopa de verduras", "soja", false)]
    [InlineData("Café con leche", "LECHE", true)]
    public void Generator_ExplicitExclusionsUseNormalizedWholeTerms(string foodName, string keyword, bool expectedExcluded)
    {
        Assert.Equal(expectedExcluded, InvokeIsExcluded(foodName, "Alimentos", keyword));
    }

    [Theory]
    [InlineData("Té verde", "té", true)]
    [InlineData("Bebida vegetal", "té", false)]
    [InlineData("Proteína", "te", false)]
    public void Generator_HonorsShortExplicitExclusionsOnlyAsWholeTerms(string foodName, string keyword, bool expectedExcluded)
    {
        Assert.Equal(expectedExcluded, InvokeIsExcluded(foodName, "Alimentos", keyword));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Generator_IgnoresEmptyExplicitExclusionKeywords(string keyword)
    {
        Assert.False(InvokeIsExcluded("Nutrition bar", "Alimentos", keyword));
    }

    [Fact]
    public void Generator_PreservesLegacyLactPrefixExclusion()
    {
        Assert.True(InvokeIsExcluded("Yogur sin lactosa", "Lácteos", "lact"));
    }

    private static bool InvokeIsExcluded(string foodName, string category, params string[] keywords)
    {
        // IsExcluded is intentionally tested directly: this exercises the production
        // matching path rather than asserting that particular source strings exist.
        var service = new DietGeneratorService(null!, null!, null!);
        var food = new foods { name = foodName, category = category };
        var method = typeof(DietGeneratorService).GetMethod(
            "IsExcluded", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No se encontró DietGeneratorService.IsExcluded.");
        return (bool)(method.Invoke(service, new object[]
        {
            food,
            keywords.ToHashSet(StringComparer.OrdinalIgnoreCase)
        }) ?? throw new InvalidOperationException("IsExcluded no devolvió un resultado."));
    }

    [Fact]
    public void Optimizer_RestoresWholeMealAfterRejectedCandidate()
    {
        var optimizer = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        Assert.Contains("var mealSnapshot = originalItems", optimizer);
        Assert.Contains("foreach (var entry in mealSnapshot)", optimizer);
        Assert.Contains("RestoreItem(entry.Item, entry.Snapshot)", optimizer);
    }

    [Fact]
    public void GeneratorAndRegenerationRequireCompleteNonNegativeMacrosForAutomaticFoodCandidates()
    {
        var generator = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));
        var regeneration = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietRegenerationService.cs"));
        var optimizer = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        foreach (var source in new[] { generator, regeneration })
        {
            Assert.Contains("f.protein.HasValue", source);
            Assert.Contains("f.carbs.HasValue", source);
            Assert.Contains("f.fat.HasValue", source);
            Assert.Contains(".Value >= 0", source);
        }

        Assert.Contains("food.protein.HasValue", optimizer);
        Assert.Contains("food.carbs.HasValue", optimizer);
        Assert.Contains("food.fat.HasValue", optimizer);
        Assert.Contains(".Value >= 0", optimizer);
    }

    [Fact]
    public void SemanticValidation_RecognizesMushroomsAfterAccentNormalization()
    {
        var method = typeof(DietSemanticValidationService).GetMethod(
            "IsVegetable", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No se encontró DietSemanticValidationService.IsVegetable.");

        Assert.True((bool)(method.Invoke(null, new object?[] { "Champiñón", null }) ?? false));
    }

    [Theory]
    [InlineData("Pollo", "Vegetariana", false)]
    [InlineData("Yogur natural", "Vegetariana", true)]
    [InlineData("Yogur natural", "Vegana", false)]
    [InlineData("Arroz blanco", "Cetogenica", false)]
    [InlineData("Pasta", "BajaCarbos", false)]
    [InlineData("Calabacín", "Vegana", true)]
    public void OptimizerDietTypeCompatibilityMatchesGeneratorRules(string foodName, string dietType, bool expected)
    {
        Assert.Equal(expected, DietSemanticValidationService.IsFoodCompatibleWithDietType(foodName, dietType));
    }

    [Fact]
    public void OptimizerRejectsHighCarbohydrateFoodsForKetogenicDiets()
    {
        Assert.False(DietSemanticValidationService.IsFoodCompatibleWithDietType(
            "Dátil", "Cetogenica", null, carbsPer100g: 60, fatPer100g: 0));
    }

    [Fact]
    public void DietTypeCompatibilityAlsoUsesFoodCategoryForHiddenAnimalProducts()
    {
        Assert.False(DietSemanticValidationService.IsFoodCompatibleWithDietType("Producto procesado", "Vegana", "Lácteos"));
        Assert.False(DietSemanticValidationService.IsFoodCompatibleWithDietType("Producto procesado", "Vegetariana", "Pescado"));
    }

    [Fact]
    public void OptimizerDoesNotPenalizeUnavailableOrDietIncompatibleFoodFrequencies()
    {
        var method = typeof(AdvancedDietOptimizerService).GetMethod(
            "FrequencyPenalty", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No se encontró FrequencyPenalty.");
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["fruta"] = 7,
            ["verdura"] = 14,
            ["legumbre"] = 3,
            ["frutos_secos"] = 4
        };
        var availableFoods = new[]
        {
            FrequencyFood(1, "Manzana", "Fruta", 52, 0.3, 14, 0.2),
            FrequencyFood(2, "Tomate", "Verduras", 18, 0.9, 3.9, 0.2),
            FrequencyFood(3, "Garbanzos cocidos", "Legumbres", 164, 8.9, 27.4, 2.6),
            FrequencyFood(4, "Almendras", "Frutos secos", 579, 21, 22, 50),
            FrequencyFood(5, "Merluza", "Pescado", 86, 18, 0, 1.5)
        };

        double Penalty(string dietType, IEnumerable<foods> catalog) =>
            (double)(method.Invoke(null, new object?[] { counts, 7, null, dietType, catalog }) ?? 0);

        var balancedPenalty = Penalty("Equilibrada", availableFoods);
        var veganPenalty = Penalty("Vegana", availableFoods);
        var vegetarianPenalty = Penalty("Vegetariana", availableFoods);
        var narrowCatalogPenalty = Penalty("Equilibrada", availableFoods.Where(food => food.id != 5));

        Assert.Equal(0d, veganPenalty);
        Assert.Equal(0d, vegetarianPenalty);
        Assert.Equal(0d, narrowCatalogPenalty);
        Assert.True(balancedPenalty > veganPenalty);
    }

    [Theory]
    [InlineData("Garbanzos cocidos", false)]
    [InlineData("Garbanzos con espinacas", false)]
    [InlineData("Pan integral", true)]
    [InlineData("Avena", true)]
    [InlineData("Arroz blanco", false)]
    public void SemanticValidation_OnlyRecognizesAppropriateBreakfastCarbohydrates(string foodName, bool expected)
    {
        Assert.Equal(expected, DietSemanticValidationService.IsBreakfastCarb(foodName));
    }

    [Fact]
    public void OptimizerDoesNotMistakeLegumesForBreakfastCarbohydratesWhenRepairingSnacks()
    {
        var optimizer = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        var start = optimizer.IndexOf("var hasBreakfastCarb", StringComparison.Ordinal);
        var end = optimizer.IndexOf("if (currentFoods.Any", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var breakfastCarbCheck = optimizer[start..end];
        Assert.Contains("!DietSemanticValidationService.IsLegumeName(f.name)", breakfastCarbCheck);
        Assert.Contains("DietSemanticValidationService.IsBreakfastCarb(f.name)", breakfastCarbCheck);
        Assert.DoesNotContain("IsCarb(", breakfastCarbCheck);
    }

    [Theory]
    [InlineData("Garbanzos cocidos", "garbanzo")]
    [InlineData("Garbanzos con espinacas", "garbanzo")]
    [InlineData("Lentejas estofadas", "lenteja")]
    [InlineData("Judías blancas cocidas", "judia")]
    [InlineData("Guisantes", "guisante")]
    [InlineData("Arroz integral", null)]
    public void OptimizerRecognizesDuplicateLegumeCoreWithoutTreatingOtherFoodsAsLegumes(string foodName, string? expected)
    {
        var method = typeof(AdvancedDietOptimizerService).GetMethod(
            "GetLegumeCoreName", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("No se encontró GetLegumeCoreName.");

        Assert.Equal(expected, method.Invoke(null, new object?[] { foodName }));
    }

    [Fact]
    public void OptimizerRepairsDuplicateLegumeComponentsBeforeFinalRebalance()
    {
        var optimizer = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "AdvancedDietOptimizerService.cs"));

        Assert.Contains("RepairDuplicateLegumeComponents(diet, foodMap)", optimizer);
        Assert.Contains("GetLegumeCoreName", optimizer);
        Assert.Contains("meal.Items.Remove(duplicate.Item)", optimizer);
        Assert.Contains("if (meal.Items == null || meal.Items.Any(item => item.RecipeId.HasValue))", optimizer);
    }

    private static foods FrequencyFood(int id, string name, string category, double kcal, double protein, double carbs, double fat) =>
        new() { id = id, name = name, category = category, kcal = kcal, protein = protein, carbs = carbs, fat = fat, source = "BEDCA" };
}
