using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Anguloso.Server.Logica;

/// <summary>
/// Abstracción del proveedor de videollamadas. DietoExpress no depende de la API
/// de un proveedor concreto y podrá sustituirlo sin cambiar el modelo de citas.
/// </summary>
public interface IVideoMeetingProvider
{
    Task<VideoMeetingRoom> CreatePrivateRoomAsync(
        string externalReference,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        CancellationToken cancellationToken = default);

    Task DeleteRoomAsync(string roomName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Identidad mínima de una sala externa asociada a una cita.
/// </summary>
public sealed record VideoMeetingRoom(
    string Provider,
    string RoomName,
    string RoomUrl,
    DateTime ExpiresAtUtc);

/// <summary>
/// Implementación Daily mediante su API REST. La API key permanece exclusivamente
/// en servidor y las salas se crean privadas para consultas con información sensible.
/// </summary>
public sealed class DailyVideoMeetingProvider : IVideoMeetingProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DailyVideoOptions _options;
    private readonly ILogger<DailyVideoMeetingProvider> _logger;

    public DailyVideoMeetingProvider(
        IHttpClientFactory httpClientFactory,
        DailyVideoOptions options,
        ILogger<DailyVideoMeetingProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<VideoMeetingRoom> CreatePrivateRoomAsync(
        string externalReference,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("El proveedor de videollamadas no está configurado.");

        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc <= startsAtUtc)
            throw new ArgumentException("El intervalo de la videollamada debe estar expresado en UTC.");

        // El nombre es opaco: no contiene tenant, paciente, email ni otros datos personales.
        var safeReference = new string(externalReference
            .Where(char.IsLetterOrDigit)
            .Take(32)
            .ToArray());

        var roomName = $"dieto-{safeReference}-{Guid.NewGuid():N}"[..48];
        var expiry = new DateTimeOffset(
            endsAtUtc.AddMinutes(Math.Clamp(_options.RoomExpiryMinutesAfterAppointment, 5, 1440)),
            TimeSpan.Zero).ToUnixTimeSeconds();

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.daily.co/v1/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var payload = new
        {
            name = roomName,
            privacy = "private",
            properties = new
            {
                exp = expiry,
                enable_chat = true,
                start_video_off = false,
                start_audio_off = false
            }
        };

        using var response = await client.PostAsJsonAsync("rooms", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Daily no pudo crear la sala para {Reference}. HTTP {StatusCode}.",
                externalReference,
                (int)response.StatusCode);
            throw new HttpRequestException($"No se pudo crear la sala de videollamada. HTTP {(int)response.StatusCode}.");
        }

        var result = await response.Content.ReadFromJsonAsync<DailyRoomResponse>(cancellationToken: cancellationToken)
                     ?? throw new InvalidOperationException("Daily devolvió una respuesta vacía al crear la sala.");

        if (string.IsNullOrWhiteSpace(result.Name) || string.IsNullOrWhiteSpace(result.Url))
            throw new InvalidOperationException("Daily no devolvió una sala válida.");

        return new VideoMeetingRoom(
            "daily",
            result.Name,
            result.Url,
            DateTimeOffset.FromUnixTimeSeconds(expiry).UtcDateTime);
    }

    public async Task DeleteRoomAsync(string roomName, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured || string.IsNullOrWhiteSpace(roomName))
            return;

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.daily.co/v1/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await client.DeleteAsync($"rooms/{Uri.EscapeDataString(roomName)}", cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Daily no pudo eliminar la sala {RoomName}. HTTP {StatusCode}.",
                roomName,
                (int)response.StatusCode);
        }
    }

    private sealed class DailyRoomResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
