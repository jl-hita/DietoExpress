using System;
using System.IO;
using Xunit;

namespace DietoExpress.Security.Tests;

public class PublicDirectoryVerificationRegressionTests
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
    public void PublicDirectory_ExposesOnlyPublishedProfiles()
    {
        var source = Read("Anguloso.Server/Controllers/DirectoryController.cs");

        Assert.Contains("u.directory_publication_status == \"published\"", source);
        Assert.Contains("u.role == \"nutritionist\"", source);
        Assert.Contains("u.directory_enabled == true", source);
    }

    [Fact]
    public void ProfessionalDirectoryChanges_ReturnToPendingReview()
    {
        var source = Read("Anguloso.Server/Controllers/ProfileController.cs");

        Assert.Contains("user.directory_publication_status = \"pending\"", source);
        Assert.Contains("publicProfileChanged", source);
        Assert.Contains("user.full_name, dto.FullName", source);
        Assert.Contains("user.clinic_name, dto.ClinicName", source);
        Assert.Contains("user.clinic_logo, dto.ClinicLogo", source);
    }

    [Fact]
    public void VerificationEndpoint_IsSuperAdminOnlyAndAudited()
    {
        var source = Read("Anguloso.Server/Controllers/AdminDirectoryVerificationController.cs");

        Assert.Contains("[Authorize(Roles = \"superadmin\")]", source);
        Assert.Contains("PUBLIC_DIRECTORY_VERIFICATION_UPDATED", source);
        Assert.Contains("directory_verified_by_user_id", source);
        Assert.Contains("status is \"verified\" or \"published\"", source);
    }

    [Fact]
    public void PublishingRequiresDirectoryToRemainEnabled()
    {
        var source = Read("Anguloso.Server/Controllers/AdminDirectoryVerificationController.cs");

        Assert.Contains("status == \"published\" && user.directory_enabled != true", source);
        Assert.Contains("No se puede publicar una ficha", source);
    }

    [Fact]
    public void VerificationSchemaHasExplicitPublicationStates()
    {
        var schema = Read("Anguloso.Server/Logica/PublicDirectoryVerificationSchema.cs");

        Assert.Contains("directory_publication_status", schema);
        Assert.Contains("draft','pending','verified','published','rejected", schema);
        Assert.Contains("directory_verified_at", schema);
        Assert.Contains("directory_verification_note", schema);
        Assert.Contains("users_directory_publication_status_check", schema);
    }
}