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
        Assert.Contains("GetClinicAdminIdsAsync(item.TenantId, ct)", worker);
        Assert.Contains("role=\'clinic_admin\'", worker);
    }

    [Fact]
    public void BothPatientAndProfessionalMustHaveWebPushEnrollmentEndpoints()
    {
        var patientController = Read("Anguloso.Server/Controllers/PatientNotificationsController.cs");
        var professionalController = Read("Anguloso.Server/Controllers/ProfessionalPushController.cs");
        var professionalService = Read("Anguloso.Server/Logica/ProfessionalPushService.cs");
        var ui = Read("anguloso.client/src/app/app.component.ts");

        Assert.Contains("push-subscriptions", patientController);
        Assert.Contains("Route(\"api/professional/push\")", professionalController);
        Assert.Contains("HttpPost(\"subscriptions\")", professionalController);
        Assert.Contains("professional_push_subscriptions", professionalService);
        Assert.Contains("enableProfessionalPush()", ui);
        Assert.Contains("navigator.serviceWorker.register('/sw.js')", ui);
    }

    [Fact]
    public void TwentyFourHourReminderMustBeHostedAndUseItsOwnDurableIdempotencyLedger()
    {
        var program = Read("Anguloso.Server/Program.cs");
        var worker = Read("Anguloso.Server/Logica/AppointmentDayReminderWorker.cs");
        var schema = Read("Anguloso.Server/Logica/DatabaseBootstrap.cs");

        Assert.Contains("AddHostedService<AppointmentDayReminderWorker>()", program);
        Assert.Contains("INTERVAL '23 hours 45 minutes'", worker);
        Assert.Contains("appointment_day_reminder_deliveries", worker);
        Assert.Contains("appointment-reminder-24h:{item.Id}:patient", worker);
        Assert.Contains("CREATE TABLE IF NOT EXISTS appointment_day_reminder_deliveries", schema);
        Assert.Contains("SendToClientAsync", worker);
        Assert.Contains("SendNotificationAsync", worker);
    }

    [Fact]
    public void EventPushesMustRespectPatientPreferencesAndSupportProfessionalAutomation()
    {
        var patientPush = Read("Anguloso.Server/Logica/PatientPushService.cs");
        var professionalPush = Read("Anguloso.Server/Logica/ProfessionalPushService.cs");
        var automation = Read("Anguloso.Server/Logica/AutomationService.cs");
        var worker = Read("Anguloso.Server/Logica/AutomationWorker.cs");
        var actions = Read("Anguloso.Server/Logica/AutomationActions.cs");

        Assert.Contains("patient_communication_preferences", patientPush);
        Assert.Contains("pref.push_enabled", patientPush);
        Assert.Contains("SendNotificationAsync", professionalPush);
        Assert.Contains("new ProfessionalPushAction", automation);
        Assert.Contains("case \"push_professional\"", worker);
        Assert.Contains("record ProfessionalPushAction", actions);
        Assert.Contains("\"appointment.requested\" => new AutomationRuleConfig(true, null, \"assigned_professional\", [\"in_app\"])", automation);
        Assert.Contains("Nueva solicitud de cita", Read("Anguloso.Server/Controllers/AppointmentsController.cs"));
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
