using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class ProfessionalRecipeRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz.");
        }
    }

    [Fact]
    public void Recipes_ExposeServingAwareNutritionAndRestrictions()
    {
        var dto = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Model", "RecipeDto.cs"));
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "RecipeNutritionService.cs"));
        Assert.Contains("Servings", dto);
        Assert.Contains("PerServingKcal", dto);
        Assert.Contains("DietaryFlags", dto);
        Assert.Contains("SelectMany(i => i.DietaryFlags)", service);
        Assert.Contains("grams / 100.0", service);
    }

    [Fact]
    public void RecipeSubstitutionRecalculatesDietUsages()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "RecipesController.cs"));
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "RecipeNutritionService.cs"));
        Assert.Contains("substitute", controller);
        Assert.Contains("PreserveCalories", controller);
        Assert.Contains("RefreshRecipeUsagesAsync", controller);
        Assert.Contains("recipe_id == recipeId", service);
        Assert.Contains("recipe_servings", service);
    }

    [Fact]
    public void DietMealItemsCarryRecipeProvenance()
    {
        var model = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Models", "meal_items.cs"));
        var dto = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Model", "DietDto.cs"));
        Assert.Contains("recipe_id", model);
        Assert.Contains("recipe_servings", model);
        Assert.Contains("RecipeId", dto);
        Assert.Contains("RecipeServings", dto);
    }
}
