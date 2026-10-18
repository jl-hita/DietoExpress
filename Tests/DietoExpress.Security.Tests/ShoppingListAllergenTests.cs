using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class ShoppingListAllergenTests
{
    [Fact]
    public void BuildFoodItems_GroupsAmountsAndCarriesAllergenMetadata()
    {
        var food = new foods
        {
            id = 42,
            name = "Barrita",
            category = "Snacks",
            dietary_flags = Array.Empty<string>(),
            allergen_tags = new[] { "en:milk", "en:peanuts" }
        };

        var result = ShoppingListRules.BuildFoodItems(new[] { (food, 100d), (food, 50d) });

        var item = Assert.Single(result);
        Assert.Equal(150d, item.TotalGrams);
        Assert.True(item.AllergenDataAvailable);
        Assert.Contains("leche/lácteos", item.AllergenTags);
        Assert.Contains("cacahuetes", item.AllergenTags);
    }

    [Fact]
    public void BuildFoodItems_MarksMissingAllergenDataAsUnknown()
    {
        var food = new foods
        {
            id = 43,
            name = "Alimento sin ficha",
            category = "Otros",
            dietary_flags = Array.Empty<string>(),
            allergen_tags = null!
        };

        var result = ShoppingListRules.BuildFoodItems(new[] { (food, 80d) });
        Assert.False(Assert.Single(result).AllergenDataAvailable);

        var emptyTagsFood = new foods
        {
            id = 44,
            name = "Alimento con etiquetas vacías",
            category = "Otros",
            dietary_flags = Array.Empty<string>(),
            allergen_tags = Array.Empty<string>()
        };
        var emptyTagsResult = ShoppingListRules.BuildFoodItems(new[] { (emptyTagsFood, 80d) });
        Assert.False(Assert.Single(emptyTagsResult).AllergenDataAvailable);
    }

    [Fact]
    public void AllShoppingListEndpointsExpandRecipeIngredients()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Anguloso.Server", "Program.cs")))
            root = root.Parent;
        Assert.NotNull(root);

        foreach (var relativePath in new[]
        {
            Path.Combine("Anguloso.Server", "Controllers", "DietController.cs"),
            Path.Combine("Anguloso.Server", "Controllers", "PatientPortalController.cs"),
            Path.Combine("Anguloso.Server", "Controllers", "ClientDietsController.cs")
        })
        {
            var source = File.ReadAllText(Path.Combine(root!.FullName, relativePath));
            Assert.Contains("recipeIngredientsByRecipe", source);
            Assert.Contains("ShoppingListRules.BuildFoodItems", source);
        }
    }
}