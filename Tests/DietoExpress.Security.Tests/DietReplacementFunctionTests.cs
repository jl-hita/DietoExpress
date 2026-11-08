using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DietReplacementFunctionTests
{
    private static async Task<(angulosodbContext Context, diets Diet, meal_items Item)> ArrangeAsync()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"diet-replacement-function-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new angulosodbContext(options);
        context.foods.AddRange(
            Food(1, "Pollo", "Carne", 165, 31, 0, 3.6),
            Food(2, "Plátano", "Fruta", 89, 1.1, 22.8, 0.3),
            Food(3, "Merluza", "Pescado", 90, 18, 0, 2),
            Food(4, "Arroz hervido", "Cereales", 130, 2.7, 28, 0.3));
        var item = new meal_items { id = 1, meal_id = 1, food_id = 1, grams = 100, kcal = 165, protein = 31, carbs = 0, fat = 3.6m };
        var meal = new meals { id = 1, diet_day_id = 1, name = "Comida", meal_index = 0 };
        meal.meal_items.Add(item);
        var day = new diet_days { id = 1, diet_id = 1, day_index = 0 };
        day.meals.Add(meal);
        var diet = new diets { id = 1, name = "Plan", target_kcal = 2000, target_protein = 100, target_carbs = 250, target_fat = 67, notes = "", excluded_food_keywords = "[]", diet_type = "Equilibrada" };
        diet.diet_days.Add(day);
        context.diets.Add(diet);
        await context.SaveChangesAsync();
        return (context, diet, item);
    }

    private static Task<DietRegenerationResponseDto> Replace(angulosodbContext context, int dietId, int itemId, int replacementId, bool allowChange = false)
        => new DietRegenerationService(context, new SpecializationRulesService(context)).RegenerateAsync(
            dietId, new DietRegenerationRequestDto
            {
                Operation = "replace-food", MealItemId = itemId, ReplacementFoodId = replacementId,
                PreserveWeeklyVariety = false, AllowFunctionChange = allowChange
            }, userId: 1, tenantId: null, isSuperAdmin: true, canUseTenantLocalFoods: false);

    [Theory]
    [InlineData(2)] // proteína → fruta
    [InlineData(4)] // proteína → hidrato
    public async Task ReplaceFood_RejectsReplacementThatChangesTheFoodFunction(int replacementId)
    {
        var (context, diet, item) = await ArrangeAsync();
        await using var _ = context;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Replace(context, diet.id, item.id, replacementId));

        Assert.Contains("misma función", ex.Message);
        Assert.Equal(1, item.food_id);
    }

    [Fact]
    public async Task ReplaceFood_AcceptsAnEquivalentProtein()
    {
        var (context, diet, item) = await ArrangeAsync();
        await using var _ = context;

        var response = await Replace(context, diet.id, item.id, 3);

        Assert.True(response.Success);
        Assert.Equal(3, item.food_id);
    }

    [Fact]
    public async Task ReplaceFood_AllowsAFunctionChangeOnlyWhenExplicitlyRequestedAndWarns()
    {
        var (context, diet, item) = await ArrangeAsync();
        await using var _ = context;

        var response = await Replace(context, diet.id, item.id, 2, allowChange: true);

        Assert.True(response.Success);
        Assert.Contains(response.Warnings, w => w.Contains("función"));
    }

    [Theory]
    [InlineData("Comida", "Pollo", "Merluza", true)]
    [InlineData("Comida", "Pollo", "Plátano", false)]
    [InlineData("Comida", "Arroz hervido", "Pollo", false)]
    [InlineData("Merienda", "Yogur natural", "Huevo cocido", true)]
    [InlineData("Merienda", "Yogur natural", "Atún", false)]
    public void IsFunctionallyEquivalent_UsesTheMealContext(string meal, string source, string replacement, bool expected)
        => Assert.Equal(expected, AdvancedDietOptimizerService.IsFunctionallyEquivalent(meal, Named(source), Named(replacement), out _, out _));

    private static foods Named(string name) => new() { name = name, category = "" };

    private static foods Food(int id, string name, string category, double kcal, double protein, double carbs, double fat) => new()
    {
        id = id, name = name, category = category, kcal = kcal, protein = protein, carbs = carbs, fat = fat,
        source = "BEDCA", allergen_data_status = "reported"
    };
}
