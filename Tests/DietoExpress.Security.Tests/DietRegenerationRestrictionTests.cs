using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DietRegenerationRestrictionTests
{
    [Theory]
    [InlineData(2)] // Alimento excluido explícitamente en la dieta persistida.
    [InlineData(3)] // Alimento incompatible con una dieta vegana.
    public async Task ReplaceFood_RejectsPersistedExclusionsAndDietTypeViolations(int replacementFoodId)
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"diet-regeneration-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var context = new angulosodbContext(options);
        context.foods.AddRange(
            Food(1, "Tofu firme", "Proteínas vegetales", 144, 17, 3, 9),
            Food(2, "Plátano", "Fruta", 89, 1.1, 22.8, 0.3),
            Food(3, "Pollo", "Carne", 165, 31, 0, 3.6));

        var item = new meal_items
        {
            id = 1,
            meal_id = 1,
            food_id = 1,
            grams = 100,
            kcal = 144,
            protein = 17,
            carbs = 3,
            fat = 9
        };
        var meal = new meals { id = 1, diet_day_id = 1, name = "Comida", meal_index = 0 };
        meal.meal_items.Add(item);
        var day = new diet_days { id = 1, diet_id = 1, day_index = 0 };
        day.meals.Add(meal);
        var diet = new diets
        {
            id = 1,
            name = "Plan Vegana",
            user_id = null,
            tenant_id = null,
            target_kcal = 2000,
            target_protein = 100,
            target_carbs = 250,
            target_fat = 67,
            notes = "",
            excluded_food_keywords = "[\"Plátano\"]",
            diet_type = "Vegana"
        };
        diet.diet_days.Add(day);
        context.diets.Add(diet);
        await context.SaveChangesAsync();

        var service = new DietRegenerationService(context, new SpecializationRulesService(context));
        var request = new DietRegenerationRequestDto
        {
            Operation = "replace-food",
            MealItemId = item.id,
            ReplacementFoodId = replacementFoodId,
            PreserveWeeklyVariety = false
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegenerateAsync(
            dietId: diet.id,
            request: request,
            userId: 1,
            tenantId: null,
            isSuperAdmin: true,
            canUseTenantLocalFoods: false));

        Assert.Equal(1, item.food_id);
    }


    [Theory]
    [InlineData("regenerate-meal")]
    [InlineData("regenerate-day")]
    public async Task Regeneration_FailsWithoutAlternativeInsteadOfReportingSuccess(string operation)
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"diet-regeneration-empty-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var context = new angulosodbContext(options);
        context.foods.Add(Food(1, "Tofu firme", "Proteínas vegetales", 144, 17, 3, 9));

        var item = new meal_items
        {
            id = 1,
            meal_id = 1,
            food_id = 1,
            grams = 100,
            kcal = 144,
            protein = 17,
            carbs = 3,
            fat = 9
        };
        var meal = new meals { id = 1, diet_day_id = 1, name = "Comida", meal_index = 0 };
        meal.meal_items.Add(item);
        var day = new diet_days { id = 1, diet_id = 1, day_index = 0 };
        day.meals.Add(meal);
        var diet = new diets
        {
            id = 1,
            name = "Plan Vegana",
            user_id = null,
            tenant_id = null,
            target_kcal = 2000,
            target_protein = 100,
            target_carbs = 250,
            target_fat = 67,
            notes = "",
            excluded_food_keywords = "[]",
            diet_type = "Vegana"
        };
        diet.diet_days.Add(day);
        context.diets.Add(diet);
        await context.SaveChangesAsync();

        var service = new DietRegenerationService(context, new SpecializationRulesService(context));
        var request = new DietRegenerationRequestDto
        {
            Operation = operation,
            MealId = operation == "regenerate-meal" ? meal.id : null,
            DayId = operation == "regenerate-day" ? day.id : null,
            PreserveWeeklyVariety = false
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegenerateAsync(
            dietId: diet.id,
            request: request,
            userId: 1,
            tenantId: null,
            isSuperAdmin: true,
            canUseTenantLocalFoods: false));

        Assert.Contains("No hay un alimento alternativo compatible", exception.Message);
        Assert.Equal(1, item.food_id);
    }


    [Theory]
    [InlineData("regenerate-meal")]
    [InlineData("regenerate-day")]
    public async Task Regeneration_RejectsUnknownAllergenDataForPatientWithDeclaredAllergies(string operation)
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"diet-regeneration-allergen-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var context = new angulosodbContext(options);
        var safeFood = Food(1, "Tofu firme", "Proteínas vegetales", 144, 17, 3, 9);
        safeFood.allergen_data_status = "reported";
        var unknownFood = Food(2, "Producto vegetal sin ficha de alérgenos", "Proteínas vegetales", 140, 15, 4, 8);
        unknownFood.allergen_data_status = "unknown";
        context.foods.AddRange(safeFood, unknownFood);

        var client = new clients
        {
            id = 1,
            full_name = "Paciente de prueba",
            archived_at = null,
            tenant_id = null,
            food_preferences = new food_preferences
            {
                id = 1,
                client_id = 1,
                allergies = "cacahuete, leche"
            }
        };
        context.clients.Add(client);

        var item = new meal_items
        {
            id = 1, meal_id = 1, food_id = 1, grams = 100,
            kcal = 144, protein = 17, carbs = 3, fat = 9
        };
        var meal = new meals { id = 1, diet_day_id = 1, name = "Comida", meal_index = 0 };
        meal.meal_items.Add(item);
        var day = new diet_days { id = 1, diet_id = 1, day_index = 0 };
        day.meals.Add(meal);
        var diet = new diets
        {
            id = 1, name = "Plan equilibrado", user_id = null, tenant_id = null,
            target_kcal = 2000, target_protein = 100, target_carbs = 250, target_fat = 67,
            notes = "", excluded_food_keywords = "[]", diet_type = "Equilibrada"
        };
        diet.diet_days.Add(day);
        context.diets.Add(diet);
        await context.SaveChangesAsync();

        var service = new DietRegenerationService(context, new SpecializationRulesService(context));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegenerateAsync(
            dietId: diet.id,
            request: new DietRegenerationRequestDto
            {
                Operation = operation,
                MealId = operation == "regenerate-meal" ? meal.id : null,
                DayId = operation == "regenerate-day" ? day.id : null,
                ClientId = client.id,
                PreserveWeeklyVariety = false
            },
            userId: 1,
            tenantId: null,
            isSuperAdmin: true,
            canUseTenantLocalFoods: false));

        Assert.Contains("No hay un alimento alternativo compatible", exception.Message);
        Assert.Equal(1, item.food_id);
    }

    private static foods Food(int id, string name, string category, double kcal, double protein, double carbs, double fat) =>
        new()
        {
            id = id,
            name = name,
            category = category,
            kcal = kcal,
            protein = protein,
            carbs = carbs,
            fat = fat,
            source = "BEDCA"
        };
}
