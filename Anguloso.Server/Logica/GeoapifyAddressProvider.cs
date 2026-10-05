using System.Text.Json;

namespace Anguloso.Server.Logica;

/// <summary>
/// Adaptador de Geoapify. Solo esta clase conoce el contrato JSON del proveedor.
/// </summary>
public sealed class GeoapifyAddressProvider : IAddressProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AddressProviderOptions _options;

    public GeoapifyAddressProvider(
        IHttpClientFactory httpClientFactory,
        AddressProviderOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public string Name => "Geoapify";
    public int DailyLimit => _options.GeoapifyDailyLimit;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.GeoapifyApiKey) && !_options.GeoapifyApiKey.StartsWith("__CONFIGURE_", StringComparison.Ordinal);

    public async Task<IReadOnlyList<AddressAutocompleteService.AddressSuggestion>> SearchAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.GeoapifyApiKey;
        if (!IsConfigured)
            throw new InvalidOperationException("Geoapify no está configurado.");

        var url =
            $"https://api.geoapify.com/v1/geocode/autocomplete?text={Uri.EscapeDataString(text.Trim())}&lang=es&limit=5&filter=countrycode:es&apiKey={Uri.EscapeDataString(apiKey!)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Geoapify devolvió HTTP {(int)response.StatusCode}.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var suggestions = new List<AddressAutocompleteService.AddressSuggestion>();

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

            suggestions.Add(new AddressAutocompleteService.AddressSuggestion(
                GetString(properties, "formatted"),
                street,
                houseNumber,
                GetString(properties, "postcode"),
                city,
                province,
                GetString(properties, "country"),
                GetString(properties, "country_code"),
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
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}