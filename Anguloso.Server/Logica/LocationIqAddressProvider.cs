using System.Text.Json;

namespace Anguloso.Server.Logica;

/// <summary>
/// Adaptador de LocationIQ para utilizarlo como proveedor de respaldo.
/// </summary>
public sealed class LocationIqAddressProvider : IAddressProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AddressProviderOptions _options;

    public LocationIqAddressProvider(
        IHttpClientFactory httpClientFactory,
        AddressProviderOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public string Name => "LocationIQ";
    public int DailyLimit => _options.LocationIqDailyLimit;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.LocationIqApiKey) && !_options.LocationIqApiKey.StartsWith("__CONFIGURE_", StringComparison.Ordinal);

    public async Task<IReadOnlyList<AddressAutocompleteService.AddressSuggestion>> SearchAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.LocationIqApiKey;
        if (!IsConfigured)
            throw new InvalidOperationException("LocationIQ no está configurado.");

        var url =
            $"https://api.locationiq.com/v1/autocomplete?key={Uri.EscapeDataString(apiKey!)}&q={Uri.EscapeDataString(text.Trim())}&format=json&limit=5&countrycodes=es&accept-language=es&normalizecity=1&dedupe=1";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"LocationIQ devolvió HTTP {(int)response.StatusCode}.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var suggestions = new List<AddressAutocompleteService.AddressSuggestion>();

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return suggestions;

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var address = item.TryGetProperty("address", out var addressElement)
                ? addressElement
                : default;

            suggestions.Add(new AddressAutocompleteService.AddressSuggestion(
                GetString(item, "display_name"),
                FirstNonEmpty(GetString(address, "road"), GetString(address, "street")),
                GetString(address, "house_number"),
                GetString(address, "postcode"),
                FirstNonEmpty(
                    GetString(address, "city"),
                    GetString(address, "town"),
                    GetString(address, "village"),
                    GetString(address, "municipality")),
                FirstNonEmpty(GetString(address, "state"), GetString(address, "county")),
                GetString(address, "country"),
                GetString(address, "country_code"),
                ParseDouble(item, "lat"),
                ParseDouble(item, "lon")));
        }

        return suggestions;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static double? ParseDouble(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}