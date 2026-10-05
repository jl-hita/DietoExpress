namespace Anguloso.Server.Logica;

/// <summary>
/// Configuración de los proveedores de autocompletado y de sus límites diarios.
/// Los valores sensibles se inyectan mediante configuración del servidor; nunca se versionan.
/// </summary>
public sealed class AddressProviderOptions
{
    public string Primary { get; set; } = "Geoapify";
    public string Fallback { get; set; } = "LocationIQ";
    public double WarningThreshold { get; set; } = 0.80;
    public double FailoverThreshold { get; set; } = 0.90;
    public int GeoapifyDailyLimit { get; set; } = 3000;
    public int LocationIqDailyLimit { get; set; } = 5000;
    public string? GeoapifyApiKey { get; set; }
    public string? LocationIqApiKey { get; set; }
}