using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Google.Apis.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        string entorno = builder.Environment.ContentRootPath;
        // Obtener la cadena de conexión desde appsettings.json
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        var usdaKey = builder.Configuration["Authentication:USDA:ApiKey"];
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

        // Añadimos CORS
        /*
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAngularApp", policy =>
            {
                policy.WithOrigins(
                    "https://localhost:65290",
                    "https://127.0.0.1:65290",
                    "https://192.168.1.100:65290" // otra IP de prueba
                )
                .AllowAnyHeader()
                .AllowAnyMethod();
            });
        });
        */

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAngularApp", policy =>
            {
                policy.SetIsOriginAllowed(origin =>
                {
                    if (string.IsNullOrEmpty(origin)) return false;

                    // Convertimos a minusculas para no tener problemas
                    origin = origin.ToLower();

                    // Permitimos localhost, 127.0.0.1 y 192.168.*.*
                    return origin.StartsWith("http://localhost")
                        || origin.StartsWith("https://localhost")
                        || origin.StartsWith("http://127.0.0.1")
                        || origin.StartsWith("https://127.0.0.1")
                        || origin.StartsWith("http://192.168.")
                        || origin.StartsWith("https://192.168.");
                })
                .AllowAnyHeader()
                .AllowAnyMethod();
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


        builder.Services.AddHttpClient<OpenFoodFactsService>();

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
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                //IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
                //NameClaimType = "unique_name"
                NameClaimType = ClaimTypes.Name, // en lugar de "unique_name"
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
            options.Limits.MaxRequestBodySize = null; // sin límite
        });

        var app = builder.Build();

        // Database schema updates for biometrics and anamnesis
        using (var scope = app.Services.CreateScope())
        {
            try
            {
                var context = scope.ServiceProvider.GetRequiredService<angulosodbContext>();
                var nLineas = context.Database.ExecuteSqlRaw(@"
                    ALTER TABLE biometrics ADD COLUMN IF NOT EXISTS biceps double precision;
                    ALTER TABLE biometrics ADD COLUMN IF NOT EXISTS chest double precision;
                    ALTER TABLE biometrics ADD COLUMN IF NOT EXISTS axilla double precision;
                    ALTER TABLE biometrics ADD COLUMN IF NOT EXISTS calf_skinfold double precision;
                    ALTER TABLE biometrics ADD COLUMN IF NOT EXISTS arm_perimeter double precision;
                    ALTER TABLE biometrics ADD COLUMN IF NOT EXISTS calf_perimeter double precision;
                    ALTER TABLE biometrics ADD COLUMN IF NOT EXISTS wrist_diameter double precision;
                    ALTER TABLE biometrics ADD COLUMN IF NOT EXISTS femur_diameter double precision;
                    ALTER TABLE biometrics ADD COLUMN IF NOT EXISTS humerus_diameter double precision;

                    CREATE TABLE IF NOT EXISTS medical_history (
                        id SERIAL PRIMARY KEY,
                        client_id INTEGER NOT NULL UNIQUE REFERENCES clients(id) ON DELETE CASCADE,
                        diabetes BOOLEAN DEFAULT FALSE,
                        hypertension BOOLEAN DEFAULT FALSE,
                        hypothyroidism BOOLEAN DEFAULT FALSE,
                        surgeries TEXT,
                        routine_medication TEXT,
                        other_pathologies TEXT
                    );

                    CREATE TABLE IF NOT EXISTS digestive_health (
                        id SERIAL PRIMARY KEY,
                        client_id INTEGER NOT NULL UNIQUE REFERENCES clients(id) ON DELETE CASCADE,
                        intestinal_habits TEXT,
                        bloating BOOLEAN DEFAULT FALSE,
                        heartburn BOOLEAN DEFAULT FALSE,
                        gluten_intolerance BOOLEAN DEFAULT FALSE,
                        lactose_intolerance BOOLEAN DEFAULT FALSE,
                        fodmaps_intolerance BOOLEAN DEFAULT FALSE,
                        other_intolerances TEXT,
                        notes TEXT
                    );

                    CREATE TABLE IF NOT EXISTS food_preferences (
                        id SERIAL PRIMARY KEY,
                        client_id INTEGER NOT NULL UNIQUE REFERENCES clients(id) ON DELETE CASCADE,
                        preferred_foods TEXT,
                        disliked_foods TEXT,
                        allergies TEXT
                    );

                    CREATE TABLE IF NOT EXISTS lifestyle_history (
                        id SERIAL PRIMARY KEY,
                        client_id INTEGER NOT NULL UNIQUE REFERENCES clients(id) ON DELETE CASCADE,
                        work_schedule TEXT,
                        sleep_habits TEXT,
                        water_consumption TEXT,
                        alcohol_consumption TEXT,
                        tobacco_consumption TEXT
                    );
                ");

                Console.WriteLine($"Cambiadas {nLineas} rows");
            }
            catch (Exception ex)
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "Error al ejecutar la migración de base de datos para biometría y anamnesis.");
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
