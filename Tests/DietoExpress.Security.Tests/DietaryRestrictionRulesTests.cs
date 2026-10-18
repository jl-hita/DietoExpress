using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DietaryRestrictionRulesTests
{
    [Theory]
    [InlineData("frutos secos", "frutos secos")]
    [InlineData("FRUTOS SÉCOS", "frutos secos")]
    [InlineData("alergia a la leche", "alergia a la leche")]
    public void BuildForbiddenTerms_PreservesCompoundTermsAndNormalizesAccents(string allergy, string expected)
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms(allergy, false, false, false);
        Assert.Contains(expected, terms);
    }

    [Fact]
    public void BuildForbiddenTerms_DoesNotMatchAliasesInsideUnrelatedWords()
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms("nutrition advice", false, false, false);
        Assert.Contains("nutrition advice", terms);
        Assert.DoesNotContain("almendras", terms);
        Assert.DoesNotContain("tree nuts", terms);
    }

    [Fact]
    public void BuildForbiddenTerms_RecognizesMultipleAllergensInOneCompoundDeclaration()
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms("almond milk", false, false, false);
        Assert.Contains("almonds", terms);
        Assert.Contains("tree nuts", terms);
        Assert.Contains("lactosa", terms);
        Assert.Contains("leche", terms);
    }

    [Theory]
    [InlineData("frutos secos; leche", "frutos secos")]
    [InlineData("soja|mostaza", "mostaza")]
    public void BuildForbiddenTerms_UsesDeclarationSeparatorsWithoutSplittingPhrases(string allergies, string expected)
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms(allergies, false, false, false);
        Assert.Contains(expected, terms);
    }

    [Fact]
    public void BuildForbiddenTerms_DoesNotSplitCompoundAllergyIntoIndividualWords()
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms("frutos secos", false, false, false);
        Assert.Contains("frutos secos", terms);
        Assert.DoesNotContain("frutos", terms);
        Assert.DoesNotContain("secos", terms);
        Assert.Contains("almendras", terms);
        Assert.Contains("tree nuts", terms);
    }

    [Theory]
    [InlineData("Barrita de cacao", "tree_nut")]
    [InlineData("Crema vegetal", "peanut_allergy")]
    [InlineData("Bebida de soja", "soy")]
    public void IsFoodAllowed_RejectsStructuredAllergenFlagsEvenWhenNameDoesNotContainAllergen(string name, string flag)
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms("frutos secos, cacahuete, soja", false, false, false);
        Assert.False(DietaryRestrictionRules.IsFoodAllowed(name, "producto procesado", new[] { flag }, terms));
    }

    [Fact]
    public void IsFoodAllowed_UsesWordBoundariesInsteadOfSubstringMatches()
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms("soja", false, false, false);
        Assert.True(DietaryRestrictionRules.IsFoodAllowed("Sopa de verduras", "plato preparado", Array.Empty<string>(), terms));
    }

    [Theory]
    [InlineData("crustáceos", "crustaceans")]
    [InlineData("moluscos", "molluscs")]
    [InlineData("sésamo", "sesame")]
    [InlineData("mostaza", "mustard")]
    [InlineData("apio", "celery")]
    [InlineData("altramuces", "lupin")]
    [InlineData("sulfitos", "sulfites")]
    [InlineData("pescado", "fish")]
    [InlineData("huevo", "egg")]
    [InlineData("soja", "soy")]
    [InlineData("leche", "dairy")]
    [InlineData("cacahuete", "peanut")]
    [InlineData("frutos secos", "tree_nut")]
    [InlineData("gluten", "gluten")]
    public void IsFoodAllowed_RejectsStructuredFlagsForAllergenGroups(string declaredAllergy, string foodFlag)
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms(declaredAllergy, false, false, false);
        Assert.False(DietaryRestrictionRules.IsFoodAllowed("Producto procesado", "Otros", new[] { foodFlag }, terms));
    }

    [Theory]
    [InlineData("cacahuete", "en:peanuts")]
    [InlineData("frutos secos", "en:nuts")]
    [InlineData("leche", "en:milk")]
    [InlineData("huevo", "en:eggs")]
    [InlineData("soja", "en:soybeans")]
    [InlineData("gluten", "en:gluten")]
    [InlineData("pescado", "en:fish")]
    [InlineData("crustáceos", "en:crustaceans")]
    [InlineData("moluscos", "en:molluscs")]
    [InlineData("sésamo", "en:sesame")]
    [InlineData("mostaza", "en:mustard")]
    [InlineData("apio", "en:celery")]
    [InlineData("altramuces", "en:lupin")]
    [InlineData("sulfitos", "en:sulphites")]
    public void IsFoodAllowed_RejectsOpenFoodFactsAllergenTags(string declaredAllergy, string allergenTag)
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms(declaredAllergy, false, false, false);
        Assert.False(DietaryRestrictionRules.IsFoodAllowed("Producto genérico", "Otros", new[] { allergenTag }, terms));
    }

    [Fact]
    public void HasUnknownAllergenData_DistinguishesUnknownFromDeclaredTags()
    {
        Assert.True(DietaryRestrictionRules.HasUnknownAllergenData("cacahuete", null));
        Assert.True(DietaryRestrictionRules.HasUnknownAllergenData("frutos secos", Array.Empty<string>()));
        Assert.False(DietaryRestrictionRules.HasUnknownAllergenData("soja", new[] { "en:soybeans" }));
        Assert.False(DietaryRestrictionRules.HasUnknownAllergenData(null, null));
        Assert.False(DietaryRestrictionRules.HasUnknownAllergenData(" ", null));
    }

    [Fact]
    public void BuildForbiddenTerms_AddsIntoleranceRules()
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms(null, true, true, true);
        Assert.Contains("gluten", terms);
        Assert.Contains("trigo", terms);
        Assert.Contains("lactosa", terms);
        Assert.Contains("leche", terms);
        Assert.Contains("cebolla", terms);
    }
}