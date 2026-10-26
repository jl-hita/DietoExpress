using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class AdminPatientPrivacyRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    [Fact]
    public void GlobalPatientAdministration_IsRestrictedToSuperadmin()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "AdminPatientsController.cs"));
        Assert.Contains("[Route(\"api/admin/patients\")]", source);
        Assert.Contains("[Authorize(Roles = \"superadmin\")]", source);
        Assert.Contains("status is not (\"active\" or \"archived\" or \"all\")", source);
    }

    [Fact]
    public void PatientArchive_RevokesPortalCredentialsAndPushTokens()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "AdminPatientsController.cs"));
        Assert.Contains("client.archived_at = DateTime.UtcNow", source);
        Assert.Contains("client.portal_token_version = checked(client.portal_token_version + 1)", source);
        Assert.Contains("client.access_token = null", source);
        Assert.Contains("client.passcode_hash = null", source);
        Assert.Contains("DELETE FROM patient_push_tokens WHERE client_id", source);
    }

    [Fact]
    public void PatientErasure_RequiresExplicitConfirmationAndDisclosesRetainedRecords()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "AdminPatientsController.cs"));
        Assert.Contains("request?.ConfirmId != id || request.ConfirmText != \"ELIMINAR\"", source);
        Assert.Contains("client.full_name = \"Cuenta eliminada\"", source);
        Assert.Contains("no declara suprimidos los datos clínicos", source);
    }
}
