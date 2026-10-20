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
    public void GuidedFlowKeepsOnlineVideoEmbeddedAndPersistent()
    {
        var g=Read("anguloso.client/src/app/componentes/guided-consultation/guided-consultation.component.ts");
        var v=Read("anguloso.client/src/app/componentes/video-consultation/video-consultation.component.ts");
        var h=Read("anguloso.client/src/app/componentes/video-consultation/video-consultation.component.html");
        Assert.Contains("VideoConsultationComponent",g);
        Assert.Contains("<app-video-consultation",g);
        Assert.Contains("[embedded]=\"true\"",g);
        Assert.Contains("[appointmentIdInput]=\"data.appointment.id\"",g);
        Assert.Contains("@Input() embedded = false",v);
        Assert.Contains("@Input() appointmentIdInput",v);
        Assert.Contains("@Input() appointmentStartsAtInput",v);
        Assert.Contains("@Output() closeEmbedded",v);
        Assert.Contains("(closeEmbedded)=\"videoPanelVisible = false\"",g);
        Assert.Contains("videoPanelVisible = true",g);
        Assert.Contains("15 * 60 * 1000",v);
        Assert.Contains("compactVideo",v);
        Assert.Contains("[class.embedded]=\"embedded\"",h);
        Assert.Contains("if (this.embedded) return",v);
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
        Assert.Contains("online:joined:patient:",s);
        Assert.Contains("online:joined:professional:",s);
        Assert.Contains("online_consultation.patient_joined",s);
        Assert.Contains("online_consultation.professional_joined",s);
        Assert.Contains("El paciente se ha unido a la videollamada",s);
        Assert.Contains("Tu nutricionista se ha unido",s);
        Assert.Contains("online_consultation.room_available.patient",s);
        Assert.Contains("$\"video:{appointment.id}:connected:{(User.IsInRole(\"patient\") ? \"patient\" : \"professional\")}\"",a);
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
