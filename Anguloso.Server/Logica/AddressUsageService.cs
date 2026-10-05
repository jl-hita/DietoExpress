using Npgsql;

namespace Anguloso.Server.Logica;

/// <summary>
/// Reserva cupo de forma atómica antes de llamar a un proveedor externo.
/// El contador es diario para reflejar los límites gratuitos publicados por los proveedores.
/// </summary>
public sealed class AddressUsageService
{
    private const string Operation = "address-autocomplete";
    private readonly string _connectionString;
    private readonly AddressProviderOptions _options;
    private readonly ILogger<AddressUsageService> _logger;

    public AddressUsageService(
        IConfiguration configuration,
        ILogger<AddressUsageService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No existe ConnectionStrings:DefaultConnection.");
        _options = configuration.GetSection("AddressProviders").Get<AddressProviderOptions>()
            ?? new AddressProviderOptions();
        _logger = logger;
    }

    public async Task<bool> TryReserveAsync(
        string provider,
        int dailyLimit,
        CancellationToken cancellationToken)
    {
        if (dailyLimit <= 0)
            return false;

        var threshold = Math.Clamp(_options.FailoverThreshold, 0.01, 1.0);
        var cap = Math.Max(1, (int)Math.Floor(dailyLimit * threshold));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO external_api_usage
                (usage_date, provider, operation, request_count, updated_at)
            VALUES (CURRENT_DATE, @provider, @operation, 1, NOW())
            ON CONFLICT (usage_date, provider, operation)
            DO UPDATE SET
                request_count = external_api_usage.request_count + 1,
                updated_at = NOW()
            WHERE external_api_usage.request_count < @cap
            RETURNING request_count;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("operation", Operation);
        command.Parameters.AddWithValue("cap", cap);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result == null || result == DBNull.Value)
        {
            _logger.LogWarning(
                "El proveedor {Provider} ha alcanzado el umbral de failover del {Threshold:P0} ({Cap}/{Limit}) para {Operation}.",
                provider,
                threshold,
                cap,
                dailyLimit,
                Operation);
            return false;
        }

        var count = Convert.ToInt32(result);
        var warningCap = Math.Max(1, (int)Math.Floor(dailyLimit * Math.Clamp(_options.WarningThreshold, 0.01, 1.0)));
        if (count >= warningCap)
        {
            _logger.LogWarning(
                "El consumo de {Provider} está en {Count}/{Limit} solicitudes diarias para {Operation}.",
                provider,
                count,
                dailyLimit,
                Operation);
        }

        return true;
    }
}