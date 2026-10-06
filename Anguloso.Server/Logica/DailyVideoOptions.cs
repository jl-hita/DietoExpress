namespace Anguloso.Server.Logica;

/// <summary>
/// Configuración del proveedor externo de videollamadas Daily.
/// La clave nunca se expone al cliente; solo el backend la utiliza.
/// </summary>
public sealed class DailyVideoOptions
{
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public int RoomExpiryMinutesAfterAppointment { get; init; } = 30;
    public int RoomCreationLeadMinutes { get; init; } = 60;

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Domain);
}
