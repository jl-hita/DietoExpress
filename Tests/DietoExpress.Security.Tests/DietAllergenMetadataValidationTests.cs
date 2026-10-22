using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DietAllergenMetadataValidationTests
{
    [Fact]
    public async Task DraftValidation_BlocksUnverifiedAllergenDataWhenPatientHasDeclaredAllergies()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"diet-allergen-metadata-validation-{Guid.NewGuid():N}")
            .Options;
        await using var context = new angulosodbContext(options);

        context.foods.Add(new foods
        {
            id = 1,
            name = "Arroz blanco",
            category = "Cereales",
            source = "BEDCA",
            kcal = 360,
            protein = 7,
            carbs = 80,
            fat = 1,
            dietary_flags = Array.Empty<string>(),
            allergen_tags = Array.Empty<string>(),
            traces_tags = Array.Empty<string>(),
            allergen_data_status = "unknown"
        });
        context.clients.Add(new clients
        {
            id = 1,
            tenant_id = 5,
            full_name = "Paciente de prueba",
            food_preferences = new food_preferences
            {
                id = 1,
                client_id = 1,
                allergies = "cacahuete",
                preferred_foods = "",
                disliked_foods = ""
            }
        });
        await context.SaveChangesAsync();

        var draft = new DietDetailDto
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
                            Items = new List<MealItemDto>
                            {
                                new() { FoodId = 1, FoodName = "Arroz blanco", Grams = 100 }
                            }
                        }
                    }
                }
            }
        };

        var service = new DietValidationService(new SpecializationRulesService(context));
        var warnings = await service.ValidateDietDraftCompatibilityAsync(
            clientId: 1,
            dietDto: draft,
            context: context,
            tenantId: null,
            userId: 1,
            canUseTenantLocalFoods: false);

        Assert.Contains(warnings, warning =>
            warning.AlertType == "AllergenDataUnknown" &&
            warning.Severity == "High");
    }
}
