namespace Anguloso.Server.Logica;

/// <summary>
/// Configuración persistente del proveedor LiveKit. Las claves se leen exclusivamente
/// de la tabla config y nunca se envían al cliente.
/// </summary>
public sealed class LiveKitVideoOptions
{
    public bool Enabled { get; init; }
    public string ServerUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
    public int RoomExpiryMinutesAfterAppointment { get; init; } = 30;
    public int RoomCreationLeadMinutes { get; init; } = 60;
    public int EmptyRoomTimeoutSeconds { get; init; } = 300;
    public int MaxCallDurationMinutes { get; init; } = 60;

    public bool IsConfigured =>
        Enabled &&
        Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "wss" &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);
}
