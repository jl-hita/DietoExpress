using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Anguloso.Server.Logica;

public class ConfigServ
{
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

            config? configuracion = dbContext.config.AsNoTracking().Where(c => c.nombre_config == nombre).FirstOrDefault();

            //Si no se encuentra se devuelve defecto
            if (configuracion == null)
            {
                //Si defecto no es null aprovechamos para guardarlo en la BBDD
                if(defecto != null)
                {
                    config nuevaConfig = new config
                    {
                        nombre_config = nombre,
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

            config? configuracion = dbContext.config.AsNoTracking().Where(c => c.nombre_config == nombre).FirstOrDefault();

            //Si no se encuentra se devuelve defecto
            if (configuracion == null)
            {
                //Si defecto no es null aprovechamos para guardarlo en la BBDD
                if (defecto != null)
                {
                    config nuevaConfig = new config
                    {
                        nombre_config = nombre,
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

            config? configuracion = dbContext.config.AsNoTracking().Where(c => c.nombre_config == nombre).FirstOrDefault();

            //Si no se encuentra se devuelve defecto
            if (configuracion == null)
            {
                //Si defecto no es null aprovechamos para guardarlo en la BBDD
                if (defecto != null)
                {
                    config nuevaConfig = new config
                    {
                        nombre_config = nombre,
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
