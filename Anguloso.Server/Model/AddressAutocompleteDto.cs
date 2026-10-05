using System.Text.Json.Serialization;

namespace Anguloso.Server.Model;

public sealed class AddressAutocompleteResponseDto
{
    [JsonPropertyName("suggestions")]
    public List<AddressSuggestionDto> Suggestions { get; set; } = new();
}

public sealed class AddressSuggestionDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string HouseNumber { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
