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
    public void ConsultationPendingQueryMustOnlyConsiderActiveTemplatesMarkedBeforeConsultation()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/PatientDocumentService.cs");

        Assert.Contains("dt.is_active=true", source);
        Assert.Contains("dt.is_required_before_consultation=true", source);
        Assert.Contains("pd.status='pending'", source);
    }
}
