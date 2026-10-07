using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Google.Apis.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using Serilog;
using System;
using System.Reflection;
using System.Security.Claims;
using System.Text;

namespace Anguloso.Server;

// Punto de composición de la aplicación: aquí se registran autenticación, persistencia, servicios y middleware en el orden que define el pipeline.
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        string entorno = builder.Environment.ContentRootPath;
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        builder.Services.AddDbContext<angulosodbContext>(options => options.UseNpgsql(connectionString));
        string pathLogs = builder.Configuration["DIETOEXPRESS_LOG_PATH"] ?? (OperatingSystem.IsWindows() ? Path.Combine(builder.Environment.ContentRootPath, "Logs") : "/var/lib/dietoexpress/Logs");
        Directory.CreateDirectory(pathLogs);
        builder.Host.UseSerilog((context, loggerConfiguration) => { loggerConfiguration.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console().WriteTo.File(Path.Combine(pathLogs, "log-.txt"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: null, shared: true); });
        builder.Services.AddCors(options => { options.AddPolicy("AllowAngularApp", policy => { var allowedOrigins = builder.Environment.IsDevelopment() ? new[] { "http://localhost:4200", "https://localhost:4200", "http://127.0.0.1:4200", "https://127.0.0.1:4200" } : builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>(); policy.WithOrigins(allowedOrigins).WithHeaders("Authorization", "Content-Type", "Accept").WithMethods("GET", "POST", "PUT", "PATCH", "DELETE"); }); });
// Add services to the container.
        builder.Services.AddSingleton<LogServ>();
        builder.Services.AddSingleton<ConfigServ>(sp => new ConfigServ(connectionString!, sp.GetRequiredService<LogServ>()));
        builder.Services.AddSingleton<EmailServ>(sp => new EmailServ(sp.GetRequiredService<ConfigServ>(), sp.GetRequiredService<LogServ>()));
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddScoped<SupportEnhancementService>();
        builder.Services.AddScoped<SupportService>();
        // El servicio comparte la lógica de publicación entre peticiones y el worker; el worker separado procesa los jobs sin bloquear las peticiones HTTP.
        builder.Services.AddSingleton<AutomationService>();
        builder.Services.AddScoped<PatientPortalAccessService>(); builder.Services.AddScoped<PatientDocumentService>(); builder.Services.AddSingleton<PatientDocumentTemplateSeeder>(); builder.Services.AddScoped<PrivacyOperationsService>(); builder.Services.AddScoped<LegalGovernanceService>();
        builder.Services.AddDataProtection();
        builder.Services.AddHttpClient();

        // La tabla config de PostgreSQL es la fuente de verdad de los proveedores de direcciones.
        // Esto evita duplicar claves entre appsettings, variables de entorno y la base de datos.
        builder.Services.AddSingleton<AddressProviderOptions>(sp =>
            AddressProviderOptions.Load(sp.GetRequiredService<ConfigServ>()));
        builder.Services.AddSingleton<AddressUsageService>();
        builder.Services.AddSingleton<IAddressProvider, GeoapifyAddressProvider>();
        builder.Services.AddSingleton<IAddressProvider, LocationIqAddressProvider>();
        builder.Services.AddSingleton<GoogleCalendarService>();
        builder.Services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<ConfigServ>();
            return new LiveKitVideoOptions
            {
                Enabled = config.GetConfigBool("VIDEO_LIVEKIT_ENABLED", false) ?? false,
                ServerUrl = config.GetConfigString("VIDEO_LIVEKIT_URL") ?? string.Empty,
                ApiKey = config.GetConfigString("VIDEO_LIVEKIT_API_KEY") ?? string.Empty,
                ApiSecret = config.GetConfigString("VIDEO_LIVEKIT_API_SECRET") ?? string.Empty,
                RoomExpiryMinutesAfterAppointment = config.GetConfigInt("VIDEO_LIVEKIT_ROOM_EXPIRY_MINUTES", 30) ?? 30,
                RoomCreationLeadMinutes = config.GetConfigInt("VIDEO_LIVEKIT_ROOM_CREATION_LEAD_MINUTES", 60) ?? 60,
                EmptyRoomTimeoutSeconds = config.GetConfigInt("VIDEO_LIVEKIT_EMPTY_ROOM_TIMEOUT_SECONDS", 300) ?? 300,
                MaxCallDurationMinutes = config.GetConfigInt("VIDEO_LIVEKIT_MAX_CALL_DURATION_MINUTES", 60) ?? 60
            };
        });
        builder.Services.AddSingleton<IVideoMeetingProvider, LiveKitVideoMeetingProvider>();
        builder.Services.AddScoped<VideoQuotaService>();
        builder.Services.AddScoped<LiveKitAnalyticsService>();
        builder.Services.AddHostedService<GoogleCalendarWorker>();
        builder.Services.AddHostedService<AutomationWorker>();
        builder.Services.AddHttpClient<IStripeBillingService, StripeBillingService>();
        builder.Services.AddHttpClient<OpenFoodFactsService>().AddTypedClient((httpClient, sp) => new OpenFoodFactsService(httpClient, connectionString!, sp.GetRequiredService<LogServ>(), sp.GetRequiredService<ConfigServ>()));
        var jwtKey = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Jwt:Key debe estar configurada y contener al menos 32 bytes.");
        var jwtIssuer = builder.Configuration["Jwt:Issuer"];
        var jwtAudience = builder.Configuration["Jwt:Audience"];
        if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience)) throw new InvalidOperationException("Jwt:Issuer y Jwt:Audience deben estar configurados.");
        var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, ValidIssuer = jwtIssuer, ValidAudience = jwtAudience, IssuerSigningKey = new SymmetricSecurityKey(keyBytes), NameClaimType = ClaimTypes.Name };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context => { if (string.IsNullOrWhiteSpace(context.Token)) { if (context.Request.Path.StartsWithSegments("/api/portal") && context.Request.Cookies.TryGetValue("dietoexpress_patient_session", out var patientCookie)) context.Token = patientCookie; else if (context.Request.Cookies.TryGetValue("dietoexpress_professional_session", out var professionalCookie)) context.Token = professionalCookie; } return Task.CompletedTask; },
                OnTokenValidated = async context =>
                {
                    if (context.Principal?.IsInRole("patient") == true)
                    {
                        var clientIdClaim = context.Principal.FindFirstValue("clientId");
                        if (!int.TryParse(clientIdClaim, out var clientId)) { context.Fail("Sesión de paciente no válida."); return; }
                        var portalTokenVersionClaim = context.Principal.FindFirstValue("portalTokenVersion");
                        if (!int.TryParse(portalTokenVersionClaim, out var portalTokenVersion)) { context.Fail("Sesión de paciente sin versión de seguridad."); return; }
                        var patientDb = context.HttpContext.RequestServices.GetRequiredService<angulosodbContext>();
                        var client = await patientDb.clients.AsNoTracking().Where(c => c.id == clientId && c.archived_at == null && c.portal_token_version == portalTokenVersion).Select(c => new { c.id, c.tenant_id, UserTenantId = c.user != null ? c.user.tenant_id : null }).FirstOrDefaultAsync();
                        if (client == null) { context.Fail("Expediente no disponible."); return; }
                        var tenantId = client.tenant_id ?? client.UserTenantId;
                        var licenseService = context.HttpContext.RequestServices.GetRequiredService<ILicenseService>();
                        if (!await licenseService.CanUseFeatureAsync(tenantId, "CLIENT_PORTAL")) context.Fail("Portal de pacientes no disponible.");
                        return;
                    }
                    var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (!int.TryParse(userIdClaim, out var userId)) { context.Fail("Identidad de usuario no válida."); return; }
                    var tokenVersionClaim = context.Principal?.FindFirstValue("tokenVersion");
                    if (!int.TryParse(tokenVersionClaim, out var tokenVersion)) { context.Fail("Token sin versión de seguridad."); return; }
                    var db = context.HttpContext.RequestServices.GetRequiredService<angulosodbContext>();
                    var user = await db.users.AsNoTracking().Where(u => u.id == userId).Select(u => new { u.archived_at, u.token_version, u.role, u.subscription_plan, u.subscription_status }).FirstOrDefaultAsync();
                    if (user == null || user.archived_at != null) { context.Fail("Cuenta no disponible."); return; }
                    if (user.token_version != tokenVersion) { context.Fail("Sesión revocada."); return; }
                    if (context.Principal?.Identity is ClaimsIdentity identity) { foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList()) identity.RemoveClaim(claim); identity.AddClaim(new Claim(ClaimTypes.Role, user.role ?? "nutritionist")); foreach (var claim in identity.FindAll("subscriptionPlan").ToList()) identity.RemoveClaim(claim); identity.AddClaim(new Claim("subscriptionPlan", user.subscription_plan ?? "free")); foreach (var claim in identity.FindAll("subscriptionStatus").ToList()) identity.RemoveClaim(claim); identity.AddClaim(new Claim("subscriptionStatus", user.subscription_status ?? "active")); }
                }
            };
        });
        QuestPDF.Settings.License = LicenseType.Community;
        builder.Services.AddSingleton<AddressAutocompleteService>(); builder.Services.AddSingleton<DietPdfService>(); builder.Services.AddSingleton<EnergyCalculatorService>(); builder.Services.AddSingleton<AnthropometryCalculatorService>(); builder.Services.AddScoped<DietValidationService>(); builder.Services.AddScoped<SpecializationRulesService>(); builder.Services.AddScoped<FoodSubstitutionService>(); builder.Services.AddSingleton<BioimpedanceParserService>(); builder.Services.AddScoped<DietGeneratorService>(); builder.Services.AddScoped<DietRegenerationService>();
        builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<AppointmentConcurrencyService>(); builder.Services.AddScoped<ITenantContextService, TenantContextService>(); builder.Services.AddScoped<IAuditLogService, AuditLogService>(); builder.Services.AddScoped<IPublicFunnelAnalyticsService, PublicFunnelAnalyticsService>(); builder.Services.AddScoped<ILicenseService, LicenseService>();
        builder.Services.AddRateLimiter(options => { options.RejectionStatusCode = StatusCodes.Status429TooManyRequests; options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true })); options.AddPolicy("expensive", httpContext => RateLimitPartition.GetFixedWindowLimiter($"{httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}:expensive", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true })); });
        builder.Services.AddAuthorization(options => { options.AddPolicy("Professional", policy => policy.RequireAuthenticatedUser().RequireAssertion(ctx => !ctx.User.IsInRole("patient"))); });
        builder.Services.AddControllers(); builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen(options => { var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"; options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename)); });
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 10 * 1024 * 1024);
        var app = builder.Build();
        bool databaseReady = false;
        using (var scope = app.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            try
            {
                var context = scope.ServiceProvider.GetRequiredService<angulosodbContext>();
                // Esta tabla se crea antes del resto del bootstrap para poder registrar el fallo si una
                // migración posterior rompe el arranque. El propio mecanismo de alerta nunca bloquea el proceso.
                context.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS system_alerts (
    id BIGSERIAL PRIMARY KEY,
    severity VARCHAR(20) NOT NULL,
    component VARCHAR(120) NOT NULL,
    title VARCHAR(250) NOT NULL,
    message VARCHAR(1000) NOT NULL,
    technical_details TEXT,
    first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    occurrences INTEGER NOT NULL DEFAULT 1,
    resolved_at TIMESTAMPTZ,
    resolved_by_user_id INTEGER,
    CONSTRAINT system_alerts_severity_check CHECK (severity IN ('critical','error','warning','info'))
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_system_alerts_active_component_title
    ON system_alerts(component, title)
    WHERE resolved_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_system_alerts_active
    ON system_alerts(resolved_at, severity, last_seen_at DESC);
");
                DatabaseBootstrap.InitializeDatabaseAsync(context, logger);
                DatabaseBootstrap.EnsureCurrentSchema(context, logger);
                DatabaseBootstrap.UpgradeDocumentTemplateSchemaV1(context, logger);
                DatabaseBootstrap.UpgradeLegalComplianceSchemaV1(context, logger); DatabaseBootstrap.UpgradeLegalConfigurationSchemaV1(context, logger); DatabaseBootstrap.UpgradeLegalGeneratedDocumentsSchemaV1(context, logger); DatabaseBootstrap.UpgradePrivacyOperationsSchemaV1(context, logger); DatabaseBootstrap.UpgradeLegalGovernanceSchemaV1(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchema(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV2(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV3(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV4(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV5(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV6(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV7(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV8(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV9(context, logger); DatabaseBootstrap.UpgradeMessagingSchemaV2(context, logger); DatabaseBootstrap.UpgradeAutomationSchemaV1(context, logger);
    DatabaseBootstrap.UpgradeAutomationSchemaV2(context, logger); DatabaseBootstrap.UpgradeAutomationSchemaV3(context, logger); DatabaseBootstrap.UpgradeAutomationSchemaV4(context, logger); DatabaseBootstrap.UpgradeAutomationSchemaV5(context, logger);
                DatabaseBootstrap.UpgradeAutomationSchemaV6(context, logger);
                DatabaseBootstrap.UpgradeAutomationSchemaV7(context, logger); DatabaseBootstrap.UpgradeAutomationSchemaV8(context, logger); DatabaseBootstrap.UpgradeAutomationSchemaV9(context, logger);
        DatabaseBootstrap.UpgradeAutomationSchemaV10(context, logger); DatabaseBootstrap.UpgradeAutomationSchemaV11(context, logger); DatabaseBootstrap.UpgradeGoogleCalendarSchemaV1(context, logger); DatabaseBootstrap.UpgradeOnlineConsultationSchemaV1(context, logger); DatabaseBootstrap.UpgradeClientAddressSchemaV1(context, logger); DatabaseBootstrap.UpgradeDirectorySchemaV1(context, logger); DatabaseBootstrap.UpgradeSpecializationsSchemaV1(context, logger); DatabaseBootstrap.UpgradeConfigurationNamingV1(context, logger); DatabaseBootstrap.UpgradeVideoProviderSchemaV1(context, logger); DatabaseBootstrap.UpgradeVideoQuotaSchemaV1(context, logger); DatabaseBootstrap.UpgradeFoodNutritionSchemaV1(context, logger); DatabaseBootstrap.UpgradeProfessionalRecipeSchemaV1(context, logger); SupportSchemaBootstrap.Initialize(context, logger);
        DatabaseBootstrap.UpgradeDirectorySchemaV2(context, logger);
                PublicDirectoryVerificationSchema.Initialize(context, logger);
                context.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS patient_checkins (id SERIAL PRIMARY KEY, client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE, tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE, week_start DATE NOT NULL, submitted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), weight DOUBLE PRECISION, adherence INTEGER, hunger INTEGER, difficulties TEXT, notes TEXT, CONSTRAINT patient_checkins_client_week_key UNIQUE (client_id, week_start)); CREATE INDEX IF NOT EXISTS idx_patient_checkins_tenant_id ON patient_checkins(tenant_id); CREATE INDEX IF NOT EXISTS idx_patient_checkins_client_id ON patient_checkins(client_id);");
                // Appointment scheduling schema
context.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS nutritionist_availability (
  id SERIAL PRIMARY KEY,
  tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  nutritionist_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  day_of_week INTEGER NOT NULL CHECK (day_of_week BETWEEN 0 AND 6),
  start_time TIME NOT NULL,
  end_time TIME NOT NULL,
  slot_minutes INTEGER NOT NULL DEFAULT 30 CHECK (slot_minutes BETWEEN 15 AND 240),
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  CONSTRAINT nutritionist_availability_time_check CHECK (end_time > start_time)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_nutritionist_availability_slot
  ON nutritionist_availability(tenant_id, nutritionist_id, day_of_week, start_time);

CREATE TABLE IF NOT EXISTS patient_appointments (
  id SERIAL PRIMARY KEY,
  tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
  nutritionist_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  starts_at TIMESTAMPTZ NOT NULL,
  ends_at TIMESTAMPTZ NOT NULL,
  status VARCHAR(30) NOT NULL DEFAULT 'requested',
  patient_notes TEXT,
  professional_notes TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT patient_appointments_time_check CHECK (ends_at > starts_at),
  CONSTRAINT patient_appointments_status_check CHECK (status IN ('requested','confirmed','cancelled','completed','no_show'))
);
CREATE INDEX IF NOT EXISTS idx_patient_appointments_tenant_start
  ON patient_appointments(tenant_id, starts_at);
CREATE INDEX IF NOT EXISTS idx_patient_appointments_client_start
  ON patient_appointments(client_id, starts_at);
CREATE INDEX IF NOT EXISTS idx_patient_appointments_nutritionist_start
  ON patient_appointments(nutritionist_id, starts_at);
");
                context.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS patient_notifications (
  id BIGSERIAL PRIMARY KEY,
  tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
  type VARCHAR(60) NOT NULL,
  title VARCHAR(200) NOT NULL,
  message VARCHAR(1000) NOT NULL,
  action_url VARCHAR(1000),
  idempotency_key VARCHAR(200),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  read_at TIMESTAMPTZ
);
ALTER TABLE patient_notifications
  ADD COLUMN IF NOT EXISTS idempotency_key VARCHAR(200);
CREATE UNIQUE INDEX IF NOT EXISTS uq_patient_notifications_tenant_idempotency
  ON patient_notifications(tenant_id, idempotency_key);
CREATE INDEX IF NOT EXISTS idx_patient_notifications_client_created
  ON patient_notifications(client_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_patient_notifications_tenant_client
  ON patient_notifications(tenant_id, client_id);

CREATE TABLE IF NOT EXISTS patient_push_subscriptions (
  id BIGSERIAL PRIMARY KEY,
  tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
  endpoint VARCHAR(2000) NOT NULL UNIQUE,
  p256dh VARCHAR(500) NOT NULL,
  auth VARCHAR(500) NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_patient_push_subscriptions_client
  ON patient_push_subscriptions(client_id);
CREATE TABLE IF NOT EXISTS patient_push_deliveries (
  id BIGSERIAL PRIMARY KEY,
  tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  notification_id BIGINT NULL REFERENCES patient_notifications(id) ON DELETE CASCADE,
  subscription_id BIGINT NOT NULL REFERENCES patient_push_subscriptions(id) ON DELETE CASCADE,
  delivery_key VARCHAR(255) NOT NULL,
  status VARCHAR(20) NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0,
  last_error TEXT NULL,
  sent_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT uq_patient_push_deliveries_key UNIQUE (tenant_id, delivery_key, subscription_id)
);
CREATE INDEX IF NOT EXISTS idx_patient_push_deliveries_status
  ON patient_push_deliveries(status, updated_at);
");
BillingSchemaBootstrap.Initialize(context, logger);
                var patientTemplateSeeder = scope.ServiceProvider.GetRequiredService<PatientDocumentTemplateSeeder>();
                await patientTemplateSeeder.SeedAllTenantsAsync();
                // Una vez disponible el esquema, recuperamos incidencias que pudieron producirse con PostgreSQL caído.
                await ApplicationAlertService.FlushPendingAsync(context, logger);
                databaseReady = true;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "ERROR CRÍTICO: La aplicación no pudo verificar o inicializar la base de datos.");
                try
                {
                    await ApplicationAlertService.RecordAsync(
                        scope.ServiceProvider.GetRequiredService<angulosodbContext>(),
                        "critical",
                        "database-bootstrap",
                        "La base de datos no pudo inicializarse",
                        "El arranque de la aplicación ha encontrado un error de infraestructura. Revisa la configuración o el esquema de la base de datos.",
                        ex,
                        logger);
                }
                catch (Exception alertException)
                {
                    logger.LogError(alertException, "No se pudo registrar la alerta crítica de bootstrap.");
                }
                throw;
            }
        }
        if (databaseReady)
        {
            using (var scope = app.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                try { var context = scope.ServiceProvider.GetRequiredService<angulosodbContext>(); var bedcaCount = context.foods.Count(f => f.source == "bedca"); if (bedcaCount == 0) { logger.LogInformation("No se han encontrado alimentos BEDCA. Iniciando importación inicial..."); var logServ = scope.ServiceProvider.GetRequiredService<LogServ>(); var bedcaClient = new BEDCAClient(new HttpClient(), logServ, context); var resultado = await bedcaClient.Importador(); var importedCount = context.foods.Count(f => f.source == "bedca"); if (importedCount == 0) throw new InvalidOperationException("La importación BEDCA terminó sin insertar ningún alimento."); logger.LogInformation("Importación inicial BEDCA completada correctamente: {Resultado}", resultado); } else logger.LogInformation("Catálogo BEDCA ya inicializado ({Count} alimentos). Se omite la importación.", bedcaCount); }
                catch (Exception ex)
                {
                    logger.LogError(ex, "La importación inicial BEDCA no se pudo completar. Se reintentará en el siguiente arranque.");
                    await ApplicationAlertService.RecordAsync(
                        scope.ServiceProvider.GetRequiredService<angulosodbContext>(),
                        "warning",
                        "bedca-import",
                        "La importación inicial de alimentos no se completó",
                        "El catálogo de alimentos todavía no está completo. La aplicación reintentará la importación en el siguiente arranque.",
                        ex);
                }
            }
        }
        var liveKitCspSource = string.Empty;
        try
        {
            var liveKitUrl = app.Services.GetRequiredService<ConfigServ>().GetConfigString("VIDEO_LIVEKIT_URL");
            if (Uri.TryCreate(liveKitUrl, UriKind.Absolute, out var liveKitUri) &&
                (liveKitUri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) ||
                 liveKitUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
            {
                var host = liveKitUri.Host;
                var port = liveKitUri.IsDefaultPort ? string.Empty : $":{liveKitUri.Port}";
                liveKitCspSource = $" wss://{host}{port}";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo cargar el endpoint LiveKit para la política CSP.");
        }

        app.UseCors("AllowAngularApp");
        if (!app.Environment.IsDevelopment()) { app.UseHttpsRedirection(); app.Use(async (context, next) => { context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains"; context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin"; context.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(self), geolocation=()"; if (context.Request.Path.StartsWithSegments("/api")) { context.Response.Headers["Cache-Control"] = "no-store"; context.Response.Headers["Pragma"] = "no-cache"; } context.Response.Headers["Content-Security-Policy"] = $"default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'self'; form-action 'self'; script-src 'self' https://accounts.google.com; connect-src 'self' https://accounts.google.com{liveKitCspSource}; img-src 'self' data: blob: https:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' data: https://fonts.gstatic.com; frame-src https://accounts.google.com; upgrade-insecure-requests"; await next(); }); }
        app.UseDefaultFiles(); app.UseStaticFiles(); if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); } app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); if (!app.Environment.IsDevelopment()) app.MapFallbackToFile("/index.html");
        try { app.Run(); } finally { Log.CloseAndFlush(); }
    }
}
