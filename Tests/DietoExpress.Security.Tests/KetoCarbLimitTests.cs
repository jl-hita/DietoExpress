using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class KetoCarbLimitTests
{
    [Theory]
    [InlineData("Cetogenica", true)]
    [InlineData("Dieta cetogénica", true)]
    [InlineData("Vegana", false)]
    [InlineData(null, false)]
    public void IsKetoDietType_RecognisesTheKetoDietTypes(string? type, bool expected)
        => Assert.Equal(expected, DietSemanticValidationService.IsKetoDietType(type));

    [Fact]
    public void KetoCarbLimitError_OnlyReportsDaysAboveTheLimit()
    {
        Assert.Null(DietSemanticValidationService.KetoCarbLimitError(0, 50));
        Assert.Null(DietSemanticValidationService.KetoCarbLimitError(0, 12));
        var error = DietSemanticValidationService.KetoCarbLimitError(2, 63.4);
        Assert.NotNull(error);
        Assert.Contains("Día 3", error);
        Assert.Contains("50", error);
    }

    [Fact]
    public async Task SaveValidation_RejectsKetoDayOverTheLimitRecalculatingFromCatalogData()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"keto-carbs-{Guid.NewGuid():N}").Options;
        await using var context = new angulosodbContext(options);
        context.foods.AddRange(Food(1, "Aguacate", carbs: 2), Food(2, "Arroz hervido", carbs: 28));
        await context.SaveChangesAsync();

        // El cliente declara 0 g de hidratos para el arroz; el servidor debe recalcular 28 g/100 g × 250 g = 70 g.
        var diet = Draft(new MealItemDto { FoodId = 1, Grams = 100, Carbs = 0 }, new MealItemDto { FoodId = 2, Grams = 250, Carbs = 0 });
        var service = new DietValidationService(new SpecializationRulesService(context));

        var warnings = await service.ValidateDietTypeCompatibilityAsync(diet, context, tenantId: null);

        var warning = Assert.Single(warnings, w => w.AlertType == "KetoCarbLimit");
        Assert.Equal("High", warning.Severity);
    }

    [Fact]
    public async Task SaveValidation_AcceptsKetoDayUnderTheLimit()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"keto-carbs-ok-{Guid.NewGuid():N}").Options;
        await using var context = new angulosodbContext(options);
        context.foods.Add(Food(1, "Aguacate", carbs: 2));
        await context.SaveChangesAsync();

        var diet = Draft(new MealItemDto { FoodId = 1, Grams = 200 });
        var service = new DietValidationService(new SpecializationRulesService(context));

        Assert.DoesNotContain(await service.ValidateDietTypeCompatibilityAsync(diet, context, tenantId: null), w => w.AlertType == "KetoCarbLimit");
    }

    private static DietDetailDto Draft(params MealItemDto[] items) => new()
    {
        Name = "Keto",
        DietType = "Cetogenica",
        Days = new List<DietDayDto> { new() { DayIndex = 0, Meals = new List<MealDto> { new() { Name = "Comida", Items = items.ToList() } } } }
    };

    private static foods Food(int id, string name, double carbs) => new()
    {
        id = id, name = name, category = "Test", source = "BEDCA", carbs = carbs,
        dietary_flags = Array.Empty<string>(), allergen_tags = Array.Empty<string>(), traces_tags = Array.Empty<string>()
    };
}
