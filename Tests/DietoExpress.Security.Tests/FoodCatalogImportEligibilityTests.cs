using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class FoodCatalogImportEligibilityTests
{
    [Fact]
    public void CompleteAndPlausibleProduct_IsEligibleForDietGenerator()
    {
        var product = new OffProduct
        {
            Code = "8410000000000",
            Product_name = "Lentejas cocidas",
            Nutriments = new OffNutriments
            {
                EnergyKcal100g = 116,
                Proteins100g = 8.5,
                Carbohydrates100g = 20,
                Fat100g = 0.4,
                Fiber100g = 7.5,
                Salt100g = 0.6
            }
        };

        Assert.True(OpenFoodFactsService.IsEligibleForDietGenerator(product));
    }

    [Fact]
    public void MissingEssentialMacro_IsNotEligible()
    {
        var product = CreateValidProduct();
        product.Nutriments.Carbohydrates100g = null;

        Assert.False(OpenFoodFactsService.IsEligibleForDietGenerator(product));
    }

    [Fact]
    public void ImplausibleNutrientValue_IsNotEligible()
    {
        var product = CreateValidProduct();
        product.Nutriments.Fat100g = 101;

        Assert.False(OpenFoodFactsService.IsEligibleForDietGenerator(product));
    }

    [Fact]
    public void ProductWithoutExternalIdOrWithAllMacrosZero_IsNotEligible()
    {
        var withoutId = CreateValidProduct();
        withoutId.Code = "";
        Assert.False(OpenFoodFactsService.IsEligibleForDietGenerator(withoutId));

        var zeroMacros = CreateValidProduct();
        zeroMacros.Nutriments.Proteins100g = 0;
        zeroMacros.Nutriments.Carbohydrates100g = 0;
        zeroMacros.Nutriments.Fat100g = 0;
        Assert.False(OpenFoodFactsService.IsEligibleForDietGenerator(zeroMacros));
    }

    private static OffProduct CreateValidProduct() => new()
    {
        Code = "8410000000000",
        Product_name = "Alimento de prueba",
        Nutriments = new OffNutriments
        {
            EnergyKcal100g = 120,
            Proteins100g = 5,
            Carbohydrates100g = 20,
            Fat100g = 2
        }
    };
}
