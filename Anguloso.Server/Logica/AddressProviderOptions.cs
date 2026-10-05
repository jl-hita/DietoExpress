namespace Anguloso.Server.Logica;

/// <summary>
/// Configuración de los proveedores de autocompletado y de sus límites diarios.
/// La tabla PostgreSQL <c>config</c> es la fuente de verdad de estos valores.
/// Las claves se consumen exclusivamente en backend y nunca se exponen al cliente.
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

    /// <summary>
    /// Carga la configuración desde ConfigServ para mantener una única fuente de verdad
    /// y permitir cambiar las claves sin recompilar la aplicación.
    /// </summary>
    public static AddressProviderOptions Load(ConfigServ config)
    {
        return new AddressProviderOptions
        {
            Primary = config.GetConfigString("addressPrimaryProvider", "Geoapify") ?? "Geoapify",
            Fallback = config.GetConfigString("addressFallbackProvider", "LocationIQ") ?? "LocationIQ",
            WarningThreshold = GetDouble(config, "addressWarningThreshold", 0.80),
            FailoverThreshold = GetDouble(config, "addressFailoverThreshold", 0.90),
            GeoapifyDailyLimit = config.GetConfigInt("addressGeoapifyDailyLimit", 3000) ?? 3000,
            LocationIqDailyLimit = config.GetConfigInt("addressLocationIqDailyLimit", 5000) ?? 5000,
            GeoapifyApiKey = config.GetConfigString("geoapifyApiKey"),
            LocationIqApiKey = config.GetConfigString("locationIqApiKey")
        };
    }

    private static double GetDouble(ConfigServ config, string name, double defaultValue)
    {
        var raw = config.GetConfigString(name, defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return double.TryParse(
            raw,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : defaultValue;
    }
}
