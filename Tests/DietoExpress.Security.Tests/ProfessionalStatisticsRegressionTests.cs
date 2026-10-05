using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class ProfessionalStatisticsRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    private static string ReadController()
        => File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "ProfessionalStatisticsController.cs"));

    [Fact]
    public void Statistics_AreTenantScopedAndSeparateProfessionalScopes()
    {
        var source = ReadController();

        Assert.Contains("[Authorize(Roles = " + "\"nutritionist,clinic_admin\"" + ")]", source);
        Assert.Contains("var tenantId = AuthHelpers.GetTenantId(User);", source);
        Assert.Contains("var isClinicAdmin = User.IsInRole(" + "\"clinic_admin\"" + ");", source);
        Assert.Contains("WHERE c.tenant_id=@tenant", source);
        Assert.Contains("ca.nutritionist_id = @user", source);
        Assert.Contains("ca.assigned_at < @to", source);
        Assert.Contains("ca.unassigned_at >= @from", source);
    }

    [Fact]
    public void Statistics_UseExistingOperationalSourcesWithoutDerivedTables()
    {
        var source = ReadController();

        Assert.Contains("FROM clients c", source);
        Assert.Contains("FROM patient_appointments pa", source);
        Assert.Contains("FROM patient_checkins pc", source);
        Assert.Contains("FROM biometrics b", source);
        Assert.Contains("generate_series(", source);
        Assert.DoesNotContain("CREATE TABLE", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClinicStatistics_KeepSubscriptionAndWorkloadMetricsTenantScoped()
    {
        var source = ReadController();

        Assert.Contains("FROM subscriptions s", source);
        Assert.Contains("s.tenant_id=@tenant", source);
        Assert.Contains("FROM subscription_payments sp", source);
        Assert.Contains("u.tenant_id=@tenant", source);
        Assert.Contains("u.role='nutritionist'", source);
        Assert.Contains("u.archived_at IS NULL", source);
        Assert.Contains("NutritionistWorkload", source);
    }

    [Fact]
    public void Statistics_RespectHistoricalNutritionistAssignments()
    {
        var source = ReadController();

        Assert.Contains("ca.assigned_at <= pc.submitted_at", source);
        Assert.Contains("ca.unassigned_at >= pc.submitted_at", source);
        Assert.Contains("ca.assigned_at < @to", source);
        Assert.Contains("ca.unassigned_at IS NULL", source);
    }

    [Fact]
    public void Statistics_ExposeConfigurableBoundedPeriodsAndMonthlySeries()
    {
        var source = ReadController();

        Assert.Contains("[FromQuery] DateOnly? from = null", source);
        Assert.Contains("[FromQuery] DateOnly? to = null", source);
        Assert.Contains("end.DayNumber - start.DayNumber > 365", source);
        Assert.Contains("result.Series.Add", source);
        Assert.Contains("ProfessionalStatisticsPointDto", source);
    }

    [Fact]
    public void Statistics_RouteAndNavigationAreProtectedBySubscriptionContext()
    {
        var routes = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "app.routes.ts"));
        var sidebar = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "sidebar", "sidebar.component.html"));

        Assert.Contains("path: 'statistics'", routes);
        Assert.Contains("StatisticsComponent", routes);
        Assert.Contains("canActivate: [SubscriptionGuard]", routes);
        Assert.Contains("routerLink=\"/statistics\"", sidebar);
    }
}
