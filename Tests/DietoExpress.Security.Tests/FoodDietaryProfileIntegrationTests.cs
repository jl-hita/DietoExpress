using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

[Collection("DietaryProfileCatalog")]
public sealed class FoodDietaryProfileIntegrationTests
{
    private const int NotVegetarianId = 9_000_001;
    private const int VeganMilkNameId = 9_000_002;
    private const int VegetarianOnlyId = 9_000_003;
    private const int NoProfileId = 9_000_004;

    private static void WithProfiles(Action body)
    {
        FoodDietaryProfileCatalog.SetForTests(new Dictionary<int, FoodDietaryProfileCatalog.Profile>
        {
            [NotVegetarianId] = new(false, false),
            [VeganMilkNameId] = new(true, true),
            [VegetarianOnlyId] = new(false, true),
        });
        try { body(); }
        finally { FoodDietaryProfileCatalog.SetForTests(new Dictionary<int, FoodDietaryProfileCatalog.Profile>()); }
    }

    [Fact]
    public void CuratedProfile_BlocksFoodsTheNameHeuristicWouldMiss()
        => WithProfiles(() =>
        {
            // Nombre sin ninguna palabra animal reconocida, pero el perfil curado dice que no es vegetariano.
            Assert.False(DietSemanticValidationService.IsFoodCompatibleWithDietType("Delikatess", "Dieta vegetariana", null, null, null, NotVegetarianId));
            Assert.False(DietSemanticValidationService.IsFoodCompatibleWithDietType("Delikatess", "Dieta vegana", null, null, null, NotVegetarianId));
        });

    [Fact]
    public void CuratedProfile_AllowsVeganFoodWhoseNameContainsAnimalWord()
        => WithProfiles(() =>
            Assert.True(DietSemanticValidationService.IsFoodCompatibleWithDietType("Leche de coco", "Dieta vegana", null, null, null, VeganMilkNameId)));

    [Fact]
    public void CuratedProfile_VegetarianOnlyFoodIsRejectedForVeganButAcceptedForVegetarian()
        => WithProfiles(() =>
        {
            Assert.False(DietSemanticValidationService.IsFoodCompatibleWithDietType("Queso fresco", "Dieta vegana", null, null, null, VegetarianOnlyId));
            Assert.True(DietSemanticValidationService.IsFoodCompatibleWithDietType("Queso fresco", "Dieta vegetariana", null, null, null, VegetarianOnlyId));
        });

    [Theory]
    [InlineData("Mejillones al vapor")]
    [InlineData("Pulpo cocido")]
    [InlineData("Calamares en su tinta")]
    [InlineData("Langostinos cocidos")]
    public void WithoutProfile_FallbackHeuristicNowCoversShellfishAndCephalopods(string name)
        => WithProfiles(() =>
        {
            Assert.False(DietSemanticValidationService.IsFoodCompatibleWithDietType(name, "Dieta vegana", null, null, null, NoProfileId));
            Assert.False(DietSemanticValidationService.IsFoodCompatibleWithDietType(name, "Dieta vegetariana", null, null, null, NoProfileId));
        });

    [Fact]
    public void OtherDietTypesIgnoreTheProfile()
        => WithProfiles(() =>
            Assert.True(DietSemanticValidationService.IsFoodCompatibleWithDietType("Delikatess", "Dieta mediterránea", null, null, null, NotVegetarianId)));
}
