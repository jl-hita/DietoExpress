using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class AppointmentPushReminderRegressionTests
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
    public void AppointmentRemindersMustBeHostedAndPersistTheirIdempotencyLedger()
    {
        var program = Read("Anguloso.Server/Program.cs");
        var worker = Read("Anguloso.Server/Logica/AppointmentReminderWorker.cs");
        var schema = Read("Anguloso.Server/Logica/DatabaseBootstrap.cs");

        Assert.Contains("AddHostedService<AppointmentReminderWorker>()", program);
        Assert.Contains("a.status='confirmed'", worker);
        Assert.Contains("appointment_reminder_deliveries", worker);
        Assert.Contains("appointment-reminder:{item.Id}:patient", worker);
        Assert.Contains("CREATE TABLE IF NOT EXISTS appointment_reminder_deliveries", schema);
    }

    [Fact]
    public void BothPatientAndProfessionalMustHaveWebPushEnrollmentEndpoints()
    {
        var patientController = Read("Anguloso.Server/Controllers/PatientNotificationsController.cs");
        var professionalController = Read("Anguloso.Server/Controllers/ProfessionalPushController.cs");
        var professionalService = Read("Anguloso.Server/Logica/ProfessionalPushService.cs");
        var ui = Read("anguloso.client/src/app/app.component.ts");

        Assert.Contains("push-subscriptions", patientController);
        Assert.Contains("api/professional/push/subscriptions", professionalController);
        Assert.Contains("professional_push_subscriptions", professionalService);
        Assert.Contains("enableProfessionalPush()", ui);
        Assert.Contains("navigator.serviceWorker.register('/sw.js')", ui);
    }

    [Fact]
    public void ProfessionalPushSubscriptionsMustBeScopedToTheAuthenticatedTenantAndUser()
    {
        var controller = Read("Anguloso.Server/Controllers/ProfessionalPushController.cs");
        var service = Read("Anguloso.Server/Logica/ProfessionalPushService.cs");
        var schema = Read("Anguloso.Server/Logica/DatabaseBootstrap.cs");

        Assert.Contains("AuthHelpers.GetTenantId(User)", controller);
        Assert.Contains("AuthHelpers.GetUserId(User)", controller);
        Assert.Contains("WHERE tenant_id=@tenant AND user_id=@user", service);
        Assert.Contains("REFERENCES users(id) ON DELETE CASCADE", schema);
    }
}
