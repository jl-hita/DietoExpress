using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Escenarios deterministas con un catálogo de alimentos de prueba y el generador real.
/// No sustituyen una validación con el catálogo de producción ni una revisión clínica.
/// </summary>
public sealed class DietGeneratorScenarioTests
{
    [Theory]
    [InlineData("Vegana", true)]
    [InlineData("Vegana", false)]
    [InlineData("Vegetariana", true)]
    [InlineData("Vegetariana", false)]
    public async Task MultiDayGeneration_RespectsDietTypeAndExplicitExclusions(string dietType, bool enableGlobalOptimization)
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase($"diet-generator-{Guid.NewGuid():N}")
            .Options;

        await using var context = new angulosodbContext(options);
        context.foods.AddRange(CreateCatalog());
        await context.SaveChangesAsync();

        var specializationRules = new SpecializationRulesService(context);
        var validation = new DietValidationService(specializationRules);
        var semanticValidation = new DietSemanticValidationService(validation);
        var generator = new DietGeneratorService(context, specializationRules, semanticValidation);

        var request = new GenerateDietRequestDto
        {
            NumberOfDays = 3,
            MealsPerDay = 3,
            TargetKcal = 2000,
            TargetProtein = 100,
            TargetCarbs = 250,
            TargetFat = 67,
            DietType = dietType,
            ExcludedFoodKeywords = new List<string> { " Plátano " },
            UseRecipes = false,
            EnableGlobalOptimization = enableGlobalOptimization,
            EnableDietTherapyRules = false,
            EnableAutomaticEnergyEstimate = false,
            MaxWeeklyFoodRepetitions = 10,
            ShoppingVarietyThreshold = 100
        };

        var diet = await generator.GenerateDietAsync(request, tenantId: null, userId: 1, canUseTenantLocalFoods: false);

        Assert.Equal(3, diet.Days.Count);
        Assert.Equal(dietType, diet.DietType);
        Assert.Equal(new[] { "Plátano" }, diet.ExcludedFoodKeywords);

        var items = diet.Days.SelectMany(day => day.Meals).SelectMany(meal => meal.Items).ToList();
        Assert.NotEmpty(items);
        Assert.DoesNotContain(items, item =>
            DietaryRestrictionRules.Normalize(item.FoodName).Contains("platano", StringComparison.Ordinal));
        Assert.DoesNotContain(items, item => item.FoodName == "Fruta sin datos");
        Assert.DoesNotContain(items, item => item.FoodName == "Fruta con macros negativos");
        Assert.DoesNotContain(items, item => item.FoodName == "Producto marino");
        if (dietType == "Vegana")
            Assert.DoesNotContain(items, item => item.FoodName == "Producto lácteo");
        Assert.All(items, item =>
        {
            Assert.True((item.Grams ?? 0) > 0);
            Assert.True(DietSemanticValidationService.IsFoodCompatibleWithDietType(item.FoodName, dietType));
            Assert.True(double.IsFinite((double)(item.Kcal ?? 0)));
            Assert.True(double.IsFinite((double)(item.Protein ?? 0)));
            Assert.True(double.IsFinite((double)(item.Carbs ?? 0)));
            Assert.True(double.IsFinite((double)(item.Fat ?? 0)));
        });

