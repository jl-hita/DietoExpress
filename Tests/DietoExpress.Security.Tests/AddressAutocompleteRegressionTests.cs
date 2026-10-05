using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Protege el contrato del autocompletado para que el proveedor externo permanezca encapsulado
/// y el formulario de pacientes no pueda acabar dependiendo de una clave de API en Angular.
/// </summary>
public sealed class AddressAutocompleteRegressionTests
{
    // Las regresiones verifican la frontera de integración y la minimización de datos expuestos por el listado.

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    private static string ReadSource(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot, relativePath));

    [Fact]
    public void AddressAutocompleteMustBeServerSideAndRestrictedToSpain()
    {
        var controller = ReadSource("Anguloso.Server/Controllers/AddressAutocompleteController.cs");
        var service = ReadSource("Anguloso.Server/Logica/AddressAutocompleteService.cs");

        Assert.Contains("[Authorize(Policy = \"Professional\")]", controller);
        Assert.Contains("[EnableRateLimiting(\"expensive\")]", controller);
        Assert.Contains("query.Length < 3", controller);
        Assert.Contains("query.Length > 120", controller);
        Assert.Contains("Geoapify:ApiKey", service);
        Assert.Contains("filter=countrycode:es", service);
        Assert.Contains("limit=5", service);
    }

    [Fact]
    public void PatientAddressMustBePersistedAsStructuredFields()
    {
        var model = ReadSource("Anguloso.Server/Models/clients.cs");
        var dto = ReadSource("Anguloso.Server/Model/ClientDto.cs");
        var controller = ReadSource("Anguloso.Server/Controllers/ClientsController.cs");
        var bootstrap = ReadSource("Anguloso.Server/Logica/DatabaseBootstrap.cs");

        Assert.Contains("public string address", model);
        Assert.Contains("public string postal_code", model);
        Assert.Contains("public string city", model);
        Assert.Contains("public string province", model);
        Assert.Contains("public double? latitude", model);
        Assert.Contains("public double? longitude", model);
        Assert.Contains("public string? Address", dto);
        Assert.Contains("public string? PostalCode", dto);
        Assert.Contains("public string? City", dto);
        Assert.Contains("public string? Province", dto);
        Assert.Contains("client.address = dto.Address", controller);
        Assert.Contains("client.postal_code = dto.PostalCode", controller);
        Assert.Contains("client.city = dto.City", controller);
        Assert.Contains("client.province = dto.Province", controller);
        Assert.Contains("UpgradeClientAddressSchemaV1", bootstrap);
    }

    [Fact]
    public void AngularMustNotContainTheGeoapifyApiKey()
    {
        var service = ReadSource("anguloso.client/src/app/servicios/address.service.ts");
        var component = ReadSource("anguloso.client/src/app/componentes/address-autocomplete/address-autocomplete.component.ts");

        Assert.DoesNotContain("Geoapify:ApiKey", service);
        Assert.DoesNotContain("apiKey=", service);
        Assert.Contains("debounceTime(350)", component);
        Assert.Contains("query.length < 3", component);
    }
}
