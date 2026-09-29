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
        // Obtener la cadena de conexión desde appsettings.json
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        //Console.WriteLine($"API key USDA: {usdaKey}");

        // Registrar el DbContext
        builder.Services.AddDbContext<angulosodbContext>(options => options.UseNpgsql(connectionString));

        //Carpeta de logs
        string pathLogs = Path.Combine(builder.Environment.ContentRootPath, "Logs");
        Directory.CreateDirectory(pathLogs);

        // Add support to logging with SERILOG
        builder.Host.UseSerilog((context, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(pathLogs, "log-.txt"), rollingInterval: RollingInterval.Day, shared: true);
        });

        // CORS: permitir únicamente los orígenes conocidos.
        // En desarrollo se mantiene el frontend Angular local (puerto 4200).
        // En producción los orígenes deben declararse explícitamente mediante
        // Cors:AllowedOrigins en configuración/variables de entorno.
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAngularApp", policy =>
            {
                var allowedOrigins = builder.Environment.IsDevelopment()
                    ? new[]
                    {
                        "http://localhost:4200",
                        "https://localhost:4200",
                        "http://127.0.0.1:4200",
                        "https://127.0.0.1:4200"
                    }
                    : builder.Configuration
                        .GetSection("Cors:AllowedOrigins")
                        .Get<string[]>() ?? Array.Empty<string>();

                policy
                    .WithOrigins(allowedOrigins)
                    .WithHeaders("Authorization", "Content-Type", "Accept")
                    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE");
            });
        });


        // Add services to the container.

        //Serilog
        /*
        builder.Services.AddSingleton<LogServ>(sp =>
        {
            ILogger<LogServ> logger = sp.GetService<ILogger<LogServ>>();
            return new LogServ(logger);
        });
        */
        builder.Services.AddSingleton<LogServ>();

        builder.Services.AddSingleton<ConfigServ>(sp =>
        {
            var logServ = sp.GetRequiredService<LogServ>();
            return new ConfigServ(connectionString!, logServ);
        });

        builder.Services.AddSingleton<EmailServ>(sp =>
        {
            var configServ = sp.GetRequiredService<ConfigServ>();
            var logServ = sp.GetRequiredService<LogServ>();

            return new EmailServ(configServ, logServ);
        });


        builder.Services.AddHttpClient<IStripeBillingService, StripeBillingService>();

        builder.Services.AddHttpClient<OpenFoodFactsService>().AddTypedClient((httpClient, sp) =>
        {
            //var usdaKey = config["UsdaApiKey"];
            var logServ = sp.GetRequiredService<LogServ>();
            //return new OpenFoodFactsService(httpClient, connectionString!, usdaKey!, logServ);
            var configServ = sp.GetRequiredService<ConfigServ>();
            return new OpenFoodFactsService(httpClient, connectionString!, logServ, configServ);
        });

        //Autenticación
        var jwtKey = builder.Configuration["Jwt:Key"];
        var keyBytes = Encoding.UTF8.GetBytes(jwtKey!);

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                //IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
                //NameClaimType = "unique_name"
                NameClaimType = ClaimTypes.Name, // en lugar de "unique_name"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    if (context.Principal?.IsInRole("patient") == true) return;

                    var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (!int.TryParse(userIdClaim, out var userId))
                    {
                        context.Fail("Identidad de usuario no válida.");
                        return;
                    }

                    var tokenVersionClaim = context.Principal?.FindFirstValue("tokenVersion");
                    if (!int.TryParse(tokenVersionClaim, out var tokenVersion))
                    {
                        context.Fail("Token sin versión de seguridad.");
                        return;
                    }

                    var db = context.HttpContext.RequestServices.GetRequiredService<angulosodbContext>();
                    var user = await db.users.AsNoTracking()
                        .Where(u => u.id == userId)
                        .Select(u => new { u.archived_at, u.token_version, u.role, u.subscription_plan, u.subscription_status })
                        .FirstOrDefaultAsync();

                    if (user == null || user.archived_at != null)
                    {
                        context.Fail("Cuenta no disponible.");
                        return;
                    }

                    if (user.token_version != tokenVersion)
                    {
                        context.Fail("Sesión revocada.");
                        return;
                    }

                    // Los cambios de rol/licencia deben reflejarse inmediatamente aunque
                    // el JWT anterior siga dentro de sus 3 horas de vida.
                    if (context.Principal?.Identity is ClaimsIdentity identity)
                    {
                        foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList())
                            identity.RemoveClaim(claim);

                        identity.AddClaim(new Claim(ClaimTypes.Role, user.role ?? "user"));

                        foreach (var claim in identity.FindAll("subscriptionPlan").ToList())
                            identity.RemoveClaim(claim);
                        identity.AddClaim(new Claim("subscriptionPlan", user.subscription_plan ?? "free"));

                        foreach (var claim in identity.FindAll("subscriptionStatus").ToList())
                            identity.RemoveClaim(claim);
                        identity.AddClaim(new Claim("subscriptionStatus", user.subscription_status ?? "active"));
                    }
                }
            };
        });

        // Configuración Email
        //builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
        //builder.Services.AddScoped<IEmailService, EmailServ>();

        // Licencia comunitaria gratuita de QuestPDF
        QuestPDF.Settings.License = LicenseType.Community;
        builder.Services.AddSingleton<DietPdfService>();
        builder.Services.AddSingleton<EnergyCalculatorService>();
        builder.Services.AddSingleton<AnthropometryCalculatorService>();
        builder.Services.AddSingleton<DietValidationService>();
        builder.Services.AddSingleton<BioimpedanceParserService>();
        builder.Services.AddScoped<DietGeneratorService>();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantContextService, TenantContextService>();
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();
        builder.Services.AddScoped<ILicenseService, LicenseService>();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("auth", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("expensive", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"{httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}:expensive",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Professional", policy =>
                policy.RequireAuthenticatedUser()
                      .RequireAssertion(ctx => !ctx.User.IsInRole("patient")));
        });

        builder.Services.AddControllers();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        //builder.Services.AddSwaggerGen();
        builder.Services.AddSwaggerGen(options =>
        {
            var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50 MB
        });

        var app = builder.Build();

        // Verificación de conexión a BBDD y creación completa de tablas.
        // La importación BEDCA solo se ejecuta si toda la inicialización del esquema
        // ha terminado correctamente.
        bool databaseReady = false;
        using (var scope = app.Services.CreateScope())
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            try
            {
                var context = scope.ServiceProvider.GetRequiredService<angulosodbContext>();
                DatabaseBootstrap.InitializeDatabaseAsync(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchema(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchemaV2(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchemaV3(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchemaV4(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchemaV5(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchemaV6(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchemaV7(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchemaV8(context, logger);
                DatabaseBootstrap.UpgradeSaaSSchemaV9(context, logger);
                BillingSchemaBootstrap.Initialize(context, logger);
                databaseReady = true;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "ERROR CRÍTICO: La aplicación no pudo verificar o inicializar la base de datos.");
            }
        }

        // Carga inicial del catálogo BEDCA. Solo se ejecuta una vez, cuando todavía
        // no existen alimentos procedentes de BEDCA. Si la importación falla, no se
        // considera completada y se volverá a intentar en el siguiente arranque.
        if (databaseReady)
        {
            using (var scope = app.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                try
                {
                    var context = scope.ServiceProvider.GetRequiredService<angulosodbContext>();
                    var bedcaCount = context.foods.Count(f => f.source == "bedca");

                    if (bedcaCount == 0)
                    {
                        logger.LogInformation("No se han encontrado alimentos BEDCA. Iniciando importación inicial...");
                        var logServ = scope.ServiceProvider.GetRequiredService<LogServ>();
                        var bedcaClient = new BEDCAClient(new HttpClient(), logServ, context);
                        var resultado = await bedcaClient.Importador();

                        var importedCount = context.foods.Count(f => f.source == "bedca");
                        if (importedCount == 0)
                        {
                            throw new InvalidOperationException("La importación BEDCA terminó sin insertar ningún alimento.");
                        }

                        logger.LogInformation("Importación inicial BEDCA completada correctamente: {Resultado}", resultado);
                    }
                    else
                    {
                        logger.LogInformation("Catálogo BEDCA ya inicializado ({Count} alimentos). Se omite la importación.", bedcaCount);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "La importación inicial BEDCA no se pudo completar. Se reintentará en el siguiente arranque.");
                }
            }
        }

        // Usamos CORS
        app.UseCors("AllowAngularApp");

        //Comentado desarrollo, se debe descomentar para producción
        //app.UseDefaultFiles();
        //app.UseStaticFiles();
        app.UseHttpsRedirection();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();


        app.MapControllers();

        //Comentado desarrollo, se debe descomentar para producción
        //app.MapFallbackToFile("/index.html");

        try
        {
            app.Run();
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
