using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class FoodCulinaryClassifierTests
{
    private static FoodProfileData? Classify(string name, double kcal, double? p = 5, double? c = 20, double? f = 5)
        => FoodCulinaryClassifier.Classify(name, null, kcal, p, c, f);

    [Theory]
    [InlineData("Arroz", 386, 7, 86, 0.9, "Starch", "arroz", "raw")]
    [InlineData("Arroz integral, hervido", 112, 2.6, 22.9, 0.9, "Starch", "arroz", "cooked")]
    [InlineData("Pasta alimenticia, cruda", 352, 12.5, 70.9, 1.4, "Starch", "pasta", "raw")]
    [InlineData("Lenteja, seca, cruda", 309, 25, 49, 1, "Legume", "legumbre", "raw")]
    [InlineData("Lenteja, en conserva", 74, 6.3, 11.4, 0.2, "Legume", "legumbre", "cooked")]
    [InlineData("Pollo, pechuga, con piel, crudo", 105, 23, 0, 1, "Protein", "ave", "raw")]
    [InlineData("Merluza fresca", 64, 11.9, 0, 1.8, "Protein", "pescado_blanco", "raw")]
    [InlineData("Salmón, plancha", 179, 20.2, 0, 11, "Protein", "pescado_azul", "cooked")]
    [InlineData("Gamba, hervida", 96, 20, 0, 2, "Protein", "marisco", "cooked")]
    [InlineData("Huevo de gallina fresco", 149, 12.5, 0, 11.1, "Protein", "huevo", "raw")]
    [InlineData("Ternera, solomillo, sin grasa, crudo", 109, 20, 0, 3, "Protein", "carne_roja", "raw")]
    [InlineData("Yogur, desnatado, sabor natural", 46, 4, 6, 0.2, "Dairy", "lacteo", "ready")]
    [InlineData("Pan integral", 250, 10.9, 44, 3, "Bread", "pan", "ready")]
        [InlineData("Almendra, cruda", 586, 19, 6, 45, "Nut", "frutos_secos", "ready")]
    [InlineData("Manzana", 50, 0.3, 12, 0.1, "Fruit", "fruta", "raw")]
    [InlineData("Lechuga", 16, 1.1, 1.4, 0.6, "Vegetable", "verdura", "raw")]
    public void Classify_RecognizesCommonSpanishFoodsWithTheirRoleAndState(string name, double kcal, double p, double c, double f, string role, string family, string state)
    {
        var profile = FoodCulinaryClassifier.Classify(name, null, kcal, p, c, f);
        Assert.NotNull(profile);
        Assert.True(profile!.IsUsable, profile.ExcludedReason);
        Assert.Equal(role, profile.Role);
        Assert.Equal(family, profile.Family);
        Assert.Equal(state, profile.State);
    }

    [Fact]
    public void Classify_AcceptsOilsWithMissingProteinButNotOtherFoods()
    {
        var oil = FoodCulinaryClassifier.Classify("Aceite de oliva virgen extra", null, 884, null, 0, 100);
        Assert.NotNull(oil);
        Assert.True(oil!.IsUsable);
        Assert.True(oil.IsStaple);
        Assert.Equal("Oil", oil.Role);
    }

    [Theory]
    [InlineData("Arroz, hervido", 392, 7.5, 85.3, 1.7)]          // kcal del crudo etiquetadas como hervido
    [InlineData("Lenteja, hervida", 331, 23, 54.8, 1.7)]
    [InlineData("Garbanzo, hervido", 359, 8.9, 18.7, 2.5)]
    [InlineData("Judia verde, hervida", 83, 1.8, 3.9, 6.8)]       // grasa incoherente para una verdura
    public void Classify_FlagsKnownFoodsWhoseCatalogValuesAreInconsistent(string name, double kcal, double p, double c, double f)
    {
        var profile = FoodCulinaryClassifier.Classify(name, null, kcal, p, c, f);
        Assert.NotNull(profile);
        Assert.False(profile!.IsUsable);
        Assert.NotNull(profile.ExcludedReason);
    }

    [Theory]
    [InlineData("Pollo, frito")]
    [InlineData("Pollo, fiambre")]
    [InlineData("Grasa de pollo")]
    [InlineData("Merluza, rebozada, frita")]
    [InlineData("Salmón ahumado")]
    [InlineData("Chorizo")]
    [InlineData("Cerdo, panceta, cruda")]
    [InlineData("Zumo de naranja")]
    [InlineData("Almendra, frita, salada")]
    [InlineData("Huevo de codorniz, entero, crudo")]
    [InlineData("Huevo de gallina, clara, cruda")]
    [InlineData("Arroz con leche")]
    [InlineData("Leche de almendra")]
    [InlineData("Yogur líquido, con frutas s/e")]
    [InlineData("Yogur, desnatado, con frutas")]
    [InlineData("Cereales desayuno base de maíz y miel")]
    [InlineData("Pan blanco, de barra")]
    [InlineData("Miel")]
    [InlineData("Gelatina")]
    public void Classify_DoesNotAdmitProcessedOrUnusualProducts(string name)
    {
        var profile = Classify(name, 200);
        Assert.True(profile == null || !profile.IsUsable);
    }

    [Fact]
    public void Normalize_MatchesTheHistoricalRegexNormalization()
    {
        foreach (var value in new[] { "Ñoquis de patata", "  Crème   brûlée ", "FRUTOS_SECOS", "tree-nut/almond", "Pan 100% integral", "", "   " })
        {
            var historical = System.Text.RegularExpressions.Regex.Replace(
                RemoveMarks(value).ToLowerInvariant().Replace('_', ' '), "[^a-z0-9]+", " ").Trim();
            Assert.Equal(string.IsNullOrWhiteSpace(value) ? string.Empty : historical, DietaryRestrictionRules.Normalize(value));
        }

        static string RemoveMarks(string v)
        {
            var d = v.Normalize(System.Text.NormalizationForm.FormD);
            return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        }
    }

    [Theory]
    [InlineData("Pan integral", "milk")]
    [InlineData("Yogur natural", "milk")]
    [InlineData("Almendras", "tree nut")]
    [InlineData("Salsa de soja", "soja")]
    [InlineData("Plátano", "platano")]
    public void ExclusionMatcher_HasTheSameSemanticsAsIsFoodAllowed(string food, string term)
    {
        var flags = new[] { "en:milk", "dairy" };
        var terms = new[] { term, "lact", "gluten" };
        var matcher = new DietaryRestrictionRules.ExclusionMatcher(terms);
        Assert.Equal(
            DietaryRestrictionRules.IsFoodAllowed(food, "Alimentos", flags, terms),
            matcher.IsAllowed(food, "Alimentos", flags));
        Assert.Equal(
            DietaryRestrictionRules.IsFoodAllowed(food, "Alimentos", null, terms),
            matcher.IsAllowed(food, "Alimentos", null));
    }

    [Fact]
    public void ShellfishAllergyDeclaredAsMariscoExcludesCrustaceansAndMolluscs()
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms("marisco", false, false, false);
        Assert.False(DietaryRestrictionRules.IsFoodAllowed("Gamba, hervida", "x", new[] { "crustaceos" }, terms));
        Assert.False(DietaryRestrictionRules.IsFoodAllowed("Calamar, crudo", "x", new[] { "moluscos" }, terms));
        Assert.True(DietaryRestrictionRules.IsFoodAllowed("Merluza fresca", "x", new[] { "pescado" }, terms));
    }

    [Fact]
    public async Task Seeder_IsIdempotentAndNeverOverwritesManualProfiles()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>().UseInMemoryDatabase($"seeder-{Guid.NewGuid():N}").Options;
        await using var context = new angulosodbContext(options);
        context.foods.AddRange(BedcaLikeCatalog.Create());
        await context.SaveChangesAsync();

        var first = await FoodCulinaryProfileSeeder.EnsureSeededAsync(context, NullLogger.Instance);
        Assert.True(first > 60);
        Assert.Equal(0, await FoodCulinaryProfileSeeder.EnsureSeededAsync(context, NullLogger.Instance));

        var apple = await context.foods.SingleAsync(f => f.name == "Manzana");
        var row = await context.food_culinary_profile.SingleAsync(p => p.food_id == apple.id);
        row.origin = "manual"; row.commonness = 1; row.classifier_version = 0;
        await context.SaveChangesAsync();
        await FoodCulinaryProfileSeeder.EnsureSeededAsync(context, NullLogger.Instance);
        Assert.Equal(1, (await context.food_culinary_profile.SingleAsync(p => p.food_id == apple.id)).commonness);

    }
}
