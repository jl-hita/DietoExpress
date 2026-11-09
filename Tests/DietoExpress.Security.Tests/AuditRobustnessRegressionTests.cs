using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class AuditRobustnessRegressionTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void ManualAppointmentCreationUsesTheSharedLockAndTransactionalOutbox()
    {
        var source = Read("Anguloso.Server/Controllers/ProfessionalConsultationActionsController.cs");
        var action = source[source.IndexOf("public async Task<IActionResult> CreateAppointment", StringComparison.Ordinal)..];
        Assert.Contains("BeginTransactionAsync", action);
        Assert.Contains("_appointmentConcurrency.LockAsync", action);
        Assert.True(action.IndexOf("_appointmentConcurrency.LockAsync", StringComparison.Ordinal) <
                    action.IndexOf("HasAppointmentOverlapAsync", StringComparison.Ordinal));
        Assert.Contains("TransactionalOutbox.EnqueueAsync", action);
        Assert.True(action.IndexOf("TransactionalOutbox.EnqueueAsync", StringComparison.Ordinal) <
                    action.IndexOf("transaction.CommitAsync", StringComparison.Ordinal));
        Assert.DoesNotContain("_automation.PublishEventAsync", action);
        Assert.Contains("_videoMeetings.DeleteRoomAsync", action);
    }

    [Fact]
    public void ConsultationCompletionEnqueuesItsAutomationInTheBusinessTransaction()
    {
        var source = Read("Anguloso.Server/Controllers/ProfessionalConsultationsController.cs");
        var action = source[source.IndexOf("[HttpPost(\"appointment/{appointmentId:int}/complete\")", StringComparison.Ordinal)..];
        Assert.Contains("TransactionalOutbox.EnqueueAsync", action);
        Assert.True(action.IndexOf("TransactionalOutbox.EnqueueAsync", StringComparison.Ordinal) <
                    action.IndexOf("transaction.CommitAsync", StringComparison.Ordinal));
        Assert.DoesNotContain("_automationService.PublishEventAsync", action);
    }

    [Fact]
    public void TransactionalOutboxConsumesAppointmentEvents()
    {
        var source = Read("Anguloso.Server/Logica/TransactionalOutboxWorker.cs");
        Assert.Contains("case \"appointment_confirmed\"", source);
        Assert.Contains("case \"appointment_completed\"", source);
        Assert.Contains("\"appointment.confirmed\"", source);
        Assert.Contains("\"appointment.completed\"", source);
    }

    [Fact]
    public void LoadSuggestionsAreAppliedAsOneLockedTransaction()
    {
        var source = Read("Anguloso.Server/Logica/TrainingAutoAdaptationApplier.cs");
        var action = source[source.IndexOf("public async Task<int> ApplyLoadSuggestionsAsync", StringComparison.Ordinal)..];
        Assert.Contains("BeginTransactionAsync", action);
        Assert.Contains("GetDbTransaction()", action);
        Assert.Contains("GetRecommendationsAsync", action);
        Assert.Contains("FOR UPDATE OF s", action);
        Assert.Contains("p.status = 'active'", action);
        Assert.Contains("s.scheduled_date > CURRENT_DATE", action);
        Assert.Contains("await efTransaction.CommitAsync", action);
    }
}
