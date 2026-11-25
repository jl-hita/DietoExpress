using System.Reflection;
using Anguloso.Server.Controllers;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>Cálculo de huecos, señales de seguimiento y acceso a videollamada del flujo de consulta.</summary>
public sealed class ConsultationFlowFixesTests
{
    private static string Read(string path)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Anguloso.Server", "Program.cs"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d?.FullName ?? throw new InvalidOperationException(), path));
    }

    [Theory]
    [InlineData("22:00", "23:00", 60, 60, new[] { "22:00" })]
    [InlineData("23:00", "23:45", 15, 15, new[] { "23:00", "23:15", "23:30" })]
    [InlineData("22:00", "23:59", 120, 30, new string[0])]
    [InlineData("09:00", "10:30", 60, 30, new[] { "09:00", "09:30" })]
    [InlineData("09:00", "10:00", 30, 0, new string[0])]
    public void SlotsNeverWrapPastMidnight(string start, string end, int duration, int step, string[] expected)
    {
        // Take acota la enumeración para que una regresión falle en lugar de colgar la ejecución.
        var slots = AvailabilitySlots.StartTimes(TimeOnly.Parse(start), TimeOnly.Parse(end), duration, step)
            .Take(200).Select(t => t.ToString("HH:mm"));

        Assert.Equal(expected, slots);
    }

    [Theory]
    [InlineData("22:00", 60, "23:00", true)]
    [InlineData("23:00", 60, "23:00", false)]
    [InlineData("23:30", 60, "23:45", false)]
    public void AppointmentMustEndInsideTheAvailability(string start, int duration, string end, bool expected)
        => Assert.Equal(expected, AvailabilitySlots.Fits(TimeOnly.Parse(start), duration, TimeOnly.Parse(end)));

    [Theory]
    [InlineData("Anguloso.Server/Controllers/AppointmentsController.cs")]
    [InlineData("Anguloso.Server/Controllers/DirectoryController.cs")]
    [InlineData("Anguloso.Server/Controllers/ProfessionalConsultationActionsController.cs")]
    public void ControllersDoNotComputeSlotEndsWithWrappingArithmetic(string path)
    {
        var source = Read(path);
        Assert.DoesNotContain("localTime.AddMinutes", source);
        Assert.DoesNotContain("time.AddMinutes", source);
    }

    [Fact]
    public void LowAdherenceSignalUsesThePercentageScale()
    {
        var source = Read("Anguloso.Server/Controllers/ProfessionalConsultationsController.cs");
        Assert.Contains("LowAdherencePercentage = 60", source);
        Assert.Contains("latestCheckin.adherence.Value <= LowAdherencePercentage", source);
    }

    [Fact]
    public void CancelledOrMissedAppointmentsCannotCloseTheConsultation()
    {
        var source = Read("Anguloso.Server/Controllers/ProfessionalConsultationsController.cs");
        var complete = source[source.IndexOf("public async Task<IActionResult> Complete", StringComparison.Ordinal)..];
        var check = complete.IndexOf("appointmentStatus is not (\"confirmed\" or \"completed\")", StringComparison.Ordinal);

        Assert.True(check > complete.IndexOf("FOR UPDATE", StringComparison.Ordinal));
        // La comprobación precede a cualquier escritura y a las automatizaciones.
        Assert.True(check < complete.IndexOf("UPDATE professional_consultations", StringComparison.Ordinal));
        Assert.True(check < complete.IndexOf("TransactionalOutbox.EnqueueAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void FinishingTheVideoDoesNotPromiseToCompleteTheAppointment()
    {
        var component = Read("anguloso.client/src/app/componentes/video-consultation/video-consultation.component.ts");
        var template = Read("anguloso.client/src/app/componentes/video-consultation/video-consultation.component.html");

        Assert.DoesNotContain("marcar la cita como completada", component);
        Assert.DoesNotContain("La cita ha quedado cerrada", template);
    }

    [Fact]
    public void PreviousConsultationReturnsWhatWasAgreed()
    {
        var source = Read("Anguloso.Server/Controllers/ProfessionalConsultationsController.cs");
        Assert.Contains("c.progress->>'goals', c.decision_summary, c.actions_summary, c.next_consultation_plan", source);
    }

    [Theory]
    [InlineData(nameof(AppointmentsController.GetVideoAccess))]
    [InlineData(nameof(AppointmentsController.RecordVideoEvent))]
    [InlineData(nameof(AppointmentsController.FinishVideoConsultation))]
    public void ClinicAdminCanRunTheirOwnVideoConsultations(string action)
    {
        var roles = typeof(AppointmentsController).GetMethod(action)!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles!.Split(',');

        Assert.Contains("clinic_admin", roles);
        Assert.Contains("nutritionist", roles);
    }

    [Fact]
    public void VideoAccessStaysLimitedToTheAssignedProfessional()
    {
        var source = Read("Anguloso.Server/Controllers/AppointmentsController.cs");
        Assert.Contains("userId.Value != appointment.nutritionist_id", source);
        Assert.Contains("a.tenant_id == tenantId.Value && a.nutritionist_id == userId.Value", source);
    }
}
