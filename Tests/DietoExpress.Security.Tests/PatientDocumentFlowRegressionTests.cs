using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class PatientDocumentFlowRegressionTests
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

    private static string ReadServerSource(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot, relativePath));

    [Fact]
    public void ProfessionalDashboardMustExposePendingPatientData()
    {
        var source = ReadServerSource("Anguloso.Server/Controllers/ProfessionalDashboardController.cs");

        Assert.Contains("PendingPatientDataCount", source);
        Assert.Contains("PendingPatientData", source);
        Assert.Contains("cl.birth_date IS NULL", source);
        Assert.Contains("cl.onboarding_consent_at IS NULL", source);
        Assert.Contains("b.weight IS NOT NULL", source);
        Assert.Contains("b.height IS NOT NULL", source);
        Assert.Contains("nutritionist_id=@user AND a.is_active", source);
    }

    [Fact]
    public void ConsultationStartMustProvisionAndBlockOnRequiredDocuments()
    {
        var source = ReadServerSource("Anguloso.Server/Controllers/ProfessionalConsultationsController.cs");

        Assert.Contains("CreateRequiredDocumentsAsync(", source);
        Assert.Contains("GetPendingSignatureDocumentsBeforeConsultationAsync(", source);
        Assert.Contains("return Conflict(new", source);
        Assert.Contains("documents_pending", source);
    }

    [Fact]
    public void PatientAcceptanceMustBeTenantAndClientBound()
    {
        var source = ReadServerSource("Anguloso.Server/Controllers/PatientDocumentsController.cs");

        Assert.Contains("id = {0} AND client_id = {1} AND tenant_id = {2}", source);
        Assert.Contains("SET status='signed'", source);
        Assert.Contains("event_type, ip_address, user_agent, details", source);
    }

    [Fact]
    public void DocumentProvisioningMustBeTenantAndPatientScoped()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/PatientDocumentService.cs");

        Assert.Contains("tenant_id=@tenant AND client_id=@client", source);
        Assert.Contains("patient-documents:{tenantId}:{clientId}", source);
        Assert.Contains("document_template_id=@template AND version=@version", source);
    }

    [Fact]
    public void PatientPortalReadMustNotProvisionConsultationDocuments()
    {
        var source = ReadServerSource("Anguloso.Server/Controllers/PatientDocumentsController.cs");
        var methodStart = source.IndexOf("public async Task<IActionResult> ListForPatient()");
        Assert.True(methodStart >= 0);
        var methodEnd = source.IndexOf("    [HttpPost(\"api/portal/documents/{documentId:long}/accept\")]", methodStart);
        Assert.True(methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.DoesNotContain("CreateRequiredDocumentsAsync", method);
        Assert.Contains("Select(c => c.tenant_id)", method);
        Assert.Contains("claimedTenantId", method);
    }

    [Fact]
    public void PatientPortalDocumentOperationsMustValidateClaimedTenantAgainstPatientRecord()
    {
        var source = ReadServerSource("Anguloso.Server/Controllers/PatientDocumentsController.cs");

        Assert.Contains("var claimedTenantId = AuthHelpers.GetTenantId(User);", source);
        Assert.Contains("claimedTenantId.Value != tenantId.Value", source);
        Assert.Contains("patientAccess", source);
    }

    [Fact]
    public void ConsultationPendingQueryMustOnlyConsiderActiveTemplatesMarkedBeforeConsultation()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/PatientDocumentService.cs");

        Assert.Contains("dt.is_active=true", source);
        Assert.Contains("dt.is_required_before_consultation=true", source);
        Assert.Contains("pd.status='pending'", source);
    }
    [Fact]
    public void DashboardMustExposeDocumentProvisioningFailuresAndRetryingJobs()
    {
        var source = ReadServerSource("Anguloso.Server/Controllers/ProfessionalDashboardController.cs");

        Assert.Contains("DocumentProvisioningRetryCount", source);
        Assert.Contains("DocumentProvisioningFailedCount", source);
        Assert.Contains("DocumentProvisioningIssues", source);
        Assert.Contains("status IN ('pending','processing','failed')", source);
        Assert.Contains("a.nutritionist_id=@user AND a.is_active", source);
        Assert.Contains("documents:provision:", source);
    }

    [Fact]
    public void FailedDocumentProvisioningMustCreateAnIdempotentRecoveryTask()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        Assert.Contains("job.ActionType == \"provision_patient_documents\"", source);
        Assert.Contains("CreateProfessionalTaskAsync", source);
        Assert.Contains("automation:patient-documents.provision.failed", source);
        Assert.Contains("job:{job.Id}:document-provision-failed", source);
        Assert.Contains("Revisar documentación del paciente", source);
    }

    [Fact]
    public void PublicBookingMustUseInjectedLogger()
    {
        var source = ReadServerSource("Anguloso.Server/Controllers/DirectoryController.cs");

        Assert.Contains("ILogger<DirectoryController> _logger", source);
        Assert.Contains("ILogger<DirectoryController> logger", source);
        Assert.Contains("_logger.LogError", source);
        Assert.DoesNotContain("RequestServices.GetRequiredService<ILogger<DirectoryController>>", source);
    }


    [Fact]
    public void CompletedDocumentProvisioningMustBeReconciledAgainstRequiredDocuments()
    {
        var professional = ReadServerSource("Anguloso.Server/Controllers/ProfessionalDashboardController.cs");
        var clinic = ReadServerSource("Anguloso.Server/Controllers/ClinicController.cs");

        Assert.Contains("j.status='completed'", professional);
        Assert.Contains("completed_incomplete", professional);
        Assert.Contains("documentProvisioningIncompleteCount", professional);
        Assert.Contains("dt.is_required_on_client_creation=true", professional);
        Assert.Contains("dt.created_at <= j.created_at", professional);
        Assert.Contains("pd.document_template_id=dt.id", professional);
        Assert.Contains("pd.version=dt.version", professional);

        Assert.Contains("IncompleteCount", clinic);
        Assert.Contains("dt.created_at <= j.created_at", clinic);
        Assert.Contains("j.status='completed' AND EXISTS", clinic);
        Assert.Contains("documentProvisioningIncompleteCount", clinic);
    }


    [Fact]
    public void CompletedDocumentProvisioningMustScheduleIdempotentRepairJobs()
    {
        var automation = ReadServerSource("Anguloso.Server/Logica/AutomationService.cs");
        var worker = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        Assert.Contains("RunDocumentProvisioningReconciliationSweepAsync", automation);
        Assert.Contains("documents:reconcile:", automation);
        Assert.Contains("dt.created_at <= j.created_at", automation);
        Assert.Contains("provision_patient_documents", automation);
        Assert.Contains("ScheduleReconciliationRepairAsync", automation);
        Assert.Contains("WHERE automation_jobs.status='failed'", automation);
        Assert.Contains("attempts=0", automation);
        Assert.Contains("RunDocumentProvisioningReconciliationSweepAsync", worker);
        Assert.Contains("nextDocumentProvisioningSweep = DateTime.UtcNow.AddHours(1)", worker);
    }

    [Fact]
    public void DocumentProvisioningJobMustBePersistentAndRetryable()
    {
        var service = ReadServerSource("Anguloso.Server/Logica/AutomationService.cs");
        var worker = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        Assert.Contains("ProvisionPatientDocumentsAction", service);
        Assert.Contains("provision_patient_documents", worker);
        Assert.Contains("GetRequiredService<PatientDocumentService>()", worker);
        Assert.Contains("maxAttempts", service);
        Assert.Contains("scheduled_at=NOW() + (@delay * INTERVAL '1 second')", worker);
    }

}

    [Fact]
    public void AutomationWorkerMustFenceCompletionAndFailureByClaimedAttempt()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        Assert.Contains("reader.GetInt32(6) + 1", source);
        Assert.Contains("WHERE id=@id AND status='processing' AND attempts=@attempts", source);
        Assert.Contains("CompleteJobAsync(job.Id, job.Attempts", source);
        Assert.Contains("command.Parameters.AddWithValue(" + ""attempts"" + ", attempts)", source);
        Assert.Contains("command.Parameters.AddWithValue(" + ""attempts"" + ", job.Attempts)", source);
        Assert.Contains("if (updated != 1)", source);
    }

    [Fact]
    public void AutomationWorkerMustRecoverStaleProcessingJobs()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        Assert.Contains("status='processing'", source);
        Assert.Contains("locked_at < NOW() - INTERVAL '10 minutes'", source);
        Assert.Contains("SET status='pending', locked_at=NULL", source);
    }

