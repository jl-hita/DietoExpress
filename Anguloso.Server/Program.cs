using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Google.Apis.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        builder.Services.AddSingleton<LogServ>();
        builder.Services.AddSingleton<ConfigServ>(sp => new ConfigServ(connectionString!, sp.GetRequiredService<LogServ>()));
        builder.Services.AddSingleton<EmailServ>(sp => new EmailServ(sp.GetRequiredService<ConfigServ>(), sp.GetRequiredService<LogServ>()));
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<RecipeNutritionService>();
        builder.Services.AddScoped<SupportEnhancementService>();
        builder.Services.AddScoped<SupportService>();
        builder.Services.AddSingleton<MaintenanceNoticeService>();
        builder.Services.AddSingleton<DatabaseBackupService>();
        builder.Services.AddScoped<CommercialCommunicationService>();
        // El servicio comparte la lógica de publicación entre peticiones y el worker; el worker separado procesa los jobs sin bloquear las peticiones HTTP.
        builder.Services.AddSingleton<AutomationService>();
        builder.Services.AddScoped<PatientPortalAccessService>(); builder.Services.AddScoped<PatientDocumentService>(); builder.Services.AddSingleton<PatientDocumentTemplateSeeder>(); builder.Services.AddScoped<PrivacyOperationsService>(); builder.Services.AddScoped<LegalGovernanceService>();
        builder.Services.AddDataProtection();
        builder.Services.AddHttpClient();

        // La tabla config de PostgreSQL es la fuente de verdad de los proveedores de direcciones.
        // Esto evita duplicar claves entre appsettings, variables de entorno y la base de datos.
        builder.Services.AddSingleton<AddressProviderOptions>(sp => AddressProviderOptions.Load(sp.GetRequiredService<ConfigServ>()));
        builder.Services.AddSingleton<AddressUsageService>();
        builder.Services.AddSingleton<IAddressProvider, GeoapifyAddressProvider>();
        builder.Services.AddSingleton<IAddressProvider, LocationIqAddressProvider>();
        builder.Services.AddScoped<GoogleCalendarService>();
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
                    var archivedSupport = context.Principal?.FindFirstValue("archivedSupport") == "true";
                    if (user == null) { context.Fail("Cuenta no disponible."); return; }
                    if (user.archived_at != null && (!archivedSupport || !context.HttpContext.Request.Path.StartsWithSegments("/api/support"))) { context.Fail("Cuenta no disponible."); return; }
                    if (user.token_version != tokenVersion) { context.Fail("Sesión revocada."); return; }
                    if (context.Principal?.Identity is ClaimsIdentity identity) { foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList()) identity.RemoveClaim(claim); identity.AddClaim(new Claim(ClaimTypes.Role, user.role ?? "nutritionist")); foreach (var claim in identity.FindAll("subscriptionPlan").ToList()) identity.RemoveClaim(claim); identity.AddClaim(new Claim("subscriptionPlan", user.subscription_plan ?? "free")); foreach (var claim in identity.FindAll("subscriptionStatus").ToList()) identity.RemoveClaim(claim); identity.AddClaim(new Claim("subscriptionStatus", user.subscription_status ?? "active")); }
                }
            };
        });
        QuestPDF.Settings.License = LicenseType.Community;
        builder.Services.AddSingleton<AddressAutocompleteService>(); builder.Services.AddSingleton<DietPdfService>(); builder.Services.AddSingleton<EnergyCalculatorService>(); builder.Services.AddSingleton<AnthropometryCalculatorService>(); builder.Services.AddScoped<DietValidationService>(); builder.Services.AddScoped<DietSemanticValidationService>(); builder.Services.AddScoped<SpecializationRulesService>(); builder.Services.AddScoped<FoodSubstitutionService>(); builder.Services.AddSingleton<BioimpedanceParserService>(); builder.Services.AddScoped<DietGeneratorService>(); builder.Services.AddScoped<DietRegenerationService>();
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
                await scope.ServiceProvider.GetRequiredService<DatabaseBootstrap>().InitializeAsync();
                databaseReady = true;
                logger.LogInformation("Database bootstrap completado.");
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Database bootstrap falló; la aplicación continuará arrancando para exponer diagnóstico.");
            }
        }
        app.UseCors("AllowAngularApp");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", databaseReady }));
        app.Run();
    }
}
