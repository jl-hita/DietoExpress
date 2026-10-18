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
    public void MergeKnownAllergenTags_DoesNotEraseKnownTagsWhenProviderReturnsNoTags()
    {
        var merged = DietaryRestrictionRules.MergeKnownAllergenTags(
            new[] { "en:milk", "en:peanuts" }, Array.Empty<string>());
        Assert.Equal(new[] { "en:milk", "en:peanuts" }, merged);
    }

    [Fact]
    public void MergeKnownAllergenTags_LeavesNewFoodUnknownWhenProviderHasNoTags()
    {
        Assert.Null(DietaryRestrictionRules.MergeKnownAllergenTags(null, null));
        Assert.Null(DietaryRestrictionRules.MergeKnownAllergenTags(null, Array.Empty<string>()));
    }

    [Fact]
    public void MergeKnownAllergenTags_UsesNonEmptyIncomingTagsAndDeduplicates()
    {
        var merged = DietaryRestrictionRules.MergeKnownAllergenTags(
            new[] { "en:milk" }, new[] { "en:peanuts", "en:PEANUTS" });
        Assert.Equal(new[] { "en:peanuts" }, merged);
    }

    [Fact]
    public void MergeKnownAllergenTags_CanPreserveTraceTagsIndependentlyFromAllergens()
    {
        var allergens = DietaryRestrictionRules.MergeKnownAllergenTags(
            new[] { "en:milk" }, Array.Empty<string>());
        var traces = DietaryRestrictionRules.MergeKnownAllergenTags(
            new[] { "en:peanuts" }, Array.Empty<string>());

        Assert.Equal(new[] { "en:milk" }, allergens);
        Assert.Equal(new[] { "en:peanuts" }, traces);
        Assert.DoesNotContain("en:peanuts", allergens!);
        Assert.DoesNotContain("en:milk", traces!);
    }

    [Fact]
    public void HasKnownAllergenTags_RequiresAtLeastOneNonBlankTag()
    {
        Assert.False(DietaryRestrictionRules.HasKnownAllergenTags(null));
        Assert.False(DietaryRestrictionRules.HasKnownAllergenTags(Array.Empty<string>()));
        Assert.False(DietaryRestrictionRules.HasKnownAllergenTags(new[] { " ", "" }));
        Assert.True(DietaryRestrictionRules.HasKnownAllergenTags(new[] { "en:milk" }));
    }

    [Theory]
    [InlineData("partial", true)]
    [InlineData("stale", true)]
    [InlineData("unknown", true)]
    [InlineData("reported", false)]
    public void HasUnknownAllergenData_UsesOpenFoodFactsCompletenessStatus(string status, bool expected)
    {
        Assert.Equal(expected, DietaryRestrictionRules.HasUnknownAllergenData(
            "cacahuete", new[] { "en:peanuts" }, status));
    }

    [Fact]
    public void HasUnknownAllergenData_AllowsCompleteDeclarationWithNoAllergenTags()
    {
        Assert.False(DietaryRestrictionRules.HasUnknownAllergenData("cacahuete, leche", Array.Empty<string>(), "reported"));
        Assert.True(DietaryRestrictionRules.HasUnknownAllergenData("cacahuete, leche", Array.Empty<string>(), "partial"));
        Assert.True(DietaryRestrictionRules.HasUnknownAllergenData("cacahuete, leche", Array.Empty<string>(), "unknown"));
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