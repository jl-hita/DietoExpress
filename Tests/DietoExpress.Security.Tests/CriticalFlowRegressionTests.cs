using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Regression guard for the 1.0 critical journey. The application already exposes
/// these flows through independent modules; this test prevents future refactors from
/// silently breaking the required end-to-end chain or removing its security gates.
/// </summary>
public sealed class CriticalFlowRegressionTests
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

    private static string Read(string path)
        => File.ReadAllText(Path.Combine(RepoRoot, path));

    [Fact]
    public void CriticalJourney_MustKeepAuthenticationAndOnboardingEntryPoints()
    {
        var auth = Read("Anguloso.Server/Controllers/AuthController.cs");
        var onboarding = Read("anguloso.client/src/app/componentes/onboarding/onboarding.component.ts");

        Assert.Contains("[EnableRateLimiting(\"auth\")]", auth);
        Assert.Contains("HttpOnly = true", auth);
        Assert.Contains("SameSite = SameSiteMode.Strict", auth);
        Assert.Contains("key: 'schedule'", onboarding);
        Assert.Contains("key: 'first-patient'", onboarding);
        Assert.Contains("router.navigate", onboarding);
    }

    [Fact]
    public void CriticalJourney_MustKeepPatientDietAndAppointmentBoundaries()
    {
        var clients = Read("Anguloso.Server/Controllers/ClientsController.cs");
        var diets = Read("Anguloso.Server/Controllers/DietsController.cs");
        var appointments = Read("Anguloso.Server/Controllers/AppointmentsController.cs");

        Assert.Contains("tenant_id", clients);
        Assert.Contains("nutritionist_id", clients);
        Assert.Contains("tenant_id", diets);
        Assert.Contains("nutritionist_id", diets);
        Assert.Contains("tenant_id", appointments);
        Assert.Contains("nutritionist_id", appointments);
    }

    [Fact]
    public void CriticalJourney_MustKeepConsultationToFollowupChain()
    {
        var consultation = Read("Anguloso.Server/Controllers/ProfessionalConsultationsController.cs");
        var dashboard = Read("Anguloso.Server/Controllers/ProfessionalDashboardController.cs");

        Assert.Contains("CONSULTATION_START", consultation);
        Assert.Contains("CONSULTATION_PROGRESS", consultation);
        Assert.Contains("CONSULTATION_COMPLETE", consultation);
        Assert.Contains("appointment.completed", consultation);
        Assert.Contains("UpcomingAppointments", dashboard);
        Assert.Contains("PendingCheckins", dashboard);
    }

    [Fact]
    public void CriticalJourney_MustKeepOnlineConsultationAuthorizationAndLifecycle()
    {
        var appointments = Read("Anguloso.Server/Controllers/AppointmentsController.cs");
        var program = Read("Anguloso.Server/Program.cs");

        Assert.Contains("connection", appointments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connectionMinutes", appointments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("final", appointments, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("camera=(self)", program);
        Assert.Contains("microphone=(self)", program);
        Assert.Contains("geolocation=()", program);
    }

    [Fact]
    public void CriticalJourney_MustKeepPublicDiscoveryBookingAndPortalAccess()
    {
        var directory = Read("Anguloso.Server/Controllers/DirectoryController.cs");
        var portal = Read("Anguloso.Server/Controllers/PatientPortalController.cs");

        Assert.Contains("[AllowAnonymous]", directory);
        Assert.Contains("RequestPublicAppointment", directory);
        Assert.Contains("CreateAccessLinkAsync", directory);
        Assert.Contains("HashAccessToken", portal);
    }

    [Fact]
    public void CriticalJourney_MustKeepLegalGateBeforeConsultationAndContractualCheckout()
    {
        var consultation = Read("Anguloso.Server/Controllers/ProfessionalConsultationsController.cs");
        var checkout = Read("Anguloso.Server/Controllers/StripeController.cs");
        var legal = Read("Anguloso.Server/Controllers/LegalDocumentsController.cs");

        Assert.Contains("GetPendingSignatureDocumentsBeforeConsultationAsync", consultation);
        Assert.Contains("interaction_type='acceptance'", checkout);
        Assert.Contains("saas_terms", checkout);
        Assert.Contains("readiness", legal, StringComparison.OrdinalIgnoreCase);
    }
}
