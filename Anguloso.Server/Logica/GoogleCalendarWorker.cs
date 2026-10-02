namespace Anguloso.Server.Logica;

public sealed class GoogleCalendarWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GoogleCalendarWorker> _logger;

    public GoogleCalendarWorker(IServiceScopeFactory scopeFactory, ILogger<GoogleCalendarWorker> logger) { _scopeFactory = scopeFactory; _logger = logger; }

    // El worker crea un scope por ciclo para resolver dependencias con ciclo de vida acotado.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Un fallo de una sincronización se registra y no impide los ciclos posteriores.
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<GoogleCalendarService>().SyncAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error en el worker de sincronización de Google Calendar."); }
        }
    }
}
