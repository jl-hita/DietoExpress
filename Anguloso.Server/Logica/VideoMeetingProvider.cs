using Livekit.Server.Sdk.Dotnet;

namespace Anguloso.Server.Logica;

/// <summary>
/// Abstracción del proveedor de videollamadas. DietoExpress no depende de la API
/// de un proveedor concreto y puede sustituirlo sin cambiar el modelo de citas.
/// </summary>
public interface IVideoMeetingProvider
{
    Task<VideoMeetingRoom> CreatePrivateRoomAsync(
        string externalReference,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        CancellationToken cancellationToken = default);

    Task<string> CreateMeetingTokenAsync(
        string roomName,
        string userName,
        string userId,
        bool isOwner,
        DateTime notBeforeUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);

    Task DeleteRoomAsync(string roomName, CancellationToken cancellationToken = default);
}

/// <summary>Identidad mínima de una sala externa asociada a una cita.</summary>
public sealed record VideoMeetingRoom(
    string Provider,
    string RoomName,
    string RoomUrl,
    DateTime ExpiresAtUtc);

/// <summary>
/// Implementación LiveKit. Las credenciales permanecen exclusivamente en servidor.
/// El navegador recibe únicamente el endpoint y un token de corta duración emitido
/// después de que el backend haya autorizado la cita.
/// </summary>
public sealed class LiveKitVideoMeetingProvider : IVideoMeetingProvider
{
    private readonly LiveKitVideoOptions _options;
    private readonly ILogger<LiveKitVideoMeetingProvider> _logger;
    private readonly RoomServiceClient _rooms;

    public LiveKitVideoMeetingProvider(
        LiveKitVideoOptions options,
        ILogger<LiveKitVideoMeetingProvider> logger)
    {
        _options = options;
        _logger = logger;
        if (_options.IsConfigured)
        {
            _rooms = new RoomServiceClient(_options.ServerUrl, _options.ApiKey, _options.ApiSecret);
        }
        else
        {
            _rooms = null!;
        }
    }

    public async Task<VideoMeetingRoom> CreatePrivateRoomAsync(
        string externalReference,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc <= startsAtUtc)
            throw new ArgumentException("El intervalo de la videollamada debe estar expresado en UTC.");

        cancellationToken.ThrowIfCancellationRequested();

        // La identidad de la sala es deliberadamente opaca: no incluye paciente,
        // tenant, email ni siquiera el identificador de la cita.
        var roomName = $"dieto-{Guid.NewGuid():N}";
        var expiry = endsAtUtc.AddMinutes(Math.Clamp(_options.RoomExpiryMinutesAfterAppointment, 5, 1440));

        try
        {
            await _rooms.CreateRoom(new CreateRoomRequest
            {
                Name = roomName,
                EmptyTimeout = Math.Clamp(_options.EmptyRoomTimeoutSeconds, 60, 3600),
                MaxParticipants = 2
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LiveKit no pudo crear la sala para la referencia {Reference}.", externalReference);
            throw new HttpRequestException("No se pudo crear la sala de videollamada.", ex);
        }

        return new VideoMeetingRoom("livekit", roomName, _options.ServerUrl, expiry);
    }

    public async Task<string> CreateMeetingTokenAsync(
        string roomName,
        string userName,
        string userId,
        bool isOwner,
        DateTime notBeforeUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(roomName))
            throw new ArgumentException("La sala de videollamada no es válida.", nameof(roomName));
        if (notBeforeUtc.Kind != DateTimeKind.Utc || expiresAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc <= notBeforeUtc)
            throw new ArgumentException("La ventana del token debe estar expresada en UTC.");

        var now = DateTime.UtcNow;
        var effectiveNotBefore = notBeforeUtc < now ? now : notBeforeUtc;
        if (effectiveNotBefore >= expiresAtUtc)
            throw new InvalidOperationException("La ventana de acceso a la videollamada ya ha expirado.");

        cancellationToken.ThrowIfCancellationRequested();
        var ttl = expiresAtUtc - effectiveNotBefore;
        var token = new AccessToken(_options.ApiKey, _options.ApiSecret)
            .WithIdentity(userId)
            .WithName(userName)
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                Room = roomName,
                CanPublish = true,
                CanSubscribe = true,
                CanPublishData = false,
                RoomAdmin = false
            })
            .WithTtl(ttl);

        return token.ToJwt();
    }

    public async Task DeleteRoomAsync(string roomName, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured || string.IsNullOrWhiteSpace(roomName)) return;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _rooms.DeleteRoom(new DeleteRoomRequest { Room = roomName });
        }
        catch (Exception ex)
        {
            // La eliminación es best-effort: la autorización de DietoExpress ya puede
            // haber sido revocada y LiveKit también expira las salas inactivas.
            _logger.LogWarning(ex, "LiveKit no pudo eliminar la sala {RoomName}.", roomName);
        }
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("El proveedor de videollamadas no está configurado.");
    }
}
