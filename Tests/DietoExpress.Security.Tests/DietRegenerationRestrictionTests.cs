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


    [Fact]
    public async Task ReplaceFood_UsesAssignedPatientsRestrictionsWhenClientIdIsOmitted()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"diet-regeneration-assigned-client-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var context = new angulosodbContext(options);
        context.foods.AddRange(
            Food(1, "Tofu firme", "Proteínas vegetales", 144, 17, 3, 9),
            Food(2, "Bebida de soja", "Bebidas", 33, 3.3, 0.7, 1.8));
        var client = new clients
        {
            id = 1,
            tenant_id = 5,
            full_name = "Paciente de prueba",
            email = "patient@example.test",
            phone = "",
            address = "",
            postal_code = "",
            city = "",
            province = "",
            country = "",
            gender = "",
            notes = "",
            access_token = "",
            passcode_hash = "",
            archived_at = null,
            food_preferences = new food_preferences
            {
                id = 1,
                client_id = 1,
                allergies = "soja",
                preferred_foods = "",
                disliked_foods = ""
            }
        };
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
            name = "Plan del paciente",
            user_id = 1,
            tenant_id = 5,
            target_kcal = 2000,
            target_protein = 100,
            target_carbs = 250,
            target_fat = 67,
            notes = "",
            excluded_food_keywords = "[]",
            diet_type = "Equilibrada"
        };
        diet.diet_days.Add(day);
        context.diets.Add(diet);
        context.clients.Add(client);
        context.client_diets.Add(new client_diets
        {
            id = 1,
            client_id = client.id,
            diet_id = diet.id,
            start_date = DateOnly.FromDateTime(DateTime.UtcNow),
            is_active = true,
            end_date = null
        });
        await context.SaveChangesAsync();

        var service = new DietRegenerationService(context, new SpecializationRulesService(context));
        var request = new DietRegenerationRequestDto
        {
            Operation = "replace-food",
            MealItemId = item.id,
            ReplacementFoodId = 2,
            PreserveWeeklyVariety = false
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegenerateAsync(
            dietId: diet.id,
            request: request,
            userId: 1,
            tenantId: 5,
            isSuperAdmin: false,
            canUseTenantLocalFoods: false));

        Assert.Equal(1, item.food_id);
    }


    [Theory]
    [InlineData("regenerate-meal")]
    [InlineData("regenerate-day")]
    public async Task Regeneration_DoesNotReintroducePersistedExplicitExclusions(string operation)
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"diet-regeneration-explicit-exclusion-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var context = new angulosodbContext(options);
        context.foods.AddRange(
            Food(1, "Tofu firme", "Proteínas vegetales", 144, 17, 3, 9),
            Food(2, "Tempeh natural", "Proteínas vegetales", 193, 20, 8, 11),
            Food(3, "Pechuga de pollo", "Carne", 165, 31, 0, 3.6));

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
            name = "Plan de prueba",
            user_id = null,
            tenant_id = null,
            target_kcal = 2000,
            target_protein = 100,
            target_carbs = 250,
            target_fat = 67,
            notes = "",
            excluded_food_keywords = "[\"Tempeh\"]",
            diet_type = "Equilibrada"
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
            PreserveNutritionTargets = false,
            PreserveWeeklyVariety = false
        };

        var response = await service.RegenerateAsync(
            dietId: diet.id,
            request: request,
            userId: 1,
            tenantId: null,
            isSuperAdmin: true,
            canUseTenantLocalFoods: false);

        Assert.True(response.Success || response.RolledBack);
        Assert.NotEqual(2, item.food_id);
        Assert.DoesNotContain("Tempeh", response.NewFoodName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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
