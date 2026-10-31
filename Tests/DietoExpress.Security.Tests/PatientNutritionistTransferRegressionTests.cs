using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class PatientNutritionistTransferRegressionTests
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
    public void NutritionistTransferCancelsOnlyFutureAppointmentsOfThePreviousProfessional()
    {
        var controller = Read("Anguloso.Server/Controllers/PatientNutritionistTransferController.cs");
        var start = controller.IndexOf("cancelledAppointmentIds = await", StringComparison.Ordinal);
        var end = controller.IndexOf("var clientUpdated =", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var cancellation = controller[start..end];

        Assert.Contains("a.client_id == workflow.ClientId", cancellation);
        Assert.Contains("a.tenant_id == workflow.TargetTenantId", cancellation);
        Assert.Contains("a.nutritionist_id == workflow.SourceNutritionistId", cancellation);
        Assert.Contains("a.starts_at > DateTime.UtcNow", cancellation);
        Assert.Contains("a.status == \"requested\" || a.status == \"confirmed\"", cancellation);
        Assert.Contains("SET status = 'cancelled'", cancellation);
        Assert.Contains("AND starts_at > NOW()", cancellation);
        Assert.Contains("AND status IN ('requested', 'confirmed')", cancellation);
        Assert.Contains("video_room_name = NULL", cancellation);
        Assert.Contains("video_room_url = NULL", cancellation);
        Assert.Contains("await transaction.CommitAsync()", controller);
    }

    [Fact]
    public void PreviousGoogleCalendarEventsAreCancelledAfterTheDatabaseCommit()
    {
        var controller = Read("Anguloso.Server/Controllers/PatientNutritionistTransferController.cs");
        var calendar = Read("Anguloso.Server/Logica/GoogleCalendarService.cs");
        var commit = controller.IndexOf("await transaction.CommitAsync()", StringComparison.Ordinal);
        var externalCancellation = controller.IndexOf("_googleCalendar.CancelAppointmentEventAsync", StringComparison.Ordinal);

        Assert.True(commit >= 0 && externalCancellation > commit);
        Assert.Contains("workflow.SourceNutritionistId", controller[externalCancellation..]);
        Assert.Contains("workflow.SourceTenantId", controller[externalCancellation..]);
        Assert.Contains('payload["status"] = "cancelled";', calendar);
        Assert.Contains("JsonSerializer.Deserialize<Dictionary<string, object?>>", calendar);
        Assert.Contains('"PUT"', calendar);
        Assert.Contains("var eventId = \"dieto\" + appointmentId;", calendar);
        Assert.Contains("if (existing == null) return;", calendar);
    }

    [Fact]
    public void TransferKeepsTheTargetAppointmentAndPastAppointmentsOutOfCancellationScope()
    {
        var controller = Read("Anguloso.Server/Controllers/PatientNutritionistTransferController.cs");
        var start = controller.IndexOf("cancelledAppointmentIds = await", StringComparison.Ordinal);
        var end = controller.IndexOf("var clientUpdated =", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var cancellation = controller[start..end];

        Assert.Contains("workflow.SourceNutritionistId", cancellation);
        Assert.DoesNotContain("workflow.TargetNutritionistId", cancellation);
        Assert.Contains("starts_at > DateTime.UtcNow", cancellation);
        Assert.DoesNotContain("DELETE FROM patient_appointments", cancellation);
    }
}
