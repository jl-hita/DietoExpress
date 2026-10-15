using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DietAssignmentAllergenTests
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
    public void AssignDiet_ValidatesRestrictionsBeforeDeactivatingExistingAssignment()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "ClientDietsController.cs"));
        var start = source.IndexOf("public async Task<ActionResult<ClientDietListDto>> AssignDiet", StringComparison.Ordinal);
        var end = source.IndexOf("// PUT: api/clients/{clientId}/diets/{id}", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];
        var validate = method.IndexOf("ValidateDietCompatibilityAsync", StringComparison.Ordinal);
        var deactivate = method.IndexOf("foreach (var activeDiet in activeDiets)", StringComparison.Ordinal);
        Assert.True(validate >= 0 && deactivate > validate);
        Assert.Contains("AlertType, \"AllergenDataUnknown\"", method);
        Assert.Contains("return Conflict", method);
    }
}
