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
    public void Recipes_RejectOversizedIngredientCollectionsAndInstructions()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "RecipesController.cs"));

        Assert.Contains("dto.Ingredients.Count > 100", controller);
        Assert.Contains("dto.Instructions?.Length > 10000", controller);
    }

    [Fact]
    public void PatientMagicLink_IsRemovedFromBrowserUrlAfterAuthentication()
    {
        var portal = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.ts"));

        Assert.Contains("queryParams: {}, replaceUrl: true", portal);
        Assert.Contains("authenticateWithToken(token)", portal);
    }

    [Fact]
    public void AngularNewTabLinks_UseNoopenerProtection()
    {
        var clientDetail = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "client-detail", "client-detail.component.html"));

        Assert.Contains("target=\"_blank\" rel=\"noopener noreferrer\"", clientDetail);
        Assert.DoesNotContain("target=\"_blank\">", clientDetail);
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
    public void StripeWebhook_BoundsRequestPayloadSize()
    {
        var source = ReadServerController("BillingController.cs");
        var webhookPos = source.IndexOf("HttpPost(\"stripe/webhook\")", StringComparison.Ordinal);
        Assert.True(webhookPos >= 0);
        var preceding = source.Substring(Math.Max(0, webhookPos - 220), Math.Min(220, webhookPos));
        Assert.Contains("[RequestSizeLimit(256 * 1024)]", preceding);
    }

    [Fact]
    public void BillingMutationEndpoints_AreRateLimited()
    {
        var source = ReadServerController("BillingController.cs");

        AssertEndpointHasRateLimit(source, "HttpPost(\"checkout\")", "expensive");
        AssertEndpointHasRateLimit(source, "HttpPost(\"subscription/change\")", "expensive");
        AssertEndpointHasRateLimit(source, "HttpPost(\"subscription/cancel-renewal\")", "expensive");
        AssertEndpointHasRateLimit(source, "HttpPost(\"subscription/reactivate-renewal\")", "expensive");
    }

    [Fact]
    public void AuthenticationAndPortalEntryPoints_AreRateLimited()
    {
        var auth = ReadServerController("AuthController.cs");
        Assert.Contains("[EnableRateLimiting(\"auth\")]", auth);

        var portal = ReadServerController("PatientPortalController.cs");
        var portalAuthPos = portal.IndexOf("HttpPost(\"auth\")", StringComparison.Ordinal);
        Assert.True(portalAuthPos >= 0);
        Assert.True(portal.LastIndexOf("[EnableRateLimiting(\"auth\")]", portalAuthPos, StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void SetupInitialization_IsOneTimeAndRateLimited()
    {
        var setup = ReadServerController("SetupController.cs");

        Assert.Contains("[EnableRateLimiting(\"auth\")]", setup);
        Assert.Contains("pg_advisory_xact_lock", setup);
        Assert.Contains("alreadyConfigured", setup);
        Assert.Contains("StatusCode(403", setup);
    }

    [Fact]
    public void AdminDiagnostics_BoundSearchAndLogReadSize()
    {
        var source = ReadServerController("AdminUsersController.cs");

        Assert.Contains("searchTerm.Length > 100", source);
        Assert.Contains("maxLogBytes = 2 * 1024 * 1024", source);
        Assert.Contains("StatusCodes.Status413PayloadTooLarge", source);
    }

    [Fact]
    public void NutritionistDeactivation_ValidatesPatientsAfterTenantLock()
    {
        var source = ReadServerController("ClinicController.cs");

        var methodPos = source.IndexOf("DisableNutritionist", StringComparison.Ordinal);
        Assert.True(methodPos >= 0);

        var lockPos = source.IndexOf("pg_advisory_xact_lock", methodPos, StringComparison.Ordinal);
        var clientQueryPos = source.IndexOf("var clients=await _context.clients", methodPos, StringComparison.Ordinal);

        Assert.True(lockPos > methodPos);
        Assert.True(clientQueryPos > lockPos);
    }

    [Fact]
    public void ClientDietAssignmentMutations_SerializeAgainstTenantLock()
    {
        var source = ReadServerController("ClientDietsController.cs");

        foreach (var method in new[] { "AssignDiet", "UpdateAssignment", "DeactivateAssignment", "DeleteAssignment" })
        {
            var methodPos = source.IndexOf(method, StringComparison.Ordinal);
            Assert.True(methodPos >= 0);

            var lockPos = source.IndexOf("pg_advisory_xact_lock", methodPos, StringComparison.Ordinal);
            var savePos = source.IndexOf("SaveChangesAsync", methodPos, StringComparison.Ordinal);

            Assert.True(lockPos > methodPos);
            Assert.True(savePos > lockPos);
        }
    }

    [Fact]
    public void AdminNutritionistDeactivation_SerializesPatientSnapshotAgainstTenantLock()
    {
        var source = ReadServerController("AdminUsersController.cs");

        var methodPos = source.IndexOf("DeleteUser", StringComparison.Ordinal);
        Assert.True(methodPos >= 0);

        var lockPos = source.IndexOf("pg_advisory_xact_lock", methodPos, StringComparison.Ordinal);
        var clientQueryPos = source.IndexOf("var clientIds=await _context.clients", methodPos, StringComparison.Ordinal);

        Assert.True(lockPos > methodPos);
        Assert.True(clientQueryPos > lockPos);
    }

    [Fact]
    public void DietCreation_SerializesAgainstTenantLicenseLimit()
    {
        var source = ReadServerController("DietController.cs");

        Assert.Contains("BeginTransactionAsync()", source);
        Assert.Contains("pg_advisory_xact_lock", source);
        Assert.Contains("CanCreateDietAsync(tenantId, userId.Value)", source);
        Assert.Contains("transaction.CommitAsync()", source);
        Assert.Contains("transaction.RollbackAsync()", source);
    }

    [Fact]
    public void ClientCreation_SerializesAgainstTenantLicenseLimit()
    {
        var source = ReadServerController("ClientsController.cs");

        Assert.Contains("BeginTransactionAsync()", source);
        Assert.Contains("pg_advisory_xact_lock", source);
        Assert.Contains("CanCreateClientAsync(tenantId, userId.Value)", source);
        Assert.Contains("transaction.CommitAsync()", source);
        Assert.Contains("transaction.RollbackAsync()", source);
    }

    [Fact]
    public void AccountCreation_RevalidatesUniquenessInsideRegistrationLocks()
    {
        var auth = ReadServerController("AuthController.cs");
        var admin = ReadServerController("AdminUsersController.cs");
        var clinic = ReadServerController("ClinicController.cs");

        Assert.Contains("pg_advisory_xact_lock(748392616)", auth);
        Assert.Contains("IsolationLevel.Serializable", auth);
        Assert.Contains("users? user = await _context.users", auth);
        Assert.Contains("googleTransaction", auth);

        Assert.Contains("IsolationLevel.Serializable", admin);
        Assert.Contains("var username = request.Username.Trim().ToLowerInvariant();", admin);

        var clinicMethod = clinic.IndexOf("CreateNutritionist", StringComparison.Ordinal);
        Assert.True(clinicMethod >= 0);
        var clinicLock = clinic.IndexOf("pg_advisory_xact_lock", clinicMethod, StringComparison.Ordinal);
        var clinicEmailRecheck = clinic.IndexOf("El email ya está registrado.", clinicLock, StringComparison.Ordinal);
        Assert.True(clinicLock > clinicMethod);
        Assert.True(clinicEmailRecheck > clinicLock);
    }

    [Fact]
    public void LicenseMutation_SerializesSubscriptionCreationPerTenant()
    {
        var source = ReadServerController("AdminUsersController.cs");
        var methodPos = source.IndexOf("UpdateLicense", StringComparison.Ordinal);
        Assert.True(methodPos >= 0);

        var serializablePos = source.IndexOf("IsolationLevel.Serializable", methodPos, StringComparison.Ordinal);
        var subscriptionQueryPos = source.IndexOf("FirstOrDefaultAsync(s => s.tenant_id == user.tenant_id)", methodPos, StringComparison.Ordinal);

        Assert.True(serializablePos > methodPos);
        Assert.True(subscriptionQueryPos > serializablePos);
    }

    [Fact]
    public void AuthenticationEndpoints_BoundCredentialInputSizes()
    {
        var auth = ReadServerController("AuthController.cs");
        var setup = ReadServerController("SetupController.cs");
        var admin = ReadServerController("AdminUsersController.cs");
        var portal = ReadServerController("PatientPortalController.cs");

        Assert.Contains("login.Password.Length > 256", auth);
        Assert.Contains("password.Length > 256", auth);
        Assert.Contains("req.NewPassword.Length > 256", auth);
        Assert.Contains("dto.IdToken.Length > 20000", auth);
        Assert.Contains("request.Password.Length > 256", setup);
        Assert.Contains("request.NewPassword.Length > 256", admin);
        Assert.Contains("request.Passcode.Length <= 128", portal);
    }

    [Fact]
    public void AuthenticationEmails_EncodeUserControlledHtml()
    {
        var auth = ReadServerController("AuthController.cs");

        Assert.Contains("HtmlEncode(user.username)", auth);
        Assert.Contains("HtmlEncode(usuario.Username)", auth);
        Assert.Contains("HtmlEncode(urlConfirm)", auth);
    }

    [Fact]
    public void AccountCreation_BoundsDatabaseBackedFieldLengths()
    {
        var auth = ReadServerController("AuthController.cs");
        var admin = ReadServerController("AdminUsersController.cs");
        var clinic = ReadServerController("ClinicController.cs");

        Assert.Contains("username.Length > 50", auth);
        Assert.Contains("email.Length > 150", auth);
        Assert.Contains("nombreCompleto.Length > 100", auth);

        Assert.Contains("request.Username.Trim().Length > 50", admin);
        Assert.Contains("request.Email.Trim().Length > 150", admin);
        Assert.Contains("request.FullName.Trim().Length > 100", admin);

        Assert.Contains("email.Length > 150 || fullName.Length > 100", clinic);
    }

    [Fact]
    public void SearchEndpoints_BoundPaginationAndSearchInput()
    {
        var clients = ReadServerController("ClientsController.cs");
        var diets = ReadServerController("DietController.cs");

        Assert.Contains("pageSize = Math.Clamp(pageSize, 5, 100)", clients);
        Assert.Contains("searchTerm?.Length > 100", clients);
        Assert.Contains("pageSize = Math.Clamp(pageSize, 5, 100)", diets);
        Assert.Contains("searchTerm.Length > 100", diets);
    }

    [Fact]
    public void SuperAdminLicenseUpdate_ValidatesPlanStatusAndUsesTransaction()
    {
        var source = ReadServerController("AdminUsersController.cs");

        Assert.Contains("p.code == subscriptionPlan && p.active", source);
        Assert.Contains("subscriptionStatus != \"active\"", source);
        Assert.Contains("subscriptionStatus != \"past_due\"", source);
        Assert.Contains("subscriptionStatus != \"suspended\"", source);
        Assert.Contains("BeginTransactionAsync()", source);
        Assert.Contains("transaction.CommitAsync()", source);
        Assert.Contains("transaction.RollbackAsync()", source);
    }

    [Fact]
    public void SuperAdminPasswordReset_IsAuditedAndInvalidatesSessions()
    {
        var source = ReadServerController("AdminUsersController.cs");

        Assert.Contains("user.token_version++", source);
        Assert.Contains("RESET_USER_PASSWORD", source);
        Assert.DoesNotContain("user.role == \"superadmin\";\n\n        user.password_hash", source);
    }

    [Fact]
    public void Usernames_AreCanonicalizedCaseInsensitively()
    {
        var auth = ReadServerController("AuthController.cs");
        var setup = ReadServerController("SetupController.cs");
        var admin = ReadServerController("AdminUsersController.cs");
        var clinic = ReadServerController("ClinicController.cs");
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));

        Assert.Contains("var normalizedUsername = identifier.ToLowerInvariant();", auth);
        Assert.Contains("var username = usuario.Username?.Trim().ToLowerInvariant()", auth);
        Assert.Contains("request.Username.Trim().ToLowerInvariant()", setup);
        Assert.Contains("var username = request.Username.Trim().ToLowerInvariant();", admin);
        Assert.Contains("ToLowerInvariant()", clinic);
        Assert.Contains("uq_users_username_ci", bootstrap);
        Assert.Contains("LOWER(username)", bootstrap);
    }

    [Fact]
    public void PublicRegistration_SerializesDuplicateChecks()
    {
        var source = ReadServerController("AuthController.cs");

        Assert.Contains("IsolationLevel.Serializable", source);
        Assert.Contains("u.username.ToLower() == username.ToLower()", source);
        Assert.Contains("u.email.ToLower() == email.ToLower()", source);
        Assert.Contains("BeginTransactionAsync", source);
    }

    [Fact]
    public void AdminLicenseUpdate_SerializesSubscriptionCreation()
    {
        var source = ReadServerController("AdminUsersController.cs");

        Assert.Contains("IsolationLevel.Serializable", source);
        Assert.Contains("var subscription = await _context.subscriptions.FirstOrDefaultAsync(s => s.tenant_id == user.tenant_id)", source);
        Assert.Contains("_context.subscriptions.Add(subscription)", source);
    }

    [Fact]
    public void StripeWebhook_IsIdempotentAndAtomic()
    {
        var source = ReadServerController("BillingController.cs");

        Assert.Contains("BeginTransactionAsync()", source);
        Assert.Contains("await _context.SaveChangesAsync();", source);
        Assert.Contains("Npgsql.PostgresException", source);
        Assert.Contains("pg.SqlState == \"23505\"", source);
        Assert.Contains("transaction.RollbackAsync()", source);
        Assert.Contains("transaction.CommitAsync()", source);
        Assert.Contains("Returning a non-2xx response makes Stripe retry", source);
    }

    [Fact]
    public void ClinicClientAssignment_IsAtomicAndHonorsNutritionistCapacity()
    {
        var clinic = ReadServerController("ClinicController.cs");
        var license = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "LicenseService.cs"));

        Assert.Contains("pg_advisory_xact_lock", clinic);
        Assert.Contains("BeginTransactionAsync()", clinic);
        Assert.Contains("transaction.CommitAsync()", clinic);
        Assert.Contains("transaction.RollbackAsync()", clinic);
        Assert.Contains("CanAssignClientAsync", clinic);
        Assert.Contains("CLIENT_ASSIGNMENT", clinic);
        Assert.Contains("MaxClientsPerNutritionist", license);
        Assert.Contains("c.id != clientId", license);
    }

    [Fact]
    public void CreatingCustomFood_RequiresTenantForNonSuperAdmin()
    {
        var source = ReadServerController("FoodController.cs");

        Assert.Contains("var tenantId = AuthHelpers.GetTenantId(User);", source);
        Assert.Contains("!User.IsInRole(\"superadmin\") && !tenantId.HasValue", source);
        Assert.Contains("tenant_id = tenantId", source);
        Assert.Contains("created_by_user_id = userId", source);
    }

    [Fact]
    public void OpenFoodFactsSync_CannotOverwriteTenantLocalFoods()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "OpenFoodFactsService.cs"));

        Assert.Contains("f.external_id == product.Code", source);
        Assert.Contains("existing.source == \"local\"", source);
        Assert.Contains("existing.tenant_id.HasValue", source);
        Assert.Contains("se omite la sincronización del alimento local", source);
        Assert.Contains("product.Id = existing.id", source);
    }

    [Fact]
    public void OpenFoodFactsRefresh_RevalidatesGlobalFoodScopeBeforeUpdate()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "OpenFoodFactsService.cs"));

        Assert.Contains("f.id == food.id", source);
        Assert.Contains("f.source != \"local\"", source);
        Assert.Contains("f.tenant_id == null", source);
    }

    [Fact]
    public void SuperAdminPlanEndpoints_BoundPlanAndFeaturePayloads()
    {
        var source = ReadServerController("AdminPlansController.cs");

        Assert.Contains("code.Length > 50", source);
        Assert.Contains("r.Description?.Length > 2000", source);
        Assert.Contains("IsValidPlanLimits", source);
        Assert.Contains("features.Count > 100", source);
        Assert.Contains("FeatureCode.Trim().Length > 100", source);
        Assert.Contains("Distinct(StringComparer.Ordinal)", source);
    }

    [Fact]
    public void StripeCheckoutWebhook_RejectsStaleEventsAndInvalidPlanMetadata()
    {
        var source = ReadServerController("BillingController.cs");

        Assert.Contains("subscription.last_stripe_event_created_at.HasValue", source);
        Assert.Contains("stripeEventCreatedAt.Value <= subscription.last_stripe_event_created_at.Value", source);
        Assert.Contains("FirstOrDefaultAsync(p => p.id == planId.Value && p.active)", source);
        Assert.Contains("plan.code is \"free\" or \"demo_nutri\" or \"trial_nutri\"", source);
        Assert.Contains("interval is not (\"monthly\" or \"yearly\")", source);
    }

    [Fact]
    public void BiometricImport_HasDefensiveBatchAndValueLimits()
    {
        var source = ReadServerController("BiometricsController.cs");

        Assert.Contains("const int maxImportRows = 500", source);
        Assert.Contains("dto.Rows.Count > maxImportRows", source);
        Assert.Contains("double.IsNaN", source);
        Assert.Contains("double.IsInfinity", source);
        Assert.Contains("row.MeasurementDate == default", source);
    }

    [Fact]
    public void AutomatedDietGeneration_HasDefensivePayloadLimits()
    {
        var source = ReadServerController("DietController.cs");

        Assert.Contains("request.NumberOfDays < 1 || request.NumberOfDays > 14", source);
        Assert.Contains("request.MealsPerDay < 3 || request.MealsPerDay > 5", source);
        Assert.Contains("request.TargetKcal < 500 || request.TargetKcal > 10000", source);
        Assert.Contains("request.ExcludedFoodKeywords.Count > 100", source);
        Assert.Contains("double.IsNaN", source);
        Assert.Contains("double.IsInfinity", source);
    }

    [Fact]
    public void FoodMutations_RequireProfessionalPolicy()
    {
        var source = ReadServerController("FoodController.cs");

        foreach (var method in new[] { "UpdateCustomFood", "DeleteCustomFood", "GetFavorites", "AddFavorite", "RemoveFavorite" })
            AssertEndpointMethodHasProfessionalPolicy(source, method);
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

    [Fact]
    public void EmailConfirmation_DoesNotIssueJwtSession()
    {
        var source = ReadServerController("AuthController.cs");

        var start = source.IndexOf("[HttpGet(\"confirmarEmail\")]", StringComparison.Ordinal);
        var end = source.IndexOf("[HttpPost(\"enviarReset\")]", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "No se encontró el endpoint de confirmación de email.");
        var endpoint = source[start..end];

        Assert.Contains("email_confirmed = true", endpoint);
        Assert.DoesNotContain("CrearJwtParaUsuario(user)", endpoint);
        Assert.DoesNotContain("token = tokenString", endpoint);
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

    private static void AssertEndpointHasRateLimit(string source, string endpointMarker, string policy)
    {
        var endpointPos = source.IndexOf(endpointMarker, StringComparison.Ordinal);
        Assert.True(endpointPos >= 0, $"No se encontró el endpoint {endpointMarker}.");

        // Algunos endpoints declaran [EnableRateLimiting] después del atributo HTTP.
        // Comprobamos una ventana alrededor del marcador para no depender del orden.
        var windowStart = Math.Max(0, endpointPos - 300);
        var windowLength = Math.Min(source.Length - windowStart, 900);
        var window = source.Substring(windowStart, windowLength);
        Assert.Contains($"[EnableRateLimiting(\"{policy}\")]", window);
    }

    private static string ReadServerController(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", fileName));
}