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

        Assert.Contains("DietaryRestrictionRules.BuildForbiddenTerms", generator);
        Assert.Contains("food.dietary_flags", generator);
        Assert.Contains("DietaryRestrictionRules.IsFoodAllowed", generator);
        Assert.Contains("DietaryRestrictionRules.BuildForbiddenTerms", validation);
        Assert.Contains("DietaryRestrictionRules.IsFoodAllowed", validation);
        Assert.Contains("food.dietary_flags", validation);
        Assert.DoesNotContain(".Split(new[] { ',', ';', ' ' }", validation);
    }
}