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
    [InlineData("Barrita de cacao", new[] { "tree_nut" })]
    [InlineData("Crema vegetal", new[] { "peanut_allergy" })]
    [InlineData("Bebida de soja", new[] { "soy" })]
    public void IsFoodAllowed_RejectsStructuredAllergenFlagsEvenWhenNameDoesNotContainAllergen(string name, string[] flags)
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms("frutos secos, cacahuete, soja", false, false, false);
        Assert.False(DietaryRestrictionRules.IsFoodAllowed(name, "producto procesado", flags, terms));
    }

    [Fact]
    public void IsFoodAllowed_UsesWordBoundariesInsteadOfSubstringMatches()
    {
        var terms = DietaryRestrictionRules.BuildForbiddenTerms("soja", false, false, false);
        Assert.True(DietaryRestrictionRules.IsFoodAllowed("Sopa de verduras", "plato preparado", Array.Empty<string>(), terms));
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