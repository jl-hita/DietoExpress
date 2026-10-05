using System.Text.Json;

namespace Anguloso.Server.Logica;

/// <summary>
/// Adapta el proveedor externo de direcciones al modelo estable que consume DietoExpress.
/// Mantener esta frontera permite sustituir Geoapify sin propagar su contrato por la aplicación.
/// </summary>
public sealed class AddressAutocompleteService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AddressAutocompleteService> _logger;

    public AddressAutocompleteService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AddressAutocompleteService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AddressSuggestion>> SearchAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var apiKey = _configuration["Geoapify:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Geoapify no está configurado: falta Geoapify:ApiKey.");

        var encodedText = Uri.EscapeDataString(text.Trim());
        var url =
            $"https://api.geoapify.com/v1/geocode/autocomplete?text={encodedText}&lang=es&limit=5&filter=countrycode:es&apiKey={Uri.EscapeDataString(apiKey)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Geoapify devolvió HTTP {StatusCode} para una búsqueda de dirección.",
                (int)response.StatusCode);
            throw new HttpRequestException($"El proveedor de direcciones devolvió HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var suggestions = new List<AddressSuggestion>();
        if (!document.RootElement.TryGetProperty("features", out var features) ||
            features.ValueKind != JsonValueKind.Array)
            return suggestions;

        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var properties))
                continue;

            var street = GetString(properties, "street");
            var houseNumber = GetString(properties, "housenumber");
            var city = FirstNonEmpty(
                GetString(properties, "city"),
                GetString(properties, "municipality"),
                GetString(properties, "town"),
                GetString(properties, "village"));
            var province = FirstNonEmpty(
                GetString(properties, "state"),
                GetString(properties, "county"));
            var country = GetString(properties, "country");
            var countryCode = GetString(properties, "country_code");
            var displayName = GetString(properties, "formatted");

            double? latitude = null;
            double? longitude = null;
            if (feature.TryGetProperty("geometry", out var geometry) &&
                geometry.TryGetProperty("coordinates", out var coordinates) &&
                coordinates.ValueKind == JsonValueKind.Array &&
                coordinates.GetArrayLength() >= 2)
            {
                longitude = GetDouble(coordinates[0]);
                latitude = GetDouble(coordinates[1]);
            }

            suggestions.Add(new AddressSuggestion(
                displayName,
                street,
                houseNumber,
                GetString(properties, "postcode"),
                city,
                province,
                country,
                countryCode,
                latitude,
                longitude));
        }

        return suggestions;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static double? GetDouble(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

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
