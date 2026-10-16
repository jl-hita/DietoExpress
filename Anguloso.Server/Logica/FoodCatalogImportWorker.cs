using Microsoft.Extensions.Configuration;

namespace Anguloso.Server.Logica;

/// <summary>
/// Amplía gradualmente el catálogo global de alimentos sin cargar de trabajo a las APIs externas.
/// Registra los alimentos insertados/actualizados y los errores con el máximo detalle útil.
/// </summary>
public sealed class FoodCatalogImportWorker : BackgroundService
{
    private static readonly string[] SearchTerms =
    {
        "arroz integral", "lentejas", "garbanzos", "avena", "pollo",
        "pavo", "merluza", "salmón", "huevo", "yogur natural",
        "leche", "manzana", "plátano", "patata", "tomate",
        "espinacas", "almendras", "pan integral", "pasta integral", "aceite de oliva"
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FoodCatalogImportWorker> _logger;
    private readonly IConfiguration _configuration;
    // Los términos que fallan se vuelven a priorizar en el siguiente lote.
    // Si el proceso se reinicia, el calendario rotatorio sigue recorriendo todos los términos.
    private readonly Queue<string> _pendingRetryTerms = new();

    public FoodCatalogImportWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<FoodCatalogImportWorker> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("FoodCatalogImport:Enabled", true))
        {
            _logger.LogInformation("La ampliación programada del catálogo de alimentos está desactivada por configuración.");
            return;
        }

        var intervalHours = Math.Clamp(_configuration.GetValue<int?>("FoodCatalogImport:IntervalHours") ?? 24, 6, 168);
        var startupDelayMinutes = Math.Clamp(_configuration.GetValue<int?>("FoodCatalogImport:StartupDelayMinutes") ?? 2, 0, 60);

        _logger.LogInformation(
            "Worker de catálogo de alimentos iniciado. Intervalo: {IntervalHours} h; 2 consultas por ejecución.",
            intervalHours);

        try
        {
            if (startupDelayMinutes > 0)
                await Task.Delay(TimeSpan.FromMinutes(startupDelayMinutes), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await ImportDailyBatchAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker de catálogo de alimentos detenido.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "El worker de catálogo de alimentos terminó inesperadamente.");
            throw;
        }
    }

    private async Task ImportDailyBatchAsync(CancellationToken stoppingToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var firstIndex = (DateOnly.FromDateTime(DateTime.UtcNow).DayNumber * 2) % SearchTerms.Length;
        var successfulTerms = 0;
        var failedTerms = 0;
        var batchTerms = new List<string>(capacity: 2);

        // Reintentar primero los términos fallidos antes de avanzar el catálogo.
        while (_pendingRetryTerms.Count > 0 && batchTerms.Count < 2)
            batchTerms.Add(_pendingRetryTerms.Dequeue());

        for (var offset = 0; batchTerms.Count < 2 && offset < SearchTerms.Length; offset++)
        {
            var candidate = SearchTerms[(firstIndex + offset) % SearchTerms.Length];
            if (!batchTerms.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                batchTerms.Add(candidate);
        }

        using var scope = _scopeFactory.CreateScope();
        var foodService = scope.ServiceProvider.GetRequiredService<OpenFoodFactsService>();

        _logger.LogInformation(
            "Inicio de lote de importación de alimentos. Términos priorizados: '{FirstTerm}' y '{SecondTerm}'. Pendientes al inicio: {PendingCount}.",
            batchTerms[0],
            batchTerms[1],
            _pendingRetryTerms.Count);

        for (var i = 0; i < batchTerms.Count; i++)
        {
            stoppingToken.ThrowIfCancellationRequested();
            var term = batchTerms[i];

            try
            {
                var eligibleFoods = await foodService.SearchProductsAsync(
                    term,
                    pais: "spain",
                    lang: "es",
                    onlyDietEligible: true,
                    logImportDetails: true,
                    cancellationToken: stoppingToken);

                successfulTerms++;
                _logger.LogInformation(
                    "Consulta de importación terminada para '{SearchTerm}'. Productos aptos procesados/devueltos: {EligibleCount}. Los INSERT/UPDATE y errores de persistencia se registran individualmente.",
                    term,
                    eligibleFoods.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedTerms++;
                if (!_pendingRetryTerms.Contains(term, StringComparer.OrdinalIgnoreCase))
                    _pendingRetryTerms.Enqueue(term);
                _logger.LogError(ex, "Fallo completo al importar el término '{SearchTerm}'. Se priorizará en el siguiente lote. Pendientes: {PendingCount}. Tipo: {ExceptionType}; detalle: {ExceptionMessage}",
                    term, _pendingRetryTerms.Count, ex.GetType().FullName, ex.Message);
            }

            if (i == 0)
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }

        _logger.LogInformation(
            "Lote de importación finalizado en {ElapsedSeconds:F1} s. Consultas correctas: {SuccessfulTerms}; consultas con error no controlado: {FailedTerms}.",
            (DateTimeOffset.UtcNow - startedAt).TotalSeconds,
            successfulTerms,
            failedTerms);
    }
}
