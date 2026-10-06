using System.Net.Http.Headers;
using System.Text.Json;
using Livekit.Server.Sdk.Dotnet;

namespace Anguloso.Server.Logica;

public sealed record LiveKitAnalyticsResult(
    bool Available,
    int ConnectionMinutes,
    int Sessions,
    DateTime FromUtc,
    DateTime ToUtc,
    string? Message);

/// <summary>
/// Consulta el consumo real de LiveKit Cloud cuando la cuenta dispone de Analytics API.
/// La API oficial limita Analytics a planes Scale o superiores, por lo que un proyecto Build
/// sigue funcionando con las cuotas internas estimadas sin que el panel falle.
/// </summary>
public sealed class LiveKitAnalyticsService
{
    private readonly ConfigServ _config;
    private readonly HttpClient _http;
    private readonly ILogger<LiveKitAnalyticsService> _logger;

    public LiveKitAnalyticsService(ConfigServ config, IHttpClientFactory httpClientFactory, ILogger<LiveKitAnalyticsService> logger)
    {
        _config = config;
        _http = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task<LiveKitAnalyticsResult> GetRecentConnectionMinutesAsync(CancellationToken cancellationToken = default)
    {
        var projectId = _config.GetConfigString("VIDEO_LIVEKIT_PROJECT_ID", "")?.Trim();
        var apiKey = _config.GetConfigString("VIDEO_LIVEKIT_API_KEY", "")?.Trim();
        var apiSecret = _config.GetConfigString("VIDEO_LIVEKIT_API_SECRET", "")?.Trim();

        var to = DateTime.UtcNow;
        var from = to.Date.AddDays(-6);
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            return new(false, 0, 0, from, to, "Configura VIDEO_LIVEKIT_PROJECT_ID para consultar Analytics de LiveKit.");

        try
        {
            var token = new AccessToken(apiKey, apiSecret)
                .WithGrants(new VideoGrants { RoomList = true })
                .WithTtl(TimeSpan.FromMinutes(5))
                .ToJwt();

            var url = $"https://cloud-api.livekit.io/api/project/{Uri.EscapeDataString(projectId)}/sessions?page=0&limit=100&start={from:yyyy-MM-dd}&end={to:yyyy-MM-dd}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var message = status is 401 or 403
                    ? "LiveKit Analytics no está disponible para las credenciales o el plan actual (requiere Scale o superior)."
                    : $"LiveKit Analytics respondió HTTP {status}.";
                return new(false, 0, 0, from, to, message);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var sessions = await JsonSerializer.DeserializeAsync<LiveKitSessionsResponse>(stream, cancellationToken: cancellationToken)
                ?? new LiveKitSessionsResponse();

            var total = 0;
            foreach (var session in sessions.Sessions ?? [])
            {
                var detailUrl = $"https://cloud-api.livekit.io/api/project/{Uri.EscapeDataString(projectId)}/sessions/{Uri.EscapeDataString(session.SessionId)}";
                using var detailRequest = new HttpRequestMessage(HttpMethod.Get, detailUrl);
                detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var detailResponse = await _http.SendAsync(detailRequest, cancellationToken);
                if (!detailResponse.IsSuccessStatusCode) continue;

                await using var detailStream = await detailResponse.Content.ReadAsStreamAsync(cancellationToken);
                var detail = await JsonSerializer.DeserializeAsync<LiveKitSessionDetail>(detailStream, cancellationToken: cancellationToken);
                total += Math.Max(0, detail?.ConnectionMinutes ?? 0);
            }

            return new(true, total, sessions.Sessions?.Count ?? 0, from, to,
                (sessions.Sessions?.Count ?? 0) >= 100 ? "Se muestran las primeras 100 sesiones del periodo." : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo consultar LiveKit Analytics.");
            return new(false, 0, 0, from, to, "No se pudo consultar LiveKit Analytics en este momento.");
        }
    }

    private sealed class LiveKitSessionsResponse
    {
        public List<LiveKitSession> Sessions { get; set; } = [];
    }

    private sealed class LiveKitSession
    {
        public string SessionId { get; set; } = string.Empty;
    }

    private sealed class LiveKitSessionDetail
    {
        public int ConnectionMinutes { get; set; }
    }
}
