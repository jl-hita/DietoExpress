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

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        string entorno = builder.Environment.ContentRootPath;
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        builder.Services.AddDbContext<angulosodbContext>(options => options.UseNpgsql(connectionString));
        string pathLogs = Path.Combine(builder.Environment.ContentRootPath, "Logs");
        Directory.CreateDirectory(pathLogs);
        builder.Host.UseSerilog((context, loggerConfiguration) => { loggerConfiguration.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext().WriteTo.Console().WriteTo.File(Path.Combine(pathLogs, "log-.txt"), rollingInterval: RollingInterval.Day, shared: true); });
        builder.Services.AddCors(options => { options.AddPolicy("AllowAngularApp", policy => { var allowedOrigins = builder.Environment.IsDevelopment() ? new[] { "http://localhost:4200", "https://localhost:4200", "http://127.0.0.1:4200", "https://127.0.0.1:4200" } : builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>(); policy.WithOrigins(allowedOrigins).WithHeaders("Authorization", "Content-Type", "Accept").WithMethods("GET", "POST", "PUT", "PATCH", "DELETE"); }); });
// Add services to the container.
        builder.Services.AddSingleton<LogServ>();
        builder.Services.AddSingleton<ConfigServ>(sp => new ConfigServ(connectionString!, sp.GetRequiredService<LogServ>()));
        builder.Services.AddSingleton<EmailServ>(sp => new EmailServ(sp.GetRequiredService<ConfigServ>(), sp.GetRequiredService<LogServ>()));
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<AutomationService>();
        builder.Services.AddDataProtection();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<GoogleCalendarService>();
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
                    if (context.Principal?.Identity is ClaimsIdentity identity) { foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList()) identity.RemoveClaim(claim); identity.AddClaim(new Claim(ClaimTypes.Role, user.role ?? "user")); foreach (var claim in identity.FindAll("subscriptionPlan").ToList()) identity.RemoveClaim(claim); identity.AddClaim(new Claim("subscriptionPlan", user.subscription_plan ?? "free")); foreach (var claim in identity.FindAll("subscriptionStatus").ToList()) identity.RemoveClaim(claim); identity.AddClaim(new Claim("subscriptionStatus", user.subscription_status ?? "active")); }
                }
            };
        });
        QuestPDF.Settings.License = LicenseType.Community;
        builder.Services.AddSingleton<DietPdfService>(); builder.Services.AddSingleton<EnergyCalculatorService>(); builder.Services.AddSingleton<AnthropometryCalculatorService>(); builder.Services.AddSingleton<DietValidationService>(); builder.Services.AddSingleton<BioimpedanceParserService>(); builder.Services.AddScoped<DietGeneratorService>();
        builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<ITenantContextService, TenantContextService>(); builder.Services.AddScoped<IAuditLogService, AuditLogService>(); builder.Services.AddScoped<ILicenseService, LicenseService>();
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
                DatabaseBootstrap.InitializeDatabaseAsync(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchema(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV2(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV3(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV4(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV5(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV6(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV7(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV8(context, logger); DatabaseBootstrap.UpgradeSaaSSchemaV9(context, logger); DatabaseBootstrap.UpgradeMessagingSchemaV2(context, logger); DatabaseBootstrap.UpgradeAutomationSchemaV1(context, logger);
    DatabaseBootstrap.UpgradeAutomationSchemaV2(context, logger); DatabaseBootstrap.UpgradeAutomationSchemaV3(context, logger); DatabaseBootstrap.UpgradeGoogleCalendarSchemaV1(context, logger);
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
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  read_at TIMESTAMPTZ
);
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
");
BillingSchemaBootstrap.Initialize(context, logger); databaseReady = true;
            }
            catch (Exception ex) { logger.LogCritical(ex, "ERROR CRÍTICO: La aplicación no pudo verificar o inicializar la base de datos."); }
        }
        if (databaseReady)
        {
            using (var scope = app.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                try { var context = scope.ServiceProvider.GetRequiredService<angulosodbContext>(); var bedcaCount = context.foods.Count(f => f.source == "bedca"); if (bedcaCount == 0) { logger.LogInformation("No se han encontrado alimentos BEDCA. Iniciando importación inicial..."); var logServ = scope.ServiceProvider.GetRequiredService<LogServ>(); var bedcaClient = new BEDCAClient(new HttpClient(), logServ, context); var resultado = await bedcaClient.Importador(); var importedCount = context.foods.Count(f => f.source == "bedca"); if (importedCount == 0) throw new InvalidOperationException("La importación BEDCA terminó sin insertar ningún alimento."); logger.LogInformation("Importación inicial BEDCA completada correctamente: {Resultado}", resultado); } else logger.LogInformation("Catálogo BEDCA ya inicializado ({Count} alimentos). Se omite la importación.", bedcaCount); }
                catch (Exception ex) { logger.LogError(ex, "La importación inicial BEDCA no se pudo completar. Se reintentará en el siguiente arranque."); }
            }
        }
        app.UseCors("AllowAngularApp");
        if (!app.Environment.IsDevelopment()) { app.UseHttpsRedirection(); app.Use(async (context, next) => { context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains"; context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin"; context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()"; if (context.Request.Path.StartsWithSegments("/api")) { context.Response.Headers["Cache-Control"] = "no-store"; context.Response.Headers["Pragma"] = "no-cache"; } context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'self'; form-action 'self'; script-src 'self' https://accounts.google.com; connect-src 'self' https://accounts.google.com; img-src 'self' data: blob: https:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' data: https://fonts.gstatic.com; frame-src https://accounts.google.com; upgrade-insecure-requests"; await next(); }); }
        app.UseDefaultFiles(); app.UseStaticFiles(); if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); } app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); if (!app.Environment.IsDevelopment()) app.MapFallbackToFile("/index.html");
        try { app.Run(); } finally { Log.CloseAndFlush(); }
    }
}
