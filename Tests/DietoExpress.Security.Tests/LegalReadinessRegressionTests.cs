using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class LegalReadinessRegressionTests
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
    public void LegalReadiness_IsSuperadminOnlyAndReportsMissingDocumentsAndConfiguration()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "Anguloso.Server", "Controllers", "LegalDocumentsController.cs"));
        var start = source.IndexOf("public async Task<IActionResult> Readiness", StringComparison.Ordinal);
        var end = source.IndexOf("[HttpGet(\"admin\")]", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var endpoint = source[start..end];

        var endpointAttributes = source[Math.Max(0, start - 250)..start];
        Assert.Contains("[HttpGet(\"readiness\")]", endpointAttributes);
        Assert.Contains("[Authorize(Policy = \"Professional\")]", endpointAttributes);
        Assert.Contains("if (!User.IsInRole(\"superadmin\")) return Forbid();", endpoint);
        Assert.Contains("requiredDocuments", endpoint);
        Assert.Contains("requiredConfiguration", endpoint);
        Assert.Contains("missingConfiguration", endpoint);
        Assert.Contains("ready = documentsStatus.All(d => d.published) && missingConfiguration.Length == 0", endpoint);
    }
}
