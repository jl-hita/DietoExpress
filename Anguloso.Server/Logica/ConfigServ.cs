using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Anguloso.Server.Logica;

// Documentación: este componente encapsula lógica compartida para mantener las reglas y transformaciones fuera de los puntos de entrada del frontend.
// La configuración se centraliza aquí para evitar que cada consumidor interprete de forma distinta valores opcionales o ausentes.
public class ConfigServ
{
    private static readonly IReadOnlyDictionary<string, string> LegacyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["platformLegalName"] = "PLATFORM_LEGAL_NAME",
        ["platformLegalForm"] = "PLATFORM_LEGAL_FORM",
        ["platformTaxId"] = "PLATFORM_TAX_ID",
        ["platformAddress"] = "PLATFORM_ADDRESS",
        ["platformPostalCode"] = "PLATFORM_POSTAL_CODE",
        ["platformCity"] = "PLATFORM_CITY",
        ["platformProvince"] = "PLATFORM_PROVINCE",
        ["platformCountry"] = "PLATFORM_COUNTRY",
        ["platformContactEmail"] = "PLATFORM_CONTACT_EMAIL",
        ["platformContactPhone"] = "PLATFORM_CONTACT_PHONE",
        ["platformDpoEmail"] = "PLATFORM_DPO_EMAIL",
        ["platformRegistryData"] = "PLATFORM_REGISTRY_DATA",
        ["googleClientId"] = "GOOGLE_CLIENT_ID",
        ["dominio"] = "PLATFORM_DOMAIN",
        ["frontendUrl"] = "PLATFORM_FRONTEND_URL",
        ["smtpServer"] = "EMAIL_SMTP_SERVER",
        ["smtpPort"] = "EMAIL_SMTP_PORT",
        ["smtpEnableSsl"] = "EMAIL_SMTP_ENABLE_SSL",
        ["smtpFromEmail"] = "EMAIL_SMTP_FROM_EMAIL",
        ["smtpFromName"] = "EMAIL_SMTP_FROM_NAME",
        ["smtpUser"] = "EMAIL_SMTP_USERNAME",
        ["smtpPwd"] = "EMAIL_SMTP_PASSWORD",
        ["usdaApiKey"] = "FOOD_USDA_API_KEY",
        ["webPushSubject"] = "NOTIFICATIONS_WEBPUSH_SUBJECT",
        ["webPushPublicKey"] = "NOTIFICATIONS_WEBPUSH_PUBLIC_KEY",
        ["webPushPrivateKey"] = "NOTIFICATIONS_WEBPUSH_PRIVATE_KEY",
        ["geoapifyApiKey"] = "ADDRESS_GEOAPIFY_API_KEY",
        ["locationIqApiKey"] = "ADDRESS_LOCATIONIQ_API_KEY",
        ["addressPrimaryProvider"] = "ADDRESS_PRIMARY_PROVIDER",
        ["addressFallbackProvider"] = "ADDRESS_FALLBACK_PROVIDER",
        ["addressWarningThreshold"] = "ADDRESS_WARNING_THRESHOLD",
        ["addressFailoverThreshold"] = "ADDRESS_FAILOVER_THRESHOLD",
        ["addressGeoapifyDailyLimit"] = "ADDRESS_GEOAPIFY_DAILY_LIMIT",
        ["addressLocationIqDailyLimit"] = "ADDRESS_LOCATIONIQ_DAILY_LIMIT",
    };

    private static string NormalizeName(string nombre) => LegacyAliases.TryGetValue(nombre, out var normalized) ? normalized : nombre;
    private string _connectionString;
    private LogServ _logServ;

    public ConfigServ(string connectionString, LogServ logServ)
    {
        _connectionString = connectionString;
        _logServ = logServ;
    }

    private angulosodbContext CrearDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<angulosodbContext>();
        optionsBuilder.UseNpgsql(_connectionString);
        return new angulosodbContext(optionsBuilder.Options);
    }

    //Recibe el nombre de un parámetro que será int y devuelve el valor en la BBDD o null
    public int? GetConfigInt(string nombre, int? defecto = null)
    {
        try
        {
            using var dbContext = CrearDbContext();

            var normalizedName = NormalizeName(nombre);
            config? configuracion = dbContext.config.AsNoTracking().Where(c => c.nombre_config == normalizedName).FirstOrDefault();

            //Si no se encuentra se devuelve defecto
            if (configuracion == null)
            {
                //Si defecto no es null aprovechamos para guardarlo en la BBDD
                if(defecto != null)
                {
                    config nuevaConfig = new config
                    {
                        nombre_config = normalizedName,
                        valor_config = defecto.ToString()
                    };

                    dbContext.Add(nuevaConfig);
                    dbContext.SaveChanges();
                }

                return defecto;
            }   

            //Si se encuentra, se parsea a int y se devuelve. Si falla se devuelve defecto
            int respuesta = 1;
            if(int.TryParse(configuracion.valor_config, out respuesta))
                return respuesta;
            else
                return defecto;
        }
        catch (Exception ex)
        {
            _logServ.LogError($"Excepción al recuperar la config {nombre} tipo int => {ex.Message}");
            return defecto;
        }
    }

    //Recibe el nombre de un parámetro que será string y devuelve el valor en la BBDD o null
    public string? GetConfigString(string nombre, string? defecto = null)
    {
        try
        {
            using var dbContext = CrearDbContext();

            var normalizedName = NormalizeName(nombre);
            config? configuracion = dbContext.config.AsNoTracking().Where(c => c.nombre_config == normalizedName).FirstOrDefault();

            //Si no se encuentra se devuelve defecto
            if (configuracion == null)
            {
                //Si defecto no es null aprovechamos para guardarlo en la BBDD
                if (defecto != null)
                {
                    config nuevaConfig = new config
                    {
                        nombre_config = normalizedName,
                        valor_config = defecto
                    };

                    dbContext.Add(nuevaConfig);
                    dbContext.SaveChanges();
                }

                return defecto;
            }

            //Si se encuentra se devuelve
            return configuracion.valor_config;
        }
        catch (Exception ex)
        {
            _logServ.LogError($"Excepción al recuperar la config {nombre} tipo string => {ex.Message}");
            return defecto;
        }
    }

    //Recibe el nombre de un parámetro que será boolean y devuelve el valor en la BBDD o null
    public bool? GetConfigBool(string nombre, bool? defecto = null)
    {
        try
        {
            using var dbContext = CrearDbContext();

            var normalizedName = NormalizeName(nombre);
            config? configuracion = dbContext.config.AsNoTracking().Where(c => c.nombre_config == normalizedName).FirstOrDefault();

            //Si no se encuentra se devuelve defecto
            if (configuracion == null)
            {
                //Si defecto no es null aprovechamos para guardarlo en la BBDD
                if (defecto != null)
                {
                    config nuevaConfig = new config
                    {
                        nombre_config = normalizedName,
                        valor_config = (bool)defecto ? "1" : "0"
                    };

                    dbContext.Add(nuevaConfig);
                    dbContext.SaveChanges();
                }

                return defecto;
            }

            //Suponemos "1" == true, "0" == false;
            switch (configuracion.valor_config)
            {
                case "1":
                    return true;
                case "0":
                    return false;
                default:
                    return defecto;
            }
        }
        catch (Exception ex)
        {
            _logServ.LogError($"Excepción al recuperar la config {nombre} tipo boolean => {ex.Message}");
            return defecto;
        }
    }
}
