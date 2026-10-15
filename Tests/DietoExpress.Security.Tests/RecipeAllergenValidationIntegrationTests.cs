using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class RecipeAllergenValidationIntegrationTests
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
    public void DietValidation_TraversesRecipeIngredientsForPersistedAndDraftDiets()
    {
        var validation = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietValidationService.cs"));
        Assert.Contains("recipeIngredientsByRecipe", validation);
        Assert.Contains("AddRecipeIngredientWarnings", validation);
        Assert.Contains("ValidateDietDraftCompatibilityAsync", validation);
        Assert.Contains("ingredient.food.dietary_flags", validation);
    }

    [Fact]
    public void ManualDietEdit_RevalidatesActiveAssignmentsBeforeSaving()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "DietController.cs"));
        var start = controller.IndexOf("public async Task<IActionResult> UpdateDiet", StringComparison.Ordinal);
        var end = controller.IndexOf("[HttpDelete(\"{id:int}\")]", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var update = controller[start..end];
        Assert.Contains("_context.client_diets", update);
        Assert.Contains("_validationService.ValidateDietDraftCompatibilityAsync", update);
        Assert.Contains("return Conflict", update);
    }
}