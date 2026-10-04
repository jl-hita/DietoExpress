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

        Assert.Contains("private readonly PatientDocumentService _patientDocumentService;", source);
        Assert.Contains("PatientDocumentService patientDocumentService)", source);
        Assert.Contains("_patientDocumentService = patientDocumentService;", source);

        var bookingStart = source.IndexOf(
            "public async Task<ActionResult<PublicAppointmentConfirmationDto>> RequestPublicAppointment",
            StringComparison.Ordinal);
        var bookingEnd = source.IndexOf("[AllowAnonymous]", bookingStart + 1, StringComparison.Ordinal);

        Assert.True(bookingStart >= 0 && bookingEnd > bookingStart);

        var booking = source[bookingStart..bookingEnd];
        var commitPos = booking.IndexOf("await transaction.CommitAsync();", StringComparison.Ordinal);
        var provisioningPos = booking.IndexOf("CreateRequiredDocumentsAsync(", commitPos, StringComparison.Ordinal);

        Assert.True(commitPos >= 0);
        Assert.True(provisioningPos > commitPos);
        Assert.Contains("forClientCreation: true", booking[provisioningPos..]);
        Assert.Contains("includeAllRequired: false", booking[provisioningPos..]);
        Assert.Contains("appointment.tenant_id", booking[provisioningPos..]);
        Assert.Contains("appointment.client_id", booking[provisioningPos..]);
    }
}