        foreach (var day in diet.Days)
        {
            var dayItems = day.Meals.SelectMany(meal => meal.Items).ToList();
            var kcal = dayItems.Sum(item => (double)(item.Kcal ?? 0));
            var protein = dayItems.Sum(item => (double)(item.Protein ?? 0));
            var carbs = dayItems.Sum(item => (double)(item.Carbs ?? 0));
            var fat = dayItems.Sum(item => (double)(item.Fat ?? 0));

            Assert.InRange(Math.Abs(kcal - 2000) / 2000, 0, 0.15);
            Assert.InRange(Math.Abs(protein - 100) / 100, 0, 0.20);
            Assert.InRange(Math.Abs(carbs - 250) / 250, 0, 0.20);
            Assert.InRange(Math.Abs(fat - 67) / 67, 0, 0.20);
        }
    }

    private static List<foods> CreateCatalog()
    {
        var catalog = new List<foods>();
        var id = 1;

        void Add(string name, string category, double kcal, double protein, double carbs, double fat)
        {
            catalog.Add(new foods
            {
                id = id++,
                name = name,
                category = category,
                kcal = kcal,
                protein = protein,
                carbs = carbs,
                fat = fat,
                source = "BEDCA",
                serving_size = 100,
                serving_size_unit = "g",
                serving_size_text = "100 g",
                default_grams = 100
            });
        }

        Add("Tofu firme", "Proteínas vegetales", 144, 17, 3, 9);
        Add("Tempeh natural", "Proteínas vegetales", 193, 20, 8, 11);
        Add("Arroz integral cocido", "Cereales", 123, 2.7, 25.6, 1);
        Add("Pasta integral cocida", "Cereales", 150, 5.5, 30, 1);
        Add("Patata cocida", "Tubérculos", 87, 1.9, 20, 0.1);
        Add("Avena", "Cereales", 389, 16.9, 66.3, 6.9);
        Add("Pan integral", "Cereales", 247, 13, 41, 4);
        Add("Quinoa cocida", "Cereales", 120, 4.4, 21.3, 1.9);
        Add("Garbanzos cocidos", "Legumbres", 164, 8.9, 27.4, 2.6);
        Add("Lentejas cocidas", "Legumbres", 116, 9, 20, 0.4);
        Add("Alubias cocidas", "Legumbres", 127, 8.7, 22.8, 0.5);

        Add("Tomate", "Verduras", 18, 0.9, 3.9, 0.2);
        Add("Lechuga", "Verduras", 15, 1.4, 2.9, 0.2);
        Add("Espinaca", "Verduras", 23, 2.9, 3.6, 0.4);
        Add("Calabacin", "Verduras", 17, 1.2, 3.1, 0.3);
        Add("Berenjena", "Verduras", 25, 1, 6, 0.2);
        Add("Brocoli", "Verduras", 34, 2.8, 6.6, 0.4);
        Add("Zanahoria", "Verduras", 41, 0.9, 9.6, 0.2);
        Add("Pepino", "Verduras", 15, 0.7, 3.6, 0.1);
        Add("Pimiento", "Verduras", 31, 1, 6, 0.3);
        Add("Calabaza", "Verduras", 26, 1, 6.5, 0.1);
        Add("Champiñón", "Verduras", 22, 3.1, 3.3, 0.3);

        Add("Manzana", "Fruta", 52, 0.3, 14, 0.2);
        Add("Pera", "Fruta", 57, 0.4, 15, 0.1);
        Add("Plátano", "Fruta", 89, 1.1, 22.8, 0.3);
        Add("Naranja", "Fruta", 47, 0.9, 11.8, 0.1);
        Add("Fresa", "Fruta", 32, 0.7, 7.7, 0.3);
        Add("Kiwi", "Fruta", 61, 1.1, 14.7, 0.5);
        Add("Melon", "Fruta", 34, 0.8, 8.2, 0.2);
        catalog.Add(new foods
        {
            id = id++,
            name = "Fruta sin datos",
            category = "Fruta",
            kcal = 50,
            protein = null,
            carbs = null,
            fat = null,
            source = "BEDCA"
        });
        catalog.Add(new foods
        {
            id = id++,
            name = "Fruta con macros negativos",
            category = "Fruta",
            kcal = 50,
            protein = -1,
            carbs = 10,
            fat = 1,
            source = "BEDCA"
        });

        Add("Huevo", "Huevos", 143, 13, 1.1, 10);
        Add("Yogur natural", "Lácteos", 61, 3.5, 4.7, 3.3);
        Add("Leche semidesnatada", "Lácteos", 46, 3.2, 4.8, 1.6);
        Add("Queso fresco", "Lácteos", 98, 12, 3, 4);
        Add("Aceite de oliva", "Aceites", 884, 0, 0, 100);
        Add("Almendras", "Frutos secos", 579, 21, 22, 50);
        Add("Producto marino", "Marisco", 120, 20, 0, 4);
        Add("Producto lácteo", "Lácteos", 100, 5, 5, 5);

        return catalog;
    }
}
