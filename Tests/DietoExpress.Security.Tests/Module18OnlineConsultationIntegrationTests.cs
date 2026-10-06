using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class Module18OnlineConsultationIntegrationTests
{
    private static string Root
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Anguloso.Server", "Program.cs"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException();
        }
    }

    private static string Read(string p) => File.ReadAllText(Path.Combine(Root, p));

    [Fact]
    public void GuidedFlowMustLaunchOnlineVideoAndReturn()
    {
        var g=Read("anguloso.client/src/app/componentes/guided-consultation/guided-consultation.component.ts");
        var v=Read("anguloso.client/src/app/componentes/video-consultation/video-consultation.component.ts");
        Assert.Contains("data.appointment.modality === 'online'",g);
        Assert.Contains("openOnlineConsultation()",g);
        Assert.Contains("returnUrl: this.router.url",g);
        Assert.Contains("navigateByUrl(returnUrl)",v);
    }

    [Fact]
    public void VideoFinishMustNotCompleteClinicalAppointment()
    {
        var s=Read("Anguloso.Server/Controllers/AppointmentsController.cs");
        var a=s.IndexOf("FinishVideoConsultation(int id)");
        var b=s.IndexOf("public sealed class VideoConsultationEventRequest",a);
        Assert.True(a>=0 && b>a);
        var m=s[a..b];
        Assert.Contains("VIDEO_FINISHED",m);
        Assert.Contains("video.finished",m);
        Assert.DoesNotContain("UpdateStatus(id, new UpdateAppointmentStatusRequestDto",m);
    }

    [Fact]
    public void OnlinePresenceAndRemindersMustUseAutomation()
    {
        var a=Read("Anguloso.Server/Controllers/AppointmentsController.cs");
        var s=Read("Anguloso.Server/Logica/AutomationService.cs");
        Assert.Contains("video.participant.connected",a);
        Assert.Contains("VideoParticipantConnectedPayload",a);
        Assert.Contains("online:room-ready:patient:",s);
        Assert.Contains("online:waiting:patient:",s);
        Assert.Contains("online:waiting:professional:",s);
        Assert.Contains("online_consultation.room_available.patient",s);
    }

    [Fact]
    public void LegalReviewMustDocumentProductionBlockers()
    {
        var s=Read("docs/legal/05-livekit-dpa-y-consulta-online.md");
        Assert.Contains("DPA",s);
        Assert.Contains("European Union (Frankfurt)",s);
        Assert.Contains("subprocesadores",s);
        Assert.Contains("Pendiente antes de producción",s);
        Assert.Contains("no almacenar grabaciones",s);
    }
}
