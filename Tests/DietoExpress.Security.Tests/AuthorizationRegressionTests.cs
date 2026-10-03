using Xunit;
using System.Security.Claims;

namespace DietoExpress.Security.Tests;

public class AuthorizationRegressionTests
{
    // Estas pruebas inspeccionan el código desplegable directamente para detectar regresiones de autorización, límites de entrada y configuración de sesión sin depender de una base de datos real.
    // Resolver la raíz desde AppContext permite que las pruebas funcionen tanto localmente como dentro del runner de CI.
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
        var portal = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.ts"));

        Assert.DoesNotContain("getPatientToken", portal);
        Assert.DoesNotContain("PATIENT_TOKEN_KEY", portal);
        Assert.DoesNotContain("savePatientToken", portal);
        Assert.DoesNotContain("localStorage.setItem('patient", portal);
    }

    // La sesión del paciente se considera revocable cuando cambia la versión almacenada; el test verifica que el claim y el filtro de autenticación estén conectados.
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
        Assert.Contains("dietoexpress_professional_session", auth);
        Assert.Contains("HttpOnly = true", auth);
        Assert.Contains("SameSite = SameSiteMode.Strict", auth);
        Assert.Contains("MaxAge = TimeSpan.FromHours(3)", auth);
        Assert.Contains("dietoexpress_professional_session", program);
        Assert.DoesNotContain("localStorage", service);
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

    // El checkout es especialmente sensible a carreras: dos peticiones simultáneas no deben crear estados de suscripción incompatibles dentro del mismo tenant.
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

    // Las mutaciones de asignaciones se comprueban como operaciones serializadas porque los límites y el estado de las relaciones pueden cambiar concurrentemente.
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
        Assert.Contains("CanCreateDietAsync(tenantIdValue, userId.Value)", source);
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
        Assert.DoesNotContain("(f.source != \"local\" || !tenantId.HasValue || f.tenant_id == tenantId.Value)", source);
    }

    [Fact]
    public void DatabaseBootstrap_EnforcesOneSubscriptionPerTenant()
    {
        var source = ReadServerLogica("DatabaseBootstrap.cs");
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS idx_subscriptions_tenant_active_unique", source);
        Assert.Contains("WHERE status NOT IN ('cancelled', 'canceled')", source);
        Assert.DoesNotContain("idx_subscriptions_tenant_id_unique", source);
        
        var billing = ReadServerLogica("BillingSchemaBootstrap.cs");
        Assert.Contains("DROP INDEX IF EXISTS idx_subscriptions_tenant_id_unique", billing);
        Assert.Contains("ON subscriptions(payment_provider, provider_customer_id)", billing);
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
        Assert.Equal("pg_advisory_xact_lock(748392616)", clinic.Substring(clinicLock, clinic.IndexOf(")", clinicLock) - clinicLock + 1));
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
    public void LicenseService_ValidatesNutritionistAndClientTenantScope()
    {
        var source = ReadServerLogica("LicenseService.cs");

        Assert.Contains("u.id == nutritionistId", source);
        Assert.Contains("u.tenant_id == tenantId.Value", source);
        Assert.Contains("u.archived_at == null", source);
        Assert.Contains("c.id == clientId", source);
        Assert.Contains("c.tenant_id == tenantId.Value", source);
        Assert.Contains("c.archived_at == null", source);
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
    public void UserEmailIdentity_IsCaseInsensitiveAcrossRegistrationAndClinicCreation()
    {
        var auth = ReadServerController("AuthController.cs");
        var clinic = ReadServerController("ClinicController.cs");
        var schema = ReadServerLogica("DatabaseBootstrap.cs");

        Assert.Contains("var normalizedEmail = req.Email.Trim().ToLowerInvariant();", auth);
        Assert.Contains("var email = req.Email.Trim().ToLowerInvariant();", clinic);
        Assert.Contains("uq_users_email_ci ON users(LOWER(email))", schema);
    }

    [Fact]
    public void OpenFoodFactsSync_SerializesExternalFoodWrites()
    {
        var source = ReadServerLogica("OpenFoodFactsService.cs");

        Assert.Contains("BeginTransactionAsync", source);
        Assert.Contains("pg_advisory_xact_lock(hashtextextended", source);
        Assert.Contains("external_id == product.Code", source);
        Assert.Contains("transaction.CommitAsync", source);
    }

    [Fact]
    public void OpenFoodFactsSync_CannotOverwriteTenantLocalFoods()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "OpenFoodFactsService.cs"));

        Assert.Contains("f.external_id == product.Code", source);
        Assert.Contains("existing.source.ToLower() == \"local\"", source);
        Assert.Contains("existing.tenant_id.HasValue", source);
        Assert.Contains("se omite la sincronización del alimento local", source);
        Assert.Contains("product.Id = existing.id", source);
    }

    [Fact]
    public void OpenFoodFactsRefresh_RevalidatesGlobalFoodScopeBeforeUpdate()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "OpenFoodFactsService.cs"));

        Assert.Contains("f.id == food.id", source);
        Assert.Contains("f.source.ToLower() != \"local\"", source);
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
    public void StripeWebhook_UsesEventIdAsTieBreakerForEqualTimestamps()
    {
        var source = ReadServerController("BillingController.cs");

        Assert.Contains("ProcessStripeEventAsync(root, eventType, eventId)", source);
        Assert.Contains("private async Task ProcessStripeEventAsync(JsonElement root, string eventType, string eventId)", source);
        Assert.Contains("stripeEventCreatedAt.Value == subscription.last_stripe_event_created_at.Value", source);
        Assert.Contains("string.CompareOrdinal(eventId, subscription.last_stripe_event_id)", source);
        Assert.Contains("last_stripe_event_id = eventId", source);
    }

    [Fact]
    public void StripeCheckoutWebhook_CreatesNewSubscriptionAfterCancelledHistory()
    {
        var source = ReadServerController("BillingController.cs");

        Assert.Contains("if (subscription == null)", source);
        Assert.Contains("new subscriptions", source);
        Assert.Contains("payment_provider = \"stripe\"", source);
        Assert.Contains("provider_subscription_id = subscriptionId", source);
        Assert.Contains("status = paymentStatus == \"paid\" ? \"active\" : \"past_due\"", source);
    }

    [Fact]
    public void StripeSubscriptionSchema_AllowsNewSubscriptionAfterCancelledHistory()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "BillingSchemaBootstrap.cs"));

        Assert.Contains("UPDATE subscriptions", source);
        Assert.Contains("WHERE status IS NULL", source);
        Assert.Contains("ALTER COLUMN status SET NOT NULL", source);
        Assert.Contains("idx_subscriptions_provider_customer", source);
        Assert.Contains("status NOT IN ('cancelled', 'canceled')", source);
    }

    [Fact]
    public void StripeCheckoutWebhook_RejectsStaleEventsAndInvalidPlanMetadata()
    {
        var source = ReadServerController("BillingController.cs");

        Assert.Contains("subscription.last_stripe_event_created_at.HasValue", source);
        Assert.Contains("SingleOrDefaultAsync(s => s.tenant_id == tenantId.Value", source);
        Assert.Contains("subscription.provider_subscription_id", source);
        Assert.Contains("subscription.provider_customer_id", source);
        Assert.Contains("stripeEventCreatedAt.Value < subscription.last_stripe_event_created_at.Value", source);
        Assert.Contains("stripeEventCreatedAt.Value == subscription.last_stripe_event_created_at.Value", source);
        Assert.Contains("string.CompareOrdinal(eventId, subscription.last_stripe_event_id) <= 0", source);
        Assert.Contains("FirstOrDefaultAsync(p => p.id == planId.Value && p.active)", source);
        Assert.Contains("plan.code is \"free\" or \"demo_nutri\" or \"trial_nutri\"", source);
        Assert.Contains("interval is not (\"monthly\" or \"yearly\")", source);
        Assert.Contains("s.payment_provider == \"stripe\" && s.provider_subscription_id == providerSubscriptionId", source);
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
    public void PatientDocuments_ProfessionalEndpointsRequireProfessionalPolicy()
    {
        var source = ReadServerController("PatientDocumentsController.cs");

        foreach (var marker in new[]
        {
            "api/clients/{clientId:int}/documents",
            "api/clients/{clientId:int}/documents/summary",
            "api/clients/{clientId:int}/documents/{documentId:long}",
            "api/clients/{clientId:int}/documents/{documentId:long}/audit"
        })
        {
            AssertEndpointAttributePair(source, marker);
        }

        Assert.Contains("[Authorize]", source);
        Assert.Contains("AuthHelpers.IsPatient(User)", source);
    }

    [Fact]
    public void PatientDocuments_DownloadsEnforceTenantAndPrivateStorage()
    {
        var source = ReadServerController("PatientDocumentsController.cs");

        Assert.Contains("tenant_id = {2}", source);
        Assert.Contains("GetSafePhysicalPath", source);
        Assert.Contains("DIETOEXPRESS_DOCUMENTS_PATH", source);
        Assert.DoesNotContain("wwwroot", source);
        Assert.Contains("storage_key", source);
        Assert.Contains("revoked_at IS NULL", source);
    }

    [Fact]
    public void PatientDocuments_AcceptanceAuditsExactVersionAndHash()
    {
        var source = ReadServerController("PatientDocumentsController.cs");

        Assert.Contains("DocumentAcceptanceSnapshot", source);
        Assert.Contains("documentSnapshot.Version", source);
        Assert.Contains("documentSnapshot.Sha256", source);
        Assert.Contains("event_type, ip_address, user_agent, details", source);
        Assert.Contains("signed_at=NOW()", source);
    }

    [Fact]
    public void DocumentTemplateVersions_AreCaseInsensitiveByName()
    {
        var source = ReadServerController("DocumentTemplatesController.cs");

        Assert.Contains("LOWER(name)=LOWER(@name)", source);
        Assert.DoesNotContain("tenant=@tenant AND name=@name", source);
    }

    [Fact]
    public void DocumentTemplateVersions_AreSerializedPerTenantAndName()
    {
        var source = ReadServerController("DocumentTemplatesController.cs");

        Assert.Contains("pg_advisory_xact_lock", source);
        Assert.Contains("hashtextextended", source);
        Assert.Contains("document-template:{name.Trim().ToLowerInvariant()}", source);
        Assert.Contains("SET is_active=false", source);
        Assert.Contains("LOWER(name)=LOWER(@name)", source);
    }

    [Fact]
    public void DocumentTemplateActivation_CannotLeaveMultipleActiveVersions()
    {
        var source = ReadServerController("DocumentTemplatesController.cs");
        var bootstrap = ReadServerLogica("DatabaseBootstrap.cs");

        Assert.Contains("SET is_active=false", source);
        Assert.Contains("id<>@id", source);
        Assert.Contains("uq_document_templates_tenant_name_active_ci", bootstrap);
        Assert.Contains("PARTITION BY tenant_id, LOWER(name)", bootstrap);
    }

    [Fact]
    public void PatientDocuments_NewVersionsPreserveHistoryAndRequireNewAcceptance()
    {
        var source = ReadServerLogica("PatientDocumentService.cs");

        Assert.Contains("includeAllRequired", source);
        Assert.Contains("LOWER(name)", source);
        Assert.Contains("revoked_at=NOW(), status='revoked'", source);
        Assert.Contains("event_type, details", source);
        Assert.Contains("'superseded'", source);
        Assert.Contains("la aceptación de la versión anterior se conserva", source);
    }

    [Fact]
    public void PatientDocuments_ConsultationChecksOnlyConsultationRequiredTemplates()
    {
        var source = ReadServerLogica("PatientDocumentService.cs");
        var controller = ReadServerController("ProfessionalConsultationsController.cs");

        Assert.Contains("is_required_before_consultation=true", source);
        Assert.Contains("GetPendingSignatureDocumentsBeforeConsultationAsync", controller);
        Assert.Contains("forClientCreation: false", controller);
        Assert.Contains("includeAllRequired: false", controller);
    }


    [Fact]
    public void PatientPortal_DoesNotProvisionConsultationOnlyDocuments()
    {
        var source = ReadServerController("PatientDocumentsController.cs");
        var portalPos = source.IndexOf("ListForPatient", StringComparison.Ordinal);
        Assert.True(portalPos >= 0);

        var portal = source[portalPos..];
        Assert.Contains("forClientCreation: true", portal);
        Assert.Contains("includeAllRequired: false", portal);
        Assert.DoesNotContain("forClientCreation: false", portal[..Math.Min(portal.Length, 1200)]);
    }

    [Fact]
    public void NewPatientDocumentReminders_RequirePendingSignatureDocuments()
    {
        var controller = ReadServerController("ClientsController.cs");

        Assert.Contains("GetPendingSignatureDocumentsAsync", controller);
        Assert.Contains("pendingSignatureDocuments.Count > 0", controller);
        Assert.Contains("documents:pending-reminder:{client.id}:24h", controller);
        Assert.Contains("documents:pending-reminder:{client.id}:72h", controller);
    }

    [Fact]
    public void ConsultationDocumentReminders_ArePatientScopedAndCancellable()
    {
        var controller = ReadServerController("ProfessionalConsultationsController.cs");
        var automation = ReadServerLogica("AutomationService.cs");

        Assert.Contains("documents:pending-reminder:{appointment.ClientId}:consultation:{appointment.Id}:24h", controller);
        Assert.Contains("documents:pending-reminder:{appointment.ClientId}:consultation:{appointment.Id}:72h", controller);
        Assert.Contains("DateTime.UtcNow.AddHours(24)", controller);
        Assert.Contains("DateTime.UtcNow.AddHours(72)", controller);
        Assert.Contains("documents:pending-reminder:{clientId}:", automation);
        Assert.Contains("status='cancelled'", automation);
    }

    [Fact]
    public void PatientDocuments_NonSignatureDocumentsAreNotPendingSignatures()
    {
        var service = ReadServerLogica("PatientDocumentService.cs");
        var controller = ReadServerController("PatientDocumentsController.cs");

        Assert.Contains(@"template.RequiresSignature ? ""pending"" : ""available""", service);
        Assert.Contains("requires_signature=true", service);
        Assert.Contains("status='pending'", service);
        Assert.Contains("requires_signature = true AND status = 'pending'", controller);
    }



    [Fact]
    public void PatientDocumentProvisioning_SerializesConcurrentRequestsPerPatient()
    {
        var service = ReadServerLogic("PatientDocumentService.cs");

        Assert.Contains("pg_advisory_xact_lock(hashtextextended(@lockKey, 0))", service);
        Assert.Contains("patient-documents:{tenantId}:{clientId}", service);
        Assert.Contains("WHERE NOT EXISTS", service);
    }

    [Fact]
    public void PatientDocumentProvisioning_CleansCopiedFilesWhenTransactionFails()
    {
        var source = ReadServerLogica("PatientDocumentService.cs");

        Assert.Contains("var copiedFiles = new List<string>();", source);
        Assert.Contains("copiedFiles.Add(destination);", source);
        Assert.Contains("await transaction.RollbackAsync(cancellationToken);", source);
        Assert.Contains("foreach (var copiedFile in copiedFiles)", source);
        Assert.Contains("File.Delete(copiedFile)", source);
    }

    [Fact]
    public void PatientDocumentAcceptance_IsIdempotentUnderConcurrentRetries()
    {
        var source = ReadServerController("PatientDocumentsController.cs");

        Assert.Contains("AND revoked_at IS NULL AND requires_signature=true AND status='pending'", source);
        Assert.Contains(@"if (string.Equals(currentStatus, ""signed"", StringComparison.OrdinalIgnoreCase)) return NoContent();", source);
        Assert.Contains("no duplicamos auditoría ni automatizaciones", source);
        Assert.Contains("documentSnapshot.Sha256", source);
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

    [Fact]
    public void PrivacyOperations_AreTenantScopedAndProfessionalOnly()
    {
        var controller = ReadServerController("PrivacyOperationsController.cs");
        var service = ReadServerLogica("PrivacyOperationsService.cs");
        var schema = ReadServerLogica("DatabaseBootstrap.cs");

        Assert.Contains("[Authorize(Policy = \"Professional\")]", controller);
        Assert.Contains("WHERE id=@id AND tenant_id=@tenant", service);
        Assert.Contains("WHERE tenant_id=@tenant", service);
        Assert.Contains("tenant_id INTEGER NOT NULL REFERENCES tenants(id)", schema);
        Assert.Contains("CREATE TABLE IF NOT EXISTS privacy_requests", schema);
        Assert.Contains("CREATE TABLE IF NOT EXISTS privacy_incidents", schema);
    }

    [Fact]
    public void PrivacyRequests_ValidatePatientTenantBeforePersisting()
    {
        var service = ReadServerLogica("PrivacyOperationsService.cs");

        Assert.Contains("EnsureClientBelongsToTenantAsync(request.ClientId, tenantId, ct)", service);
        Assert.Contains("FROM clients WHERE id=@client AND tenant_id=@tenant", service);
        Assert.Contains("CREATE_PRIVACY_REQUEST", service);
        Assert.Contains("UPDATE_PRIVACY_REQUEST", service);
    }

    [Fact]
    public void PrivacyIncidentRegister_DoesNotStoreSecretsOrClinicalPayloadByDesign()
    {
        var service = ReadServerLogica("PrivacyOperationsService.cs");
        var schema = ReadServerLogica("DatabaseBootstrap.cs");

        Assert.Contains("CREATE_PRIVACY_INCIDENT", service);
        Assert.DoesNotContain("password", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("description TEXT NOT NULL", schema);
        Assert.Contains("data_categories VARCHAR(500)", schema);
        Assert.DoesNotContain("clinical_content", schema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PatientExport_IsTenantScopedAndExcludesAuthenticationSecrets()
    {
        var service = ReadServerLogica("PrivacyOperationsService.cs");
        var controller = ReadServerController("PrivacyOperationsController.cs");

        Assert.Contains("c.tenant_id == tenantId", service);
        Assert.Contains("EXPORT_PATIENT_DATA", service);
        Assert.DoesNotContain("passcode_hash", service);
        Assert.DoesNotContain("access_token", service);
        Assert.DoesNotContain("access_token_expires_at", service);
        Assert.Contains("clients/{clientId:int}/export", controller);
    }

    [Fact]
    public void ErasureWorkflow_DoesNotPerformImmediatePhysicalDelete()
    {
        var service = ReadServerLogica("PrivacyOperationsService.cs");

        Assert.Contains("right_type='erasure'", service);
        Assert.Contains("pendiente de revisión de obligaciones de conservación", service);
        Assert.DoesNotContain("DELETE FROM clients", service);
        Assert.DoesNotContain("_context.clients.Remove", service);
    }

}