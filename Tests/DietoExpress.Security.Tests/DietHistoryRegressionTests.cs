using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>El historial de dietas no se pierde y cada paciente solo ve el suyo.</summary>
public sealed class DietHistoryRegressionTests
{
    private static string Read(string path)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Anguloso.Server", "Program.cs"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d?.FullName ?? throw new InvalidOperationException(), path)).Replace("\r\n", "\n");
    }

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, from);
        var end = source.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, to);
        return source[start..end];
    }

    private const string Professional = "Anguloso.Server/Controllers/ClientDietsController.cs";
    private const string Portal = "Anguloso.Server/Controllers/PatientPortalController.cs";

    [Fact]
    public void AssignmentsAreNeverHardDeleted()
        => Assert.DoesNotContain("client_diets.Remove", Read(Professional));

    [Fact]
    public void ProfessionalHistoryKeepsArchivedDiets()
    {
        var history = Between(Read(Professional), "GetHistory(int clientId)", "[HttpGet(\"active\")]");
        Assert.DoesNotContain("d.archived_at == null", history);
        Assert.Contains("IsArchived", history);
        Assert.Contains("d.user_id == userId.Value", history);
    }

    [Fact]
    public void ArchivedDietsCannotBeAssignedAgain()
    {
        var assign = Between(Read(Professional), "AssignDiet(int clientId", "UpdateAssignment(int clientId");
        Assert.DoesNotContain("includeArchived", assign);
    }

    [Fact]
    public void PatientHistoryIsScopedToTheAuthorizedPatient()
    {
        var history = Between(Read(Portal), "[HttpGet(\"diet/history\")]", "BuildPatientDietAsync(int dietId");

        Assert.Equal(2, history.Split("ResolveAuthorizedClientId()").Length - 1);
        Assert.Equal(2, history.Split("PortalFeatureAllowedAsync(clientId.Value)").Length - 1);
        Assert.Contains("cd.id == assignmentId && cd.client_id == clientId.Value", history);
        // Las notas de la asignación son del profesional.
        Assert.DoesNotContain("cd.notes", history);
    }

    [Fact]
    public void PatientCanOnlyDownloadTheirOwnPastPlans()
    {
        var download = Between(Read(Portal), "DownloadMyDietPdfAsync(int? assignmentId)", "[HttpGet(\"shopping-list\")]");

        Assert.Contains("ResolveAuthorizedClientId()", download);
        Assert.Contains("PortalFeatureAllowedAsync(clientId.Value)", download);
        Assert.Contains("\"PDF_EXPORT\"", download);
        Assert.Contains("cd.client_id == clientId.Value &&", download);
        Assert.Contains("assignmentId == null ? cd.is_active == true : cd.id == assignmentId", download);
    }
}
