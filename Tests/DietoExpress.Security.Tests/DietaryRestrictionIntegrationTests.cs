using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DietaryRestrictionIntegrationTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    [Fact]
    public void GeneratorAndCompatibilityValidator_UseSharedAllergenRules()
    {
        var generator = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));
        var validation = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietValidationService.cs"));
        var regeneration = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietRegenerationService.cs"));

        Assert.Contains("DietaryRestrictionRules.BuildForbiddenTerms", generator);
        Assert.Contains("food.dietary_flags", generator);
        Assert.Contains("food.allergen_tags", generator);
        Assert.Contains("food.traces_tags", generator);
        Assert.Contains("DietaryRestrictionRules.IsFoodAllowed", generator);
        Assert.Contains("DietaryRestrictionRules.BuildForbiddenTerms", validation);
        Assert.Contains("DietaryRestrictionRules.IsFoodAllowed", validation);
        Assert.Contains("food.dietary_flags", validation);
        Assert.Contains("food.allergen_tags", validation);
        Assert.Contains("food.traces_tags", validation);
        Assert.Contains("DietaryRestrictionRules.BuildForbiddenTerms", regeneration);
        Assert.Contains("DietaryRestrictionRules.IsFoodAllowed", regeneration);
        Assert.Contains("_specializationRulesService.GetFoodExclusionsAsync", regeneration);
        Assert.Contains("_specializationRulesService.GetExcludedFoodIdsAsync", regeneration);
        Assert.Contains("specializationExcludedFoodIds.Contains(f.id)", regeneration);
        Assert.DoesNotContain("HasUnknownAllergenDataForClient", generator);
        Assert.DoesNotContain("IsAllowedForClient", regeneration);
        var substitutions = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "FoodSubstitutionService.cs"));
        Assert.DoesNotContain("DietaryRestrictionRules.HasUnknownAllergenData", substitutions);
        Assert.Contains("f.allergen_tags", substitutions);
        Assert.Contains("f.traces_tags", substitutions);
        Assert.Contains("food.allergen_tags", regeneration);
        Assert.Contains("food.traces_tags", regeneration);
        Assert.DoesNotContain(".Split(new[] { ',', ';', ' ' }", validation);

        var shopping = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "ShoppingListRules.cs"));
        var recipeNutrition = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "RecipeNutritionService.cs"));
        Assert.Contains("DietaryRestrictionRules.HasKnownAllergenTags((food.allergen_tags ?? Array.Empty<string>()).Concat(food.traces_tags ?? Array.Empty<string>()))", shopping);
        Assert.Contains("DietaryRestrictionRules.HasKnownAllergenTags((food.allergen_tags ?? Array.Empty<string>()).Concat(food.traces_tags ?? Array.Empty<string>()))", recipeNutrition);

        var offService = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "OpenFoodFactsService.cs"));
        Assert.Contains("allergens_tags", offService);
        Assert.Contains("traces_tags", offService);
        Assert.Contains("DietaryRestrictionRules.MergeKnownAllergenTags", offService);
        Assert.Contains("allergen_data_status", offService);
        Assert.Contains("allergen_data_checked_at", offService);

        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        Assert.Contains("allergen-classification-v2", bootstrap);
        Assert.Contains("allergen-traces-quality-v1", bootstrap);
        var shoppingDto = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Model", "PatientPortalDto.cs"));
        var recipeDto = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Model", "RecipeDto.cs"));
        Assert.Contains("TraceTags", shoppingDto);
        Assert.Contains("AllergenDataStatus", shoppingDto);
        Assert.Contains("TraceTags", recipeDto);
        Assert.Contains("AllergenDataStatus", recipeDto);
    }
}