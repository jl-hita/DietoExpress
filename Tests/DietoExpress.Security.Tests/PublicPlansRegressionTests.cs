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

    private static string ReadRepo(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot, .. parts]));

    [Fact]
    public void OnlyTheIndividualPlanOffersTheFreeTrial()
    {
        // No existe prueba de clínica: el sistema no puede darla, así que la web no debe ofrecerla.
        var source = Source();
        Assert.Contains("TrialPlanCode = \"nutri_full\"", source);
        Assert.Contains("p.code == TrialPlanCode && trialDays is > 0 ? trialDays : null", source);

        var landing = ReadRepo("anguloso.client", "src", "app", "componentes", "landing", "landing.component.html");
        Assert.Contains("plan.trialDays ? 'Probar ' + plan.trialDays + ' días gratis' : 'Crear cuenta y contratar'", landing);
        Assert.Contains("Sin periodo de prueba", landing);
        // Los días no están escritos en la plantilla: vienen del catálogo.
        Assert.DoesNotContain("30 días", landing);
        Assert.DoesNotContain("7 días", landing);
    }

    [Fact]
    public void FreeTrial_IsThirtyDaysAndIsNotResetAtEveryStartup()
    {
        var bootstrap = ReadRepo("Anguloso.Server", "Logica", "DatabaseBootstrap.cs");
        Assert.Contains("UpgradeFreeTrialLengthV1(context, logger);", bootstrap);
        Assert.Contains("free-trial-30-days-v1", bootstrap);
        Assert.Contains("SET trial_days = 30", bootstrap);

        // La migración antigua solo fija los días la primera vez: antes los reescribía en cada arranque y anulaba lo que
        // SuperAdmin hubiera editado.
        var v3Start = bootstrap.IndexOf("public static void UpgradeSaaSSchemaV3(", StringComparison.Ordinal);
        var v3 = bootstrap[v3Start..bootstrap.IndexOf("public static void UpgradeFreeTrialLengthV1(", v3Start, StringComparison.Ordinal)];
        Assert.Contains("firstApplication", v3);
        Assert.Contains("if (firstApplication)", v3);

        var auth = ReadRepo("Anguloso.Server", "Controllers", "AuthController.cs");
        Assert.Contains("freePlan.trial_days ?? 30", auth);
        Assert.Contains("freePlanGoogle.trial_days ?? 30", auth);
        Assert.DoesNotContain("trial_days ?? 7", auth);
    }
}
