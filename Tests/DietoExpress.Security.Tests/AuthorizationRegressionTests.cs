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
    public void Biometrics_Create_RejectsArchivedClients()
    {
        var source = ReadServerController("BiometricsController.cs");
        var start = source.IndexOf("public async Task<ActionResult> Create", StringComparison.Ordinal);
        var end = source.IndexOf("// PUT: api/clients/{clientId}/biometrics/{id}", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        Assert.Contains("c.archived_at == null", source[start..end]);
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
    public void PatientSessions_AreRevocableByPortalTokenVersion()
    {
        var portal = ReadServerController("PatientPortalController.cs");
        var program = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));

        Assert.Contains("portal_token_version++", portal);
        Assert.Contains("new Claim(\"portalTokenVersion\",", portal);
        Assert.Contains("c.portal_token_version == portalTokenVersion", program);
        Assert.Contains("client.portal_token_version++", portal);
        Assert.Contains("[Authorize(Roles = \"patient\")]", portal);
        Assert.Contains("portal_token_version", ReadServerLogic("DatabaseBootstrap.cs"));
    }

    [Fact]
    public void ProfessionalJwt_IsStoredOnlyInHttpOnlyCookie()
    {
        var auth = ReadServerController("AuthController.cs");
        var program = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "servicios", "auth.service.ts"));
        var interceptor = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "auth.interceptor.ts"));

        Assert.Contains("dietoexpress_professional_session", auth);
        Assert.Contains("HttpOnly = true", auth);
        Assert.Contains("SameSite = SameSiteMode.Strict", auth);
        Assert.Contains("MaxAge = TimeSpan.FromHours(3)", auth);
        Assert.Contains("dietoexpress_professional_session", program);
        Assert.DoesNotContain("localStorage", service);
        Assert.DoesNotContain("localStorage", interceptor);
        Assert.DoesNotContain("Authorization", interceptor);
    }

    [Fact]
    public void ProfessionalFrontend_UsesSessionObjectAndRefreshesCookieSession()
    {
        var authService = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "servicios", "auth.service.ts"));
        var login = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "login", "login.component.ts"));
        var billing = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "billing", "billing.component.ts"));

        Assert.Contains("refreshSession(): Observable<AuthUser>", authService);
        Assert.Contains("this.http.post<AuthUser>('/api/auth/refreshSession', {})", authService);
        Assert.DoesNotContain("localStorage", authService);
        Assert.Contains("this.authService.login(res);", login);
        Assert.DoesNotContain("this.authService.login(res.token)", login);
        Assert.Contains("this.authService.login(session);", billing);
        Assert.DoesNotContain("session.token", billing);
    }

    [Fact]
    public void ProfessionalLogout_RevokesTokenVersionAndClearsCookie()
    {
        var auth = ReadServerController("AuthController.cs");
        var logoutPos = auth.IndexOf("HttpPost(\"logout\")", StringComparison.Ordinal);
        Assert.True(logoutPos >= 0);
        var logout = auth[logoutPos..];

        Assert.Contains("user.token_version++", logout);
        Assert.Contains("Response.Cookies.Delete(\"dietoexpress_professional_session\"", logout);
        AssertEndpointMethodHasProfessionalPolicy(auth, "Logout");
    }

    [Fact]
    public void JwtCookieSelection_AllowsProfessionalAndPatientSessionsToCoexist()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));
        Assert.Contains("context.Request.Path.StartsWithSegments(\"/api/portal\")", program);
        Assert.Contains("dietoexpress_patient_session", program);
        Assert.Contains("dietoexpress_professional_session", program);
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
        Assert.Contains("var alreadyConfigured = await _context.users.AnyAsync();", setup);
        Assert.Contains("request.Username.Trim().Length > 50", setup);
        Assert.Contains("StatusCode(403", setup);
    }

    [Fact]
    public void Authentication_NormalizesEmailIdentityAndSerializesPasswordResetConsumption()
    {
        var auth = ReadServerController("AuthController.cs");
        var setup = ReadServerController("SetupController.cs");

        Assert.Contains("var email = usuario.Email?.Trim().ToLowerInvariant() ?? string.Empty;", auth);
        Assert.Contains("var normalizedEmail = req.Email.Trim().ToLowerInvariant();", auth);
        Assert.Contains("var email = payload.Email.Trim().ToLowerInvariant();", auth);
        Assert.Contains("u.email.ToLower() == email", auth);
        Assert.Contains("748392617", auth);
        Assert.Contains("IsolationLevel.Serializable", auth);

        Assert.Contains("var isConfigured = await _context.users.AnyAsync();", setup);
        Assert.Contains("var email = request.Email.Trim().ToLowerInvariant();", setup);
    }

    [Fact]
    public void StripeSubscriptionMutations_SerializePerTenantAndCheckoutUsesIdempotency()
    {
        var source = ReadServerLogica("StripeBillingService.cs");

        Assert.Contains("pg_advisory_xact_lock", source);
        Assert.Contains("CancelRenewalAsync", source);
        Assert.Contains("ReactivateRenewalAsync", source);
        Assert.Contains("CreateCheckoutIdempotencyKey", source);
        Assert.Contains("Idempotency-Key", source);
    }

    [Fact]
    public void StripeCheckout_UsesPersistedAttemptForIdempotencyLifecycle()
    {
        var source = ReadServerLogica("StripeBillingService.cs");
        var schema = ReadServerLogica("BillingSchemaBootstrap.cs");

        Assert.Contains("billing_checkout_attempts", source);
        Assert.Contains("status == \"pending\"", source);
        Assert.Contains("expires_at > DateTime.UtcNow", source);
        Assert.Contains("CreateCheckoutIdempotencyKey(tenantId, plan.id, billingInterval, attemptId)", source);
        Assert.Contains("checkout_url = url", source);
        Assert.Contains("expires_at = DateTime.UtcNow.AddHours(24)", source);
        Assert.Contains("idx_billing_checkout_pending_tenant", schema);
    }

    [Fact]
    public void StripeCheckout_SerializesConcurrentTenantSessions()
    {
        var source = ReadServerLogic("StripeBillingService.cs");
        var methodPos = source.IndexOf("CreateCheckoutSessionAsync", StringComparison.Ordinal);
        var lockPos = source.IndexOf("pg_advisory_xact_lock", methodPos, StringComparison.Ordinal);
        var subscriptionQueryPos = source.IndexOf("existingSubscription", methodPos, StringComparison.Ordinal);

        Assert.True(methodPos >= 0);
        Assert.True(lockPos > methodPos);
        Assert.True(subscriptionQueryPos > lockPos);
        Assert.Contains("BeginTransactionAsync", source[methodPos..subscriptionQueryPos]);
    }

    [Fact]
    public void AutomatedDietGeneration_BoundsMacroAndDietTypeInput()
    {
        var source = ReadServerController("DietController.cs");
        var start = source.IndexOf("GenerateAutomatedDiet", StringComparison.Ordinal);
        var end = source.IndexOf("// POST: api/dietas/validate", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var section = source[start..end];
        Assert.Contains("request.TargetProtein.Value > 2000", section);
        Assert.Contains("request.TargetCarbs.Value > 2000", section);
        Assert.Contains("request.TargetFat.Value > 2000", section);
        Assert.Contains("request.DietType.Length > 50", section);
    }

    [Fact]
    public void ProfileUpdates_BoundDatabaseBackedFields()
    {
        var dto = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Model", "ProfileDto.cs"));

        Assert.Contains("[StringLength(100)]", dto);
        Assert.Contains("[StringLength(150)]", dto);
        Assert.Contains("[StringLength(250)]", dto);
        Assert.Contains("[StringLength(50)]", dto);
        Assert.Contains("[StringLength(1_000_000)]", dto);
    }

    [Fact]
    public void AdminPlansAndBilling_BoundAndValidateMutationInput()
    {
        var plans = ReadServerController("AdminPlansController.cs");
        var billing = ReadServerController("BillingController.cs");

        Assert.Contains("if (!IsValidPlanLimits(r)", plans);
        Assert.Contains("r.Name.Trim().Length > 100", plans);
        Assert.Contains("f.FeatureCode.Trim().ToLowerInvariant()", plans);

        Assert.Contains("request.PlanCode.Trim().Length > 50", billing);
        Assert.Contains("request.BillingInterval.Trim().Length > 20", billing);
    }

    [Fact]
    public void AdminConfig_ValidatesFrontendUrlBeforePersisting()
    {
        var source = ReadServerController("AdminUsersController.cs");

        Assert.Contains("value.Length > 10000", source);
        Assert.Contains("config.nombre_config.Equals(\"frontendUrl\", StringComparison.OrdinalIgnoreCase)", source);
        Assert.Contains("frontendUri.Scheme != Uri.UriSchemeHttps", source);
        Assert.Contains("frontendUri.UserInfo.Length > 0", source);
        Assert.Contains("frontendUri.Query", source);
        Assert.Contains("frontendUri.Fragment", source);
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
    public void LocalFoodSearch_IsBoundedBeforeMaterializingResults()
    {
        var source = ReadServerLogica("OpenFoodFactsService.cs");
        Assert.Contains(".Take(50)", source);
        Assert.Contains(".OrderBy(f => f.name)", source);
    }

    [Fact]
    public void UnpagedCatalogResponses_AreBounded()
    {
        var clinic = ReadServerController("ClinicController.cs");
        var admin = ReadServerController("AdminUsersController.cs");
        var groups = ReadServerController("FoodExchangeGroupController.cs");
        var recipes = ReadServerController("RecipesController.cs");

        Assert.Contains(".Take(500)", clinic);
        Assert.Contains(".Take(1000)", clinic);
        Assert.Contains(".Take(500)", groups);
        Assert.Contains(".Take(500)", recipes);
    }

    [Fact]
    public void Biometrics_RequireActiveClient()
    {
        var source = ReadServerController("BiometricsController.cs");

        Assert.Contains("c.id == clientId && c.archived_at == null", source);
        Assert.Contains("x.id == id && x.client_id == clientId && x.client.archived_at == null", source);
    }

    [Fact]
    public void BiometricHistoryEndpoints_AreBounded()
    {
        var controller = ReadServerController("BiometricsController.cs");
        var clients = ReadServerController("ClientsController.cs");

        Assert.Contains(".Take(500)", controller);
        Assert.Contains(".Include(c => c.biometrics.OrderByDescending(b => b.measurement_date).Take(500))", clients);
    }

    [Fact]
    public void DietValidation_DoesNotTreatLocalFoodsAsGlobalWithoutTenant()
    {
        var source = ReadServerLogica("DietValidationService.cs");
        Assert.Contains("(f.source == null || f.source.ToLower() != \"local\")", source);
        Assert.DoesNotContain("(f.source != \"local\")", source);
    }

    [Fact]
    public void BiometricImportPreview_RejectsArchivedClients()
    {
        var source = ReadServerController("BiometricsController.cs");

        var start = source.IndexOf("public async Task<ActionResult<BioimpedancePreviewResponseDto>> PreviewImport", StringComparison.Ordinal);
        var end = source.IndexOf("// POST: api/clients/{clientId}/biometrics/import/confirm", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "No se encontró el endpoint PreviewImport.");
        var endpoint = source[start..end];

        Assert.Contains("c.archived_at == null", endpoint);
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
    public void LocalFoodScopeChecks_AreCaseInsensitive()
    {
        var food = ReadServerController("FoodController.cs");
        var diet = ReadServerController("DietController.cs");
        var recipes = ReadServerController("RecipesController.cs");

        Assert.Contains("f.source.ToLower()", food);
        Assert.Contains("f.source.ToLower()", diet);
        Assert.Contains("f.source.ToLower()", recipes);
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
        Assert.Contains("f.source.ToLower() != \"local\"", source);
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
            Assert.Contains("f.source.ToLower() != \"local\"", source);
            Assert.Contains("f.created_by_user_id == userId", source);
            Assert.Contains("f.tenant_id == tenantId.Value", source);
        }
    }

    [Fact]
    public void DietAndRecipeDetailReads_RecheckFoodTenantScope()
    {
        var diet = ReadServerController("DietController.cs");
        var recipes = ReadServerController("RecipesController.cs");

        var getDietStart = diet.IndexOf("public async Task<ActionResult<DietDetailDto>> GetDiet", StringComparison.Ordinal);
        var getDietEnd = diet.IndexOf("// POST:", getDietStart, StringComparison.Ordinal);
        Assert.True(getDietStart >= 0 && getDietEnd > getDietStart);
        Assert.DoesNotContain(".ThenInclude(i => i.food)", diet[getDietStart..getDietEnd]);
        Assert.DoesNotContain(".ThenInclude(ri => ri.food)", recipes);
        Assert.Contains("accessibleFoods", diet);
        Assert.Contains("accessibleFoods", recipes);
        Assert.Contains("f.tenant_id == tenantId.Value", diet);
        Assert.Contains("f.tenant_id == tenantId.Value", recipes);
    }

    [Fact]
    public void PatientPortalDietReads_StayWithinDietTenant()
    {
        var source = ReadServerController("PatientPortalController.cs");

        Assert.DoesNotContain(".ThenInclude(i => i.food)", source);
        Assert.Contains("accessibleFoods", source);
        Assert.Contains("shoppingFoods", source);
        Assert.Contains("f.tenant_id == d.tenant_id", source);
        Assert.Contains("f.tenant_id == diet.tenant_id", source);
    }

    [Fact]
    public void ClientDietReadsAndPdfs_RecheckFoodTenantScope()
    {
        var source = ReadServerController("ClientDietsController.cs");

        Assert.Contains("SanitizeDietFoodScopeAsync", source);
        Assert.Contains("accessibleFoods", source);
        Assert.Contains("f.source.ToLower()", source);
        Assert.Contains("f.tenant_id == tenantId.Value", source);
        Assert.Contains("f.created_by_user_id == userId", source);
    }

    [Fact]
    public void DietPdf_LocalFoodScopeRequiresCreatorOrClinicAdmin()
    {
        var source = ReadServerLogica("DietPdfService.cs");

        Assert.Contains("canUseTenantLocalFoods", source);
        Assert.Contains("f.created_by_user_id == userId", source);
        Assert.Contains("f.tenant_id == client.tenant_id.Value", source);
    }

    [Fact]
    public void DietValidation_LocalFoodScopeRequiresCreatorOrClinicAdmin()
    {
        var source = ReadServerLogica("DietValidationService.cs");

        Assert.Contains("canUseTenantLocalFoods", source);
        Assert.Contains("f.created_by_user_id == userId", source);
        Assert.Contains("tenantId.HasValue", source);
    }

    [Fact]
    public void DietGenerator_LocalFoodScopeRequiresCreatorOrClinicAdmin()
    {
        var source = ReadServerLogica("DietGeneratorService.cs");

        Assert.Contains("UserCanUseTenantLocalFood", source);
        Assert.Contains("food.created_by_user_id == userId", source);
        Assert.Contains("canUseTenantLocalFoods", source);
        Assert.Contains("tenantId.HasValue", source);
    }

    [Fact]
    public void DietGenerator_UsesCaseInsensitiveLocalFoodScope()
    {
        var source = ReadServerLogica("DietGeneratorService.cs");

        Assert.Contains("f.source.ToLower()", source);
        Assert.DoesNotContain("f.source != \"local\"", source);
    }

    [Fact]
    public void DietPdfService_UsesCaseInsensitiveLocalFoodScope()
    {
        var source = ReadServerLogica("DietPdfService.cs");

        Assert.Contains("f.source.ToLower()", source);
        Assert.DoesNotContain("f.source != \"local\"", source);
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

    [Fact]
    public void StripeCheckout_PersistsIdempotencyBeforeExternalCall()
    {
        var service = ReadServerLogica("StripeBillingService.cs");
        var schema = ReadServerLogica("BillingSchemaBootstrap.cs");
        var model = ReadServerModel("billing_checkout_attempts.cs");

        Assert.Contains("status = \"creating\"", service);
        Assert.Contains("await _context.SaveChangesAsync();", service);
        Assert.Contains("await transaction.CommitAsync();", service);
        Assert.Contains("SendStripeAsync(HttpMethod.Post, \"/v1/checkout/sessions\", form, idempotencyKey)", service);
        Assert.Contains("ALTER COLUMN checkout_url DROP NOT NULL", schema);
        Assert.Contains("WHERE status IN ('creating', 'pending')", schema);
        Assert.Contains("public string? checkout_url", model);
    }

    [Fact]
    public void AdminSubscriptionLifecycle_ResolvesCurrentHistoryRecord()
    {
        var source = ReadServerController("AdminUsersController.cs");

        Assert.Contains("s.status != \"cancelled\" && s.status != \"canceled\"", source);
        Assert.Contains(".OrderByDescending(s => s.created_at)", source);
        Assert.Contains(".Where(s => s.tenant_id == user.tenant_id.Value)", source);
    }

    [Fact]
    public void BillingAndAdministrationEndpoints_KeepExplicitAuthorizationBoundaries()
    {
        var billing = ReadServerController("BillingController.cs");
        var adminPlans = ReadServerController("AdminPlansController.cs");
        var adminUsers = ReadServerController("AdminUsersController.cs");
        var setup = ReadServerController("SetupController.cs");
        var portal = ReadServerController("PatientPortalController.cs");

        Assert.Contains("[Authorize(Policy = \"Professional\")]", billing);
        Assert.Contains("[Authorize(Roles = \"superadmin\")]", adminPlans);
        Assert.Contains("[Authorize(Roles = \"superadmin\")]", adminUsers);
        Assert.Contains("[EnableRateLimiting(\"auth\")]", setup);
        Assert.Contains("[Authorize(Roles = \"patient\")]", portal);
        Assert.Contains("[HttpPost(\"stripe/webhook\")]", billing);
        Assert.Contains("[AllowAnonymous]", billing);
    }

    [Fact]
    public void ProductionHardening_DoesNotExposeSwaggerOrWildcardCors()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));

        Assert.Contains("if (!app.Environment.IsDevelopment())", program);
        Assert.Contains("app.UseSwagger();", program);
        Assert.Contains("app.UseSwaggerUI();", program);
        Assert.Contains("WithOrigins(allowedOrigins)", program);
        var corsStart = program.LastIndexOf("builder.Services.AddCors(options =>", StringComparison.Ordinal);
        var corsEnd = program.IndexOf("// Add services to the container.", corsStart, StringComparison.Ordinal);
        Assert.True(corsStart >= 0 && corsEnd > corsStart);
        Assert.DoesNotContain("AllowAnyOrigin()", program[corsStart..corsEnd]);
        Assert.Contains("X-Content-Type-Options", program);
        Assert.Contains("Strict-Transport-Security", program);
        Assert.Contains("Cache-Control", program);
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

    private static string ReadServerModel(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Models", fileName));

    private static string ReadServerController(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", fileName));

    private static string ReadServerLogica(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", fileName));

    private static string ReadServerLogic(string fileName) => ReadServerLogica(fileName);

    [Fact]
    public void SuperAdminAccountCreation_NormalizesEmailIdentity()
    {
        var source = ReadServerController("AdminUsersController.cs");

        Assert.Contains("request.Email.Trim().ToLowerInvariant()", source);
        Assert.Contains("u.email != null && u.email.ToLower() == email", source);
    }

    [Fact]
    public void PatientPortal_ClientPreview_AllowsTenantClinicAdmins()
    {
        var source = ReadServerController("PatientPortalController.cs");

        Assert.Contains("User.IsInRole(\"superadmin\")", source);
        Assert.Contains("User.IsInRole(\"clinic_admin\")", source);
        Assert.Contains("c.tenant_id == AuthHelpers.GetTenantId(User)!.Value", source);
    }

    [Fact]
    public void PatientPasscode_RequiresExactlySixDigits()
    {
        var source = ReadServerController("PatientPortalController.cs");

        Assert.Contains("Regex.IsMatch(dto.Passcode, @\"^\\d{6}$\")", source);
        Assert.DoesNotContain("Regex.IsMatch(dto.Passcode, @\"^\\\\d{6}$\")", source);
    }

    [Fact]
    public void PatientPortalAccess_DoesNotReturnStoredHashedToken()
    {
        var source = ReadServerController("PatientPortalController.cs");

        Assert.Contains("MagicLink = string.Empty", source);
        Assert.Contains("AccessToken = string.Empty", source);
        Assert.DoesNotContain("var magicLink = $\"/patient?token={client.access_token}\"", source);
    }

    [Fact]
    public void PatientMagicLinks_StoreOnlyHashedTokens()
    {
        var source = ReadServerController("PatientPortalController.cs");

        Assert.Contains("HashAccessToken(request.Token)", source);
        Assert.Contains("client.access_token = HashAccessToken(rawToken)", source);
        Assert.Contains("SHA256.HashData", source);
        Assert.Contains("AccessToken = rawToken", source);
    }

    [Fact]
    public void PatientMagicLinks_ConsumeTheStoredHashAndNeverCompareRawTokenToHash()
    {
        var source = ReadServerController("PatientPortalController.cs");

        Assert.Contains("c.access_token == HashAccessToken(request.Token)", source);
        Assert.DoesNotContain("c.access_token == request.Token", source);
    }

    [Fact]
    public void Biometrics_CreateAndUpdateRejectNonFiniteValuesAndOversizedNotes()
    {
        var source = ReadServerController("BiometricsController.cs");

        Assert.Contains("HasInvalidMeasurementValues(dto)", source);
        Assert.Contains("double.IsNaN(v.Value)", source);
        Assert.Contains("double.IsInfinity(v.Value)", source);
        Assert.Contains("dto.Notes?.Length > 5000", source);
        Assert.Contains("dto.MeasurementDate == default", source);
    }

    [Fact]
    public void FoodExternalIds_AreDatabaseUnique()
    {
        var source = ReadServerLogica("DatabaseBootstrap.cs");

        Assert.Contains("uq_foods_external_id ON foods(external_id) WHERE external_id IS NOT NULL", source);
    }

    [Fact]
    public void BiometricImportParser_BoundsInputExpansion()
    {
        var source = ReadServerLogica("BioimpedanceParserService.cs");

        Assert.Contains("const int maxLines = 20000", source);
        Assert.Contains("const int maxLineLength = 10000", source);
        Assert.Contains("rawLines.Length > maxLines", source);
        Assert.Contains("rawLines.Any(l => l.Length > maxLineLength)", source);
    }

    [Fact]
    public void GeneratedUsernames_AreComparedCaseInsensitively()
    {
        var auth = ReadServerController("AuthController.cs");
        var clinic = ReadServerController("ClinicController.cs");
        var admin = ReadServerController("AdminUsersController.cs");

        Assert.Contains("u.username.ToLower() == candidate.ToLower()", auth);
        Assert.Contains("u.username.ToLower() == username.ToLower()", clinic);
        Assert.Contains("u.username.ToLower() == username.ToLower()", admin);
    }


    [Fact]
    public void DietCreateAndUpdate_BoundPayloadExpansion()
    {
        var source = ReadServerController("DietController.cs");

        Assert.Contains("days.Count > 31", source);
        Assert.Contains("d.Meals.Count > 12", source);
        Assert.Contains("m.Items.Count > 100", source);
        Assert.Contains(".Count() > 2000", source);
        Assert.Contains("m.Name.Length > 100", source);
        Assert.Contains("notes?.Length > 10000", source);
        Assert.Contains("i.Grams.Value > 100000", source);
        Assert.Contains("i.ExchangeCount.Value > 10000", source);
        Assert.Contains("ValidateDietPayload(dto?.Name, dto?.Notes, dto?.Days)", source);
    }


    [Fact]
    public void ClientCreateAndUpdate_BoundProfilePayload()
    {
        var source = ReadServerController("ClientsController.cs");

        Assert.Contains("fullName.Length > 200", source);
        Assert.Contains("email?.Length > 254", source);
        Assert.Contains("phone?.Length > 50", source);
        Assert.Contains("notes?.Length > 10000", source);
        Assert.Contains("medical.Surgeries?.Length > 5000", source);
        Assert.Contains("digestive.IntestinalHabits?.Length > 5000", source);
        Assert.Contains("preferences.PreferredFoods?.Length > 5000", source);
        Assert.Contains("lifestyle.WorkSchedule?.Length > 5000", source);
        Assert.Contains("ValidateClientPayload(dto)", source);
    }

}