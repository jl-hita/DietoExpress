namespace Anguloso.Server.Logica;

// Servicio de fachada para mantener un punto común de logging en la lógica de aplicación.
// Este servicio concentra el registro operativo para mantener un formato coherente y evitar que la lógica de negocio dependa directamente del proveedor de logs.
public class LogServ
{
    private ILogger<LogServ> _logger;
    public LogServ(ILogger<LogServ> logger)
    {
        _logger = logger;
    }

    public void LogInfo(string log)
    {
        _logger.LogInformation(log);
    }
    public void LogError(string log)
    {
        _logger.LogError(log);
    }
    public void LogWarning(string log)
    {
        _logger.LogWarning(log);
    }
}
