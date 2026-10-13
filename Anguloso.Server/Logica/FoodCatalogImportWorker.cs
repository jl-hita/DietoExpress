using Microsoft.Extensions.Configuration;

namespace Anguloso.Server.Logica;

/// <summary>
/// Amplía gradualmente el catálogo global de alimentos sin cargar de trabajo a las APIs externas.
/// Reutiliza el mismo proceso de normalización y persistencia que las búsquedas manuales,
/// pero solo guarda fichas con macros esenciales completos y plausibles.
/// </summary>
public sealed class FoodCatalogImportWorker : BackgroundService
{
    // Consultas cortas y centradas en alimentos habituales para planificación nutricional,
    // evitando una descarga masiva indiscriminada del catálogo de productos envasados.
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

        // Valores configurables, con límites defensivos para evitar una frecuencia accidentalmente agresiva.
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
            // Parada normal del host: no se registra como error de sincronización.
        }
    }

    private async Task ImportDailyBatchAsync(CancellationToken stoppingToken)
    {
        // El día UTC determina el par de términos. Si el proceso se reinicia, se repite
        // como máximo el lote de ese día; la persistencia por código externo es idempotente.
        var firstIndex = (DateOnly.FromDateTime(DateTime.UtcNow).DayNumber * 2) % SearchTerms.Length;

        using var scope = _scopeFactory.CreateScope();
        var foodService = scope.ServiceProvider.GetRequiredService<OpenFoodFactsService>();

        for (var i = 0; i < 2; i++)
        {
            stoppingToken.ThrowIfCancellationRequested();
            var term = SearchTerms[(firstIndex + i) % SearchTerms.Length];

            try
            {
                var eligibleFoods = await foodService.SearchProductsAsync(
                    term,
                    pais: "spain",
                    lang: "es",
                    onlyDietEligible: true);

                _logger.LogInformation(
                    "Importación incremental de alimentos para '{SearchTerm}': {EligibleCount} fichas aptas encontradas.",
                    term,
                    eligibleFoods.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Un fallo de una categoría no impide procesar la segunda ni las ejecuciones futuras.
                _logger.LogError(ex, "Error importando alimentos para el término '{SearchTerm}'.", term);
            }

            // Respeta una pausa entre consultas a Open Food Facts; USDA queda limitado
            // internamente a cinco intentos de enriquecimiento por búsqueda.
            if (i == 0)
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
