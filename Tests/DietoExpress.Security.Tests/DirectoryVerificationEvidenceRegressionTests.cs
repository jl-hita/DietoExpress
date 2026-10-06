using System;
using System.IO;
using Xunit;

namespace DietoExpress.Security.Tests;

public class DirectoryVerificationEvidenceRegressionTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static string Read(string path) => File.ReadAllText(Path.Combine(RepoRoot, path));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    [Fact]
    public void Evidence_IsPrivateAndRestricted()
    {
        var source = Read("Anguloso.Server/Controllers/DirectoryVerificationEvidenceController.cs");
        Assert.Contains("[Authorize]", source);
        Assert.Contains("[Authorize(Policy = "Professional")]", source);
        Assert.Contains("[Authorize(Roles = "superadmin")]", source);
        Assert.Contains("DIETOEXPRESS_DOCUMENTS_PATH", source);
        Assert.DoesNotContain("wwwroot", source);
    }

    [Fact]
    public void Evidence_UsesHashesAndSafeFileTypes()
    {
        var source = Read("Anguloso.Server/Controllers/DirectoryVerificationEvidenceController.cs");
        Assert.Contains("SHA256", source);
        Assert.Contains("application/pdf", source);
        Assert.Contains("image/jpeg", source);
        Assert.Contains("image/png", source);
        Assert.Contains("Path.GetFileName(file.FileName)", source);
    }

    [Fact]
    public void Evidence_ReviewIsAuditedAndCanBeRevoked()
    {
        var source = Read("Anguloso.Server/Controllers/DirectoryVerificationEvidenceController.cs");
        Assert.Contains("PUBLIC_DIRECTORY_EVIDENCE_REVIEWED", source);
        Assert.Contains("revoked_at", source);
        Assert.Contains("reviewed_by_user_id", source);
        Assert.Contains("review_note", source);
        Assert.Contains("identity", source);
        Assert.Contains("qualification", source);
        Assert.Contains("approved", source);
    }
}