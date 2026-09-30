using Xunit;
using System.Security.Claims;

namespace DietoExpress.Security.Tests;

public class AuthorizationRegressionTests
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
    public void PatientIdentity_IsExplicitlyExcludedFromProfessionalPolicy()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));

        Assert.Contains("AddPolicy(\"Professional\"", program);
        Assert.Contains("RequireAuthenticatedUser()", program);
        Assert.Contains("!ctx.User.IsInRole(\"patient\")", program);
    }

    [Fact]
    public void PatientPortal_UsesCookieAndDoesNotPersistJwtInLocalStorage()
    {
        var interceptor = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "auth.interceptor.ts"));
        var portal = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.ts"));

        Assert.DoesNotContain("getPatientToken", interceptor);
        Assert.DoesNotContain("PATIENT_TOKEN_KEY", interceptor);
        Assert.DoesNotContain("savePatientToken", portal);
        Assert.DoesNotContain("localStorage.setItem('patient", portal);
    }

    [Fact]
    public void SensitiveProfessionalEndpoints_RequireProfessionalPolicy()
    {
        AssertEndpointRequiresProfessional("Anguloso.Server/Controllers/BillingController.cs", "HttpPost(\"checkout\")");
        AssertEndpointRequiresProfessional("Anguloso.Server/Controllers/AuthController.cs", "HttpPost(\"refreshSession\")");

        var portal = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "PatientPortalController.cs"));
        AssertEndpointMethodHasProfessionalPolicy(portal, "GetClientPortalAccess");
        AssertEndpointMethodHasProfessionalPolicy(portal, "RegenerateToken");
        AssertEndpointMethodHasProfessionalPolicy(portal, "SetPasscode");
    }

    [Fact]
    public void ClientDietController_ContainsTenantSafeDietAuthorizationGuards()
    {
        var source = ReadServerController("ClientDietsController.cs");

        Assert.Contains("UserCanAccessDietAsync", source);
        Assert.Contains("dto.DietId", source);
        Assert.Contains("sharedAllowed", source);
        Assert.Contains("d.tenant_id == tenantId.Value", source);
    }

    [Fact]
    public void FoodExchangeGroups_DoNotExposeOtherTenantsLocalFoods()
    {
        var source = ReadServerController("FoodExchangeGroupController.cs");

        Assert.Contains("f.exchange_group_id == id", source);
        Assert.Contains("f.source != \"local\"", source);
        Assert.Contains("f.created_by_user_id == userId.Value", source);
        Assert.Contains("f.tenant_id == tenantId.Value", source);
        Assert.Contains("User.IsInRole(\"clinic_admin\")", source);
        Assert.Contains("User.IsInRole(\"superadmin\")", source);
    }

    [Fact]
    public void DietAndRecipeControllers_ValidateLocalFoodOwnership()
    {
        foreach (var file in new[] { "DietController.cs", "RecipesController.cs" })
        {
            var source = ReadServerController(file);

            Assert.Contains("CanUseFoodAsync", source);
            Assert.Contains("f.source != \"local\"", source);
            Assert.Contains("f.created_by_user_id == userId", source);
            Assert.Contains("f.tenant_id == tenantId.Value", source);
        }
    }

    private static void AssertEndpointRequiresProfessional(string relativePath, string httpAttribute)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        AssertEndpointAttributePair(source, httpAttribute);
    }

    private static void AssertEndpointMethodHasProfessionalPolicy(string source, string methodName)
    {
        var index = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(index >= 0, $"No se encontró el método {methodName}.");

        var start = Math.Max(0, index - 1200);
        var attributes = source[start..index];

        Assert.Contains("[Authorize(Policy = \"Professional\")]", attributes);
    }

    private static void AssertEndpointAttributePair(string source, string httpAttribute)
    {
        var index = source.IndexOf(httpAttribute, StringComparison.Ordinal);
        Assert.True(index >= 0, $"No se encontró {httpAttribute}.");

        var start = Math.Max(0, index - 500);
        var attributes = source[start..index];

        Assert.Contains("[Authorize(Policy = \"Professional\")]", attributes);
        Assert.DoesNotContain("[Authorize]\n", attributes.Replace("\r\n", "\n"));
    }

    private static string ReadServerController(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", fileName));
}
