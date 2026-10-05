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
