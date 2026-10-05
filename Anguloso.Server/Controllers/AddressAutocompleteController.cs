using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Anguloso.Server.Controllers;

[Route("api/address-autocomplete")]
[ApiController]
[Authorize(Policy = "Professional")]
[EnableRateLimiting("expensive")]
public sealed class AddressAutocompleteController : ControllerBase
{
    private readonly AddressAutocompleteService _service;

    public AddressAutocompleteController(AddressAutocompleteService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<AddressAutocompleteResponseDto>> Search(
        [FromQuery] string? text,
        CancellationToken cancellationToken)
    {
        var query = text?.Trim() ?? string.Empty;
        if (query.Length < 3)
            return Ok(new AddressAutocompleteResponseDto());

        if (query.Length > 120)
            return BadRequest("La búsqueda de dirección no puede superar los 120 caracteres.");

        try
        {
            var suggestions = await _service.SearchAsync(query, cancellationToken);
            return Ok(new AddressAutocompleteResponseDto
            {
                Suggestions = suggestions.Select(Map).ToList()
            });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "El servicio de direcciones no está configurado correctamente.");
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "El servicio de direcciones no está disponible temporalmente.");
        }
    }

    private static AddressSuggestionDto Map(AddressAutocompleteService.AddressSuggestion suggestion) =>
        new()
        {
            DisplayName = suggestion.DisplayName,
            Street = suggestion.Street,
            HouseNumber = suggestion.HouseNumber,
            PostalCode = suggestion.PostalCode,
            City = suggestion.City,
            Province = suggestion.Province,
            Country = suggestion.Country,
            CountryCode = suggestion.CountryCode,
            Latitude = suggestion.Latitude,
            Longitude = suggestion.Longitude
        };
}
