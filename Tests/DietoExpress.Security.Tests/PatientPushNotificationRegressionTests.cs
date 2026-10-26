using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class PatientPushNotificationRegressionTests
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
    public void PushTokensArePatientAuthenticatedAndCanBeRevoked()
    {
        var controller = Read("Anguloso.Server/Controllers/PatientPortalController.cs");
        var register = controller.IndexOf("RegisterPushToken", StringComparison.Ordinal);
        var revoke = controller.IndexOf("RevokePushToken", StringComparison.Ordinal);

        Assert.True(register >= 0 && revoke > register);
        Assert.Contains("[Authorize(Roles = \"patient\")]", controller[register - 120..register]);
        Assert.Contains("ON CONFLICT (token) DO UPDATE", controller);
        Assert.Contains("DELETE FROM patient_push_tokens WHERE client_id =", controller);
        Assert.Contains("token.Length > 4096", controller);
        Assert.Contains("portal_token_version = EXCLUDED.portal_token_version", controller);
    }

    [Fact]
    public void PushDeliveryUsesServerCredentialsAndDoesNotExposeMessageContent()
    {
        var service = Read("Anguloso.Server/Logica/PatientPushService.cs");
        var messages = Read("Anguloso.Server/Controllers/PatientMessagesController.cs");
        var bootstrap = Read("Anguloso.Server/Logica/DatabaseBootstrap.cs");
        var appointments = Read("Anguloso.Server/Controllers/AppointmentsController.cs");

        Assert.Contains("DIETOEXPRESS_FCM_PROJECT_ID", service);
        Assert.Contains("DIETOEXPRESS_FCM_SERVICE_ACCOUNT_PATH", service);
        Assert.Contains("https://fcm.googleapis.com/v1/projects/", service);
        Assert.Contains("Tienes un nuevo mensaje de tu profesional.", service);
        Assert.Contains("patient_push_tokens", bootstrap);
        Assert.Contains("p.portal_token_version = c.portal_token_version", service);
        Assert.Contains("data = new { title, body, target }", service);
        Assert.DoesNotContain("notification = new { title, body }", service);
        Assert.Contains("await ownedTransaction.CommitAsync()", messages);
        Assert.Contains("NotifyPatientOfNewMessageAsync", messages);
        Assert.Contains("Cita cancelada", appointments);
        Assert.Contains("Tu cita es en 15 minutos", Read("Anguloso.Server/Logica/AppointmentReminderWorker.cs"));
        Assert.DoesNotContain("LogInformation(.*token", service);
    }
}
