using Xunit;

namespace DietoExpress.Security.Tests;

public class PublicBookingRegressionTests
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

    [Fact]
    public void PublicBooking_ProvisionsClientCreationDocumentsAfterReservationCommit()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "Anguloso.Server", "Controllers", "DirectoryController.cs"));

        Assert.Contains("AutomationService _automationService;", source);

        var bookingStart = source.IndexOf(
            "public async Task<ActionResult<PublicAppointmentConfirmationDto>> RequestPublicAppointment",
            StringComparison.Ordinal);
        var bookingEnd = source.IndexOf("[AllowAnonymous]", bookingStart + 1, StringComparison.Ordinal);

        Assert.True(bookingStart >= 0 && bookingEnd > bookingStart);

        var booking = source[bookingStart..bookingEnd];
        var commitPos = booking.IndexOf("await transaction.CommitAsync();", StringComparison.Ordinal);
        var schedulingPos = booking.IndexOf("ScheduleActionAsync(", commitPos, StringComparison.Ordinal);

        Assert.True(commitPos >= 0);
        Assert.True(schedulingPos > commitPos);
        Assert.Contains("provision_patient_documents", booking[schedulingPos..]);
        Assert.Contains("appointment.tenant_id", booking[schedulingPos..]);
        Assert.Contains("appointment.client_id", booking[schedulingPos..]);
        Assert.Contains("ForClientCreation: true", booking[schedulingPos..]);
        Assert.Contains("IncludeAllRequired: false", booking[schedulingPos..]);
        Assert.Contains("maxAttempts: 8", booking[schedulingPos..]);
        Assert.Contains("documents:provision:", booking[schedulingPos..]);
    }
}
