using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Reglas del flujo de consulta guiada: el resumen de datos previos solo existe a partir de la
/// primera consulta completada y la navegación nunca queda bloqueada.
/// </summary>
public sealed class GuidedConsultationFlowRegressionTests
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

    private const string Controller = "Anguloso.Server/Controllers/ProfessionalConsultationsController.cs";
    private const string Component = "anguloso.client/src/app/componentes/guided-consultation/guided-consultation.component.ts";

    [Fact]
    public void FollowUpTypeDependsOnCompletedConsultationsNotOnCompletedAppointments()
    {
        var s = Read(Controller);
        Assert.Contains("FROM professional_consultations", s);
        Assert.Contains("status='completed' AND appointment_id<>@appointment", s);
        // Antes bastaba con que una cita se hubiese marcado "Completar" en la agenda.
        Assert.DoesNotContain("a.id != appointmentId && a.status == \"completed\"", s);
    }

    [Fact]
    public void ServerDecidesConsultationTypeInsteadOfTrustingTheClient()
    {
        var s = Read(Controller);
        Assert.Contains("previousConsultationExists ? \"follow_up\" : \"first\"", s);
        Assert.DoesNotContain("consultationType = string.IsNullOrWhiteSpace(request?.ConsultationType)", s);
        // Una consulta ya iniciada conserva su tipo.
        Assert.Contains("storedType is \"first\" or \"follow_up\"", s);
    }

    [Fact]
    public void FirstConsultationNeverReceivesNorAcceptsThePatientSummary()
    {
        var s = Read(Controller);
        Assert.Contains("latestCheckin = suggestedType == \"first\" ? null : latestCheckin", s);
        Assert.Contains("previousAppointment = suggestedType == \"first\" ? null : previousAppointment", s);
        Assert.Contains("followupSignals = suggestedType == \"first\" ? new List<object>() : followupSignals", s);
        Assert.Contains("step == \"summary\" && consultationType == \"first\"", s);
        Assert.Contains("InitialStepFor(consultationType)", s);

        var complete = s[s.IndexOf("public async Task<IActionResult> Complete", StringComparison.Ordinal)..];
        Assert.Contains("consultation_type='first' AND x.step='summary'", complete);
    }

    [Fact]
    public void FrontendOnlyOffersSummaryStepInFollowUps()
    {
        var c = Read(Component);
        Assert.Contains("currentStep === 'summary' && !isFirstConsultation", c);
        Assert.DoesNotContain("Preparación de la primera consulta", c);
        var first = c[c.IndexOf("const firstConsultationOrder", StringComparison.Ordinal)..];
        first = first[..first.IndexOf("const followUpOrder", StringComparison.Ordinal)];
        Assert.DoesNotContain("'summary'", first);
        Assert.Contains("this.isFirstConsultation && storedStep === 'summary' ? 'anamnesis'", c);
        Assert.Contains("followupHistory.length && !isFirstConsultation", c);
    }

    [Fact]
    public void ClosedConsultationNavigationDoesNotWaitForSavesThatNeverHappen()
    {
        var c = Read(Component);
        var save = c[c.IndexOf("saveClinicalProgress(onSaved", StringComparison.Ordinal)..];
        save = save[..save.IndexOf("this.clinicalSaveState === 'saving'", StringComparison.Ordinal)];
        Assert.Contains("this.consultation.status === 'completed'", save);
        Assert.Contains("callbacks.forEach(callback => callback())", save);

        var persist = c[c.IndexOf("private persistProgress", StringComparison.Ordinal)..];
        persist = persist[..persist.IndexOf("this.savingProgress = true", StringComparison.Ordinal)];
        Assert.Contains("this.consultation.status === 'completed'", persist);
    }

    [Fact]
    public void FrontendLeavesTheScreenWhenTheConsultationCannotStartAndRefreshesBaselineData()
    {
        var c = Read(Component);
        var start = c[c.IndexOf("No se ha podido iniciar la consulta.", StringComparison.Ordinal)..];
        Assert.Contains("this.backToAgenda()", start[..200]);
        Assert.Contains("this.refreshBiometrics()", c);
        Assert.Contains("private refreshBiometrics()", c);
    }

    [Fact]
    public void CompletingDoesNotMarkEveryStepAsDone()
    {
        var complete = Read(Controller);
        complete = complete[complete.IndexOf("public async Task<IActionResult> Complete", StringComparison.Ordinal)..];
        // Antes se escribía la lista fija de todos los pasos, aunque la consulta rápida omitiera varios.
        Assert.DoesNotContain("'[\"summary\",\"evolution\",\"checkin\"", complete);
        Assert.Contains("jsonb_array_elements_text(professional_consultations.completed_steps)", complete);
    }

    [Theory]
    [InlineData("follow_up", new[] { "summary", "goals", "training", "diet", "tasks", "close" }, new[] { "evolution", "checkin", "education", "next_appointment" })]
    [InlineData("first", new[] { "anamnesis", "goals", "close" }, new[] { "evolution", "checkin", "training", "diet", "education", "tasks", "next_appointment" })]
    [InlineData("follow_up", new[] { "summary", "evolution", "checkin", "goals", "training", "diet", "education", "tasks", "next_appointment", "close" }, new string[0])]
    public void SkippedStepsAreTheApplicableStepsNotDone(string type, string[] completed, string[] expectedSkipped)
        => Assert.Equal(expectedSkipped,
            Anguloso.Server.Controllers.ProfessionalConsultationsController.ComputeSkippedSteps(type, completed));

    [Fact]
    public void LeavingTheConsultationAlwaysGoesThroughTheSavingExitPoint()
    {
        var s = Read(Component);
        foreach (var method in new[] { "openPatientMessages", "openPatientDiet", "openAdvancedTraining", "openPatient()", "leaveToAgenda" })
        {
            var start = s.IndexOf($"  {method}", StringComparison.Ordinal);
            Assert.True(start >= 0, method);
            Assert.Contains("leaveAfterSaving", s.Substring(start, 400));
        }
        Assert.DoesNotContain("(click)=\"backToAgenda()\"", s);
    }

    [Fact]
    public void ClosedConsultationBlocksActionsThatModifyClinicalData()
    {
        var s = Read(Component);
        foreach (var method in new[] { "reviewCheckin(): void {", "createQuickDiet(): void {", "createConsultationRoutine(): void {" })
            Assert.Contains("blockedInReview()", s.Substring(s.IndexOf(method, StringComparison.Ordinal), 250));
        Assert.Contains("previousStep = revertTo ?? this.currentStep", s);
    }
}
