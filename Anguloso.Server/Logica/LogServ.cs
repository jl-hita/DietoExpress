namespace Anguloso.Server.Logica;

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
