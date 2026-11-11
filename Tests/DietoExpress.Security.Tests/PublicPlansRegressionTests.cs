using Xunit;

namespace DietoExpress.Security.Tests;

public class PublicPlansRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    private static string Source() => File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "PublicPlansController.cs"));

    [Fact]
    public void PublicPlans_AreAnonymousCachedAndRateLimited()
    {
        var source = Source();
        Assert.Contains("[AllowAnonymous]", source);
        Assert.Contains("[EnableRateLimiting(\"public-read\")]", source);
        Assert.Contains("_cache.GetOrCreateAsync(\"public:plans\"", source);
    }

    [Fact]
    public void PublicPlans_NeverExposeStripeIdentifiersOrInternalPlans()
    {
        var source = Source();
        var response = source[source.IndexOf("public sealed record PublicPlanDto", StringComparison.Ordinal)..];

        // El DTO público solo lleva datos comerciales.
        Assert.DoesNotContain("Stripe", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Id,", response.Replace("MaxNutritionists", string.Empty));

        // Los planes internos o de prueba no se anuncian y solo se muestran los que realmente se pueden comprar.
        Assert.Contains("HiddenPlanCodes = [\"free\", \"demo_nutri\", \"trial_nutri\"]", source);
        Assert.Contains("p.active", source);
        Assert.Contains("p.monthly_price > 0 && p.yearly_price > 0", source);
        Assert.Contains("p.stripe_monthly_price_id != null", source);
        Assert.Contains("p.stripe_yearly_price_id != null", source);
    }

    [Fact]
    public void PublicPlans_ReadTheSameCatalogThatIsCharged()
    {
        // Debe leer subscription_plans (la tabla que usa el cobro), no precios escritos a mano en la web.
        Assert.Contains("_context.subscription_plans", Source());
        var landing = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "landing", "landing.component.html"));
        Assert.DoesNotContain("29,90", landing);
        Assert.DoesNotContain("79,90", landing);
        Assert.DoesNotContain("29.90", landing);
    }
}
