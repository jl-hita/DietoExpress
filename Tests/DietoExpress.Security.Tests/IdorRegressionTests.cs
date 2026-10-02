using Xunit;

namespace DietoExpress.Security.Tests;

public class IdorRegressionTests
{
    // Los tests son regresiones estáticas: cada caso exige que el endpoint vuelva a comprobar tenant, propietario o asignación antes de usar el identificador recibido.
    // Todas las aserciones leen el fuente real del repositorio para detectar la eliminación accidental de filtros de autorización.
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
    public void ClientReadsAndWrites_AreTenantAndAssignmentScoped()
    {
        var source = ReadController("ClientsController.cs");

        Assert.Contains("c.id == id && c.archived_at == null", source);
        Assert.Contains("c.tenant_id == tenantId.Value", source);
        Assert.Contains("c.user_id == userId.Value", source);
        Assert.Contains("a.nutritionist_id == userId.Value && a.is_active", source);
        Assert.Contains("User.IsInRole(\"clinic_admin\")", source);
    }

    [Fact]
    public void DietReads_AreTenantScopedAndSharedAccessIsExplicit()
    {
        var source = ReadController("DietController.cs");

        Assert.Contains("d.id == id && d.archived_at == null", source);
        Assert.Contains("d.tenant_id == tenantId.Value", source);
        Assert.Contains("d.user_id == userId.Value", source);
        Assert.Contains("d.is_shared", source);
        Assert.Contains("sharedAllowed", source);
    }

    [Fact]
    public void RecipeReadsAndWrites_AreTenantScoped()
    {
        var source = ReadController("RecipesController.cs");

        Assert.Contains("r.id == id", source);
        Assert.Contains("r.tenant_id == tenantId.Value", source);
        Assert.Contains("r.user_id == userId.Value", source);
        Assert.Contains("CanUseFoodAsync", source);
        Assert.Contains("f.tenant_id == tenantId.Value", source);
    }

    [Fact]
    public void RecipeReads_DoNotExposeAnotherProfessionalWithinTheSameTenant()
    {
        var source = ReadController("RecipesController.cs");

        Assert.Contains("r.tenant_id == tenantId.Value && r.user_id == userId.Value", source);
        Assert.DoesNotContain(".Where(r => tenantId.HasValue && r.tenant_id == tenantId.Value)", source);
    }

    [Fact]
    public void LocalFoodMutations_CannotCrossTenantBoundary()
    {
        var source = ReadController("FoodController.cs");

        Assert.Contains("f.tenant_id == tenantId.Value && f.created_by_user_id == userId.Value", source);
        Assert.Contains("User.IsInRole(\"clinic_admin\") && tenantId.HasValue && f.tenant_id == tenantId.Value", source);
        Assert.DoesNotContain("FindAsync(id)", source);
    }

    [Fact]
    public void BiometricResourceReadsAndWrites_ReauthorizeTheParentClient()
    {
        var source = ReadController("BiometricsController.cs");

        Assert.Contains("x.id == id && x.client_id == clientId", source);
        Assert.Contains("x.client.archived_at == null", source);
        Assert.Contains("x.client.tenant_id == AuthHelpers.GetTenantId(User)!.Value", source);
        Assert.Contains("a.client_id == x.client_id && a.nutritionist_id == userId.Value && a.is_active", source);
        Assert.Contains("b.client_id == clientId && b.measurement_date == dateOnly", source);
    }

    [Fact]
    public void ClientDietOperations_RecheckClientAndDietTenantScope()
    {
        var source = ReadController("ClientDietsController.cs");

        Assert.Contains("UserOwnsClientAsync", source);
        Assert.Contains("UserCanAccessDietAsync", source);
        Assert.Contains("d.tenant_id == tenantId.Value", source);
        Assert.Contains("cd.diet.tenant_id == tenantId.Value", source);
        Assert.Contains("SanitizeDietFoodScopeAsync", source);
    }

    [Fact]
    public void PatientPortalPreview_ResolvesAuthorizedClientBeforeResourceAccess()
    {
        var source = ReadController("PatientPortalController.cs");

        Assert.Contains("ResolveAuthorizedClientId", source);
        Assert.Contains("c.tenant_id == AuthHelpers.GetTenantId(User)!.Value", source);
        Assert.Contains("User.IsInRole(\"clinic_admin\")", source);
        Assert.Contains("c.archived_at == null", source);
    }

    [Fact]
    public void AdministrativeResourceEndpoints_AreSuperadminOnly()
    {
        var plans = ReadController("AdminPlansController.cs");
        var users = ReadController("AdminUsersController.cs");

        Assert.Contains("[Authorize(Roles = \"superadmin\")]", plans);
        Assert.Contains("[Authorize(Roles = \"superadmin\")]", users);
        Assert.Contains("[Route(\"api/admin/plans\")]", plans);
        Assert.Contains("[Route(\"api/admin\")]", users);
    }

    private static string ReadController(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", fileName));
}
