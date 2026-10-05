namespace Anguloso.Server.Logica;

/// <summary>
/// Contrato interno para proveedores de direcciones. El resto de la aplicación no conoce sus APIs.
/// </summary>
public interface IAddressProvider
{
    string Name { get; }
    int DailyLimit { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<AddressAutocompleteService.AddressSuggestion>> SearchAsync(
        string text,
        CancellationToken cancellationToken);
}