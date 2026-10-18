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
        Assert.Contains("DietaryRestrictionRules.IsFoodAllowed", generator);
        Assert.Contains("DietaryRestrictionRules.BuildForbiddenTerms", validation);
        Assert.Contains("DietaryRestrictionRules.IsFoodAllowed", validation);
        Assert.Contains("food.dietary_flags", validation);
        Assert.Contains("food.allergen_tags", validation);
        Assert.Contains("DietaryRestrictionRules.BuildForbiddenTerms", regeneration);
        Assert.Contains("DietaryRestrictionRules.IsFoodAllowed", regeneration);
        Assert.Contains("food.allergen_tags", regeneration);
        Assert.DoesNotContain(".Split(new[] { ',', ';', ' ' }", validation);

        var offService = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "OpenFoodFactsService.cs"));
        Assert.Contains("allergens_tags", offService);
        Assert.Contains("DietaryRestrictionRules.MergeKnownAllergenTags", offService);

        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        Assert.Contains("allergen-classification-v2", bootstrap);
    }
}