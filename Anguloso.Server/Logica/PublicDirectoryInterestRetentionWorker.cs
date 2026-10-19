namespace Anguloso.Server.Logica;

/// <summary>
/// Elimina solicitudes de interés y sus datos personales una vez cumplido el plazo de conservación de 90 días.
/// </summary>
public sealed class PublicDirectoryInterestRetentionWorker : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PublicDirectoryInterestRetentionWorker> _logger;

    public PublicDirectoryInterestRetentionWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PublicDirectoryInterestRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ejecutar al arrancar para que las solicitudes vencidas no permanezcan un día extra tras un reinicio.
        await DeleteExpiredRequestsAsync(stoppingToken);

        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await DeleteExpiredRequestsAsync(stoppingToken);
    }

    private async Task DeleteExpiredRequestsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<angulosodbContext>();
            var cutoff = DateTime.UtcNow.Subtract(RetentionPeriod);
            var deleted = await db.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM public_directory_interests
                WHERE created_at < {cutoff}", stoppingToken);

            if (deleted > 0)
                _logger.LogInformation(
                    "Se eliminaron {Count} solicitudes de interés de directorio con más de 90 días.",
                    deleted);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cierre normal del host.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudieron eliminar las solicitudes de interés vencidas.");
        }
    }
}
