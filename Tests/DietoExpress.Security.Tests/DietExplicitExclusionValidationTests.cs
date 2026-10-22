using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DietExplicitExclusionValidationTests
{
    [Fact]
    public async Task DraftValidation_RejectsExplicitlyExcludedFoodEvenWithoutAssignedClients()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"diet-explicit-exclusion-{Guid.NewGuid():N}")
            .Options;
        await using var context = new angulosodbContext(options);
        context.foods.AddRange(
            Food(1, "Bebida de soja", "Bebidas"),
            Food(2, "Nutrition bar", "Snacks"));
        await context.SaveChangesAsync();

        var draft = Draft(1, 2);
        draft.ExcludedFoodKeywords.Add(" soja ");

        var service = new DietValidationService(new SpecializationRulesService(context));
        var warnings = await service.ValidateExplicitDietExclusionsAsync(
            draft, context, tenantId: null, userId: 1, canUseTenantLocalFoods: false);

        var warning = Assert.Single(warnings);
        Assert.Equal(1, warning.FoodId);
        Assert.Equal("ExplicitExclusion", warning.AlertType);
        Assert.Equal("High", warning.Severity);
    }

    [Fact]
    public async Task DraftValidation_RejectsExplicitlyExcludedRecipeIngredient()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"diet-recipe-explicit-exclusion-{Guid.NewGuid():N}")
            .Options;
        await using var context = new angulosodbContext(options);
        var food = Food(1, "Bebida de soja", "Bebidas");
        var recipe = new recipes
        {
            id = 10,
            user_id = 1,
            name = "Batido",
            instructions = "Mezclar",
            servings = 1,
            recipe_items = new List<recipe_items>()
        };
        recipe.recipe_items.Add(new recipe_items
        {
            id = 100,
            recipe_id = recipe.id,
            food_id = food.id,
            grams = 100,
            food = food,
            recipe = recipe
        });
        context.foods.Add(food);
        context.recipes.Add(recipe);
        await context.SaveChangesAsync();

        var draft = Draft();
        draft.Days.Single().Meals.Single().Items.Add(new MealItemDto { RecipeId = recipe.id });
        draft.ExcludedFoodKeywords.Add("soja");

        var service = new DietValidationService(new SpecializationRulesService(context));
        var warnings = await service.ValidateExplicitDietExclusionsAsync(
            draft, context, tenantId: null, userId: 1, canUseTenantLocalFoods: false);

        var warning = Assert.Single(warnings);
        Assert.Equal("ExplicitExclusion", warning.AlertType);
        Assert.Equal("High", warning.Severity);
        Assert.Equal("Bebida de soja", warning.FoodName);
    }

    private static DietDetailDto Draft(params int[] foodIds) => new()
    {
        Name = "Dieta de prueba",
        Days = new List<DietDayDto>
        {
            new()
            {
                DayIndex = 0,
                Meals = new List<MealDto>
                {
                    new()
                    {
                        Name = "Comida",
                        Items = foodIds.Select(id => new MealItemDto { FoodId = id }).ToList()
                    }
                }
            }
        }
    };

    private static foods Food(int id, string name, string category) => new()
    {
        id = id,
        name = name,
        category = category,
        source = "BEDCA",
        dietary_flags = Array.Empty<string>(),
        allergen_tags = Array.Empty<string>(),
        traces_tags = Array.Empty<string>()
    };
}
