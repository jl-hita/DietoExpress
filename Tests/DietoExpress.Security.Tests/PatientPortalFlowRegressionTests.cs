using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class PatientPortalFlowRegressionTests
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

    private static string Read(string path) => File.ReadAllText(Path.Combine(RepoRoot, path));

    [Fact]
    public void PatientMagicLinkCanBeReusedUntilItsExpiry()
    {
        var controller = Read("Anguloso.Server/Controllers/PatientPortalController.cs");
        var start = controller.IndexOf("public async Task<ActionResult<PatientAuthResponseDto>> Authenticate", StringComparison.Ordinal);
        var end = controller.IndexOf("[HttpPost(\"reviews\")]", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var auth = controller[start..end];

        Assert.Contains("PatientPortalAccessService.HashAccessToken(request.Token)", auth);
        Assert.Contains("c.access_token_expires_at > DateTime.UtcNow", auth);
        Assert.DoesNotContain("ExecuteUpdateAsync", auth);
        Assert.DoesNotContain("SetProperty(c => c.access_token, (string?)null)", auth);
        Assert.Contains("client.access_token_expires_at", auth);
    }

    [Fact]
    public void WeeklyCheckinRequiresACompletedConsultationAndOnlyAllowsOneSubmissionPerWeek()
    {
        var controller = Read("Anguloso.Server/Controllers/PatientCheckinsController.cs");
        var model = Read("Anguloso.Server/Models/BillingModelConfiguration.cs");
        var component = Read("anguloso.client/src/app/componentes/patient-portal/patient-checkin/patient-checkin.component.html");

        Assert.Contains("[HttpGet(\"eligibility\")]", controller);
        Assert.Contains("a.status == \"completed\"", controller);
        Assert.Contains("if (!hasCompletedConsultation)", controller);
        Assert.Contains("if (alreadySubmitted)", controller);
        Assert.Contains("patient_checkins_client_week_key", model);
        Assert.Contains("Podrás rellenarla de nuevo la próxima semana", component);
        Assert.Contains("*ngIf=\"!currentCheckin\"", component);
    }

    [Fact]
    public void OnlineAppointmentShowsCountdownBeforeVideoAccessOpens()
    {
        var component = Read("anguloso.client/src/app/componentes/patient-portal/patient-appointments/patient-appointments.component.ts");
        var template = Read("anguloso.client/src/app/componentes/patient-portal/patient-appointments/patient-appointments.component.html");
        var dashboard = Read("anguloso.client/src/app/componentes/patient-portal/patient-portal.component.html");
        var dashboardComponent = Read("anguloso.client/src/app/componentes/patient-portal/patient-portal.component.ts");

        Assert.Contains("videoAccessCountdown", component);
        Assert.Contains("new Date(appointment.startsAt).getTime() - 15 * 60_000", component);
        Assert.Contains("videoAccessState(appointment) !== 'available'", template);
        Assert.Contains("Disponible en {{ videoAccessCountdown(appointment) }}", template);
        Assert.Contains("El botón se activará 15 minutos antes de la cita.", template);
        Assert.Contains("home-video-access-card", dashboard);
        Assert.Contains("homeVideoAccessCountdown", dashboardComponent);
    }

    [Fact]
    public void PatientCommercialConsentUsesPatientIdentityAndDistinguishesNoChoiceFromRejection()
    {
        var controller = Read("Anguloso.Server/Controllers/CommercialCommunicationController.cs");
        Assert.Contains("User.FindFirst(\"clientId\")", controller);
        Assert.Contains("User.IsInRole(\"patient\")", controller);
        Assert.DoesNotContain("AuthHelpers.GetUserId(User)", controller);
        Assert.DoesNotContain("c.user_id=@user", controller);
        Assert.Contains("(p.client_id IS NOT NULL)", controller);
        Assert.Contains("hasDecision = reader.GetBoolean(3)", controller);
        Assert.Contains("_service.SetEmailPreferenceAsync(parsedClientId", controller);
    }
}
