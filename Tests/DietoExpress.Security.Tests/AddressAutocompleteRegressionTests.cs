using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Protege el contrato del autocompletado y evita que la integración externa se exponga al frontend.
/// </summary>
public sealed class AddressAutocompleteRegressionTests
{
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
        var geoapify = ReadSource("Anguloso.Server/Logica/GeoapifyAddressProvider.cs");
        var locationIq = ReadSource("Anguloso.Server/Logica/LocationIqAddressProvider.cs");

        Assert.Contains("[Authorize(Policy = \"Professional\")]", controller);
        Assert.Contains("[EnableRateLimiting(\"expensive\")]", controller);
        Assert.Contains("query.Length < 3", controller);
        Assert.Contains("query.Length > 120", controller);
        Assert.Contains("_options.Primary", service);
        Assert.Contains("Geoapify", geoapify);
        Assert.Contains("filter=countrycode:es", geoapify);
        Assert.Contains("countrycodes=es", locationIq);
        Assert.Contains("limit=5", geoapify);
        Assert.Contains("limit=5", locationIq);
    }

    [Fact]
    public void AddressUsageMustBeAtomicallyReservedBeforeExternalCalls()
    {
        var usage = ReadSource("Anguloso.Server/Logica/AddressUsageService.cs");
        var bootstrap = ReadSource("Anguloso.Server/Logica/DatabaseBootstrap.cs");
        var service = ReadSource("Anguloso.Server/Logica/AddressAutocompleteService.cs");

        Assert.Contains("ON CONFLICT (usage_date, provider, operation)", usage);
        Assert.Contains("WHERE external_api_usage.request_count < @cap", usage);
        Assert.Contains("RETURNING request_count", usage);
        Assert.Contains("provider.IsConfigured", service);
        Assert.Contains("TryReserveAsync(provider.Name, provider.DailyLimit", service);
        Assert.Contains("CREATE TABLE IF NOT EXISTS external_api_usage", bootstrap);
    }

    [Fact]
    public void AddressProvidersMustKeepKeysOnTheBackend()
    {
        var geoapify = ReadSource("Anguloso.Server/Logica/GeoapifyAddressProvider.cs");
        var locationIq = ReadSource("Anguloso.Server/Logica/LocationIqAddressProvider.cs");
        var angular = ReadSource("anguloso.client/src/app/servicios/address.service.ts");

        Assert.Contains("GeoapifyApiKey", geoapify);
        Assert.Contains("LocationIqApiKey", locationIq);
        Assert.Contains("GetConfigString(\"geoapifyApiKey\")", ReadSource("Anguloso.Server/Logica/AddressProviderOptions.cs"));
        Assert.Contains("GetConfigString(\"locationIqApiKey\")", ReadSource("Anguloso.Server/Logica/AddressProviderOptions.cs"));
        Assert.DoesNotContain("GeoapifyApiKey", angular);
        Assert.DoesNotContain("LocationIqApiKey", angular);
        Assert.DoesNotContain("apiKey=", angular);
        Assert.DoesNotContain("key=", angular);
    }

    [Fact]
    public void DatabaseBootstrapMustSeedProviderPlaceholders()
    {
        var bootstrap = ReadSource("Anguloso.Server/Logica/DatabaseBootstrap.cs");

        Assert.Contains("'geoapifyApiKey', '__CONFIGURE_GEOAPIFY_API_KEY__'", bootstrap);
        Assert.Contains("'locationIqApiKey', '__CONFIGURE_LOCATIONIQ_API_KEY__'", bootstrap);
        Assert.Contains("'addressPrimaryProvider', 'Geoapify'", bootstrap);
        Assert.Contains("'addressFallbackProvider', 'LocationIQ'", bootstrap);
        Assert.Contains("'geoapifyApiKey', '__CONFIGURE_GEOAPIFY_API_KEY__'", bootstrap);
        Assert.Contains("'locationIqApiKey', '__CONFIGURE_LOCATIONIQ_API_KEY__'", bootstrap);
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
    public void AddressProviderConfigurationMustNotReadEnvironmentKeys()
    {
        var program = ReadSource("Anguloso.Server/Program.cs");

        Assert.DoesNotContain("Geoapify:ApiKey", program);
        Assert.DoesNotContain("LocationIQ:ApiKey", program);
        Assert.Contains("AddressProviderOptions.Load", program);
        Assert.Contains("GetRequiredService<ConfigServ>()", program);
    }

    [Fact]
    public void AngularMustNotContainExternalProviderSecrets()
    {
        var service = ReadSource("anguloso.client/src/app/servicios/address.service.ts");
        var component = ReadSource("anguloso.client/src/app/componentes/address-autocomplete/address-autocomplete.component.ts");

        Assert.DoesNotContain("Geoapify:ApiKey", service);
        Assert.DoesNotContain("GeoapifyApiKey", service);
        Assert.DoesNotContain("LocationIqApiKey", service);
        Assert.DoesNotContain("apiKey=", service);
        Assert.Contains("debounceTime(350)", component);
        Assert.Contains("query.length < 3", component);
    }
}