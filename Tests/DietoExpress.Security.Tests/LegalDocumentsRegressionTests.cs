using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class LegalDocumentsRegressionTests
{
    private static string ReadSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    [Fact]
    public void PatientDocumentsMustBeRenderedPerPatientInsteadOfCopyingRawTemplates()
    {
        var service = ReadSource("Anguloso.Server/Logica/PatientDocumentService.cs");
        Assert.Contains("PatientDocumentRenderer.Render", service);
        Assert.Contains("LoadTemplateValuesAsync", service);

        var renderer = ReadSource("Anguloso.Server/Logica/PatientDocumentRenderer.cs");
        Assert.Contains("**Nota interna", renderer);
        Assert.Contains("BlankLine", renderer);
    }

    [Fact]
    public void GeneratedTermsMustBePublishedWithKeyUsedBySignupAndBilling()
    {
        var generator = ReadSource("Anguloso.Server/Controllers/LegalDocumentGeneratorController.cs");
        Assert.Contains("\"saas_terms\"", generator);
        Assert.Contains("PublishedKey(key)", generator);
    }

    [Fact]
    public void InternalPlatformDocumentsMustNotBePublishedToPublicCatalog()
    {
        var generator = ReadSource("Anguloso.Server/Controllers/LegalDocumentGeneratorController.cs");
        var start = generator.IndexOf("PublishablePlatformTemplates =", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = generator.IndexOf("];", start, StringComparison.Ordinal);
        var list = generator[start..end];
        Assert.DoesNotContain("09-rat-minimo", list);
        Assert.DoesNotContain("10-matriz-conservacion", list);
        Assert.DoesNotContain("11-analisis-riesgos-eipd", list);
    }

    [Fact]
    public void PatientDocumentationMustBeBlockedUntilLegalConfigurationIsComplete()
    {
        var service = ReadSource("Anguloso.Server/Logica/PatientDocumentService.cs");
        Assert.Contains("GetMissingLegalSettingsAsync(tenantId, userId, cancellationToken)).Count > 0", service);

        var consultations = ReadSource("Anguloso.Server/Controllers/ProfessionalConsultationsController.cs");
        Assert.Contains("missingLegalSettings", consultations);

        var clients = ReadSource("Anguloso.Server/Controllers/ClientsController.cs");
        Assert.Contains("documentationBlocked", clients);
    }

    [Fact]
    public void RetiredDuplicateTemplatesMustNotBeSeededOrGenerated()
    {
        var seeder = ReadSource("Anguloso.Server/Logica/PatientDocumentTemplateSeeder.cs");
        Assert.DoesNotContain("new(\"PAT-006\"", seeder);
        Assert.DoesNotContain("new(\"PAT-014\"", seeder);

        var generator = ReadSource("Anguloso.Server/Controllers/LegalDocumentGeneratorController.cs");
        Assert.DoesNotContain("06-informacion-privacidad-paciente", generator);
        Assert.DoesNotContain("07-consentimiento-informado-paciente", generator);
    }
}
