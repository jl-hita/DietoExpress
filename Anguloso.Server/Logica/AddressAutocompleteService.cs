namespace Anguloso.Server.Logica;

/// <summary>
/// Coordina proveedores de autocompletado y aplica el control de cuota antes de cada llamada externa.
/// </summary>
public sealed class AddressAutocompleteService
{
    private readonly AddressUsageService _usage;
    private readonly IReadOnlyDictionary<string, IAddressProvider> _providers;
    private readonly AddressProviderOptions _options;
    private readonly ILogger<AddressAutocompleteService> _logger;

    public AddressAutocompleteService(
        AddressUsageService usage,
        IEnumerable<IAddressProvider> providers,
        AddressProviderOptions options,
        ILogger<AddressAutocompleteService> logger)
    {
        _usage = usage;
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AddressSuggestion>> SearchAsync(
        string text,
        CancellationToken cancellationToken)
    {
        foreach (var providerName in GetProviderOrder())
        {
            if (!_providers.TryGetValue(providerName, out var provider) || !provider.IsConfigured)
                continue;

            if (!await _usage.TryReserveAsync(provider.Name, provider.DailyLimit, cancellationToken))
                continue;

            try
            {
                return await provider.SearchAsync(text, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "El proveedor {Provider} no está configurado. Se intentará el siguiente proveedor.", provider.Name);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "El proveedor {Provider} falló. Se intentará el siguiente proveedor.", provider.Name);
            }
        }

        throw new InvalidOperationException("No hay ningún proveedor de direcciones disponible.");
    }

    private IEnumerable<string> GetProviderOrder()
    {
        yield return _options.Primary;
        if (!string.Equals(_options.Primary, _options.Fallback, StringComparison.OrdinalIgnoreCase))
            yield return _options.Fallback;
    }

    public sealed record AddressSuggestion(
        string DisplayName,
        string Street,
        string HouseNumber,
        string PostalCode,
        string City,
        string Province,
        string Country,
        string CountryCode,
        double? Latitude,
        double? Longitude);
}