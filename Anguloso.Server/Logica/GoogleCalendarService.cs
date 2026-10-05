using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Integra la agenda profesional de DietoExpress con Google Calendar.
/// Los tokens se cifran antes de persistirse y la sincronización respeta el tenant del profesional.
/// Los eventos externos bloquean disponibilidad; las citas DietoExpress mantienen su propia identidad.
/// </summary>
public sealed class GoogleCalendarService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly angulosodbContext _db;
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleCalendarService> _logger;

    public GoogleCalendarService(IHttpClientFactory httpClientFactory, angulosodbContext db, IDataProtectionProvider protectionProvider, IConfiguration configuration, ILogger<GoogleCalendarService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
        _protector = protectionProvider.CreateProtector("DietoExpress.GoogleCalendar.v1");
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["GoogleCalendar:ClientId"]) &&
                                 !string.IsNullOrWhiteSpace(_configuration["GoogleCalendar:ClientSecret"]) &&
                                 !string.IsNullOrWhiteSpace(_configuration["GoogleCalendar:RedirectUri"]);

    public string BuildAuthorizationUrl(int userId, int tenantId, string state)
    {
        var clientId = _configuration["GoogleCalendar:ClientId"] ?? throw new InvalidOperationException("Google Calendar no está configurado.");
        var redirectUri = _configuration["GoogleCalendar:RedirectUri"] ?? throw new InvalidOperationException("Google Calendar no está configurado.");
        var scope = "https://www.googleapis.com/auth/calendar";
        return "https://accounts.google.com/o/oauth2/v2/auth" +
               "?client_id=" + Uri.EscapeDataString(clientId) +
               "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
               "&response_type=code&access_type=offline&prompt=consent" +
               "&scope=" + Uri.EscapeDataString(scope) +
               "&state=" + Uri.EscapeDataString(state);
    }

    public async Task CompleteAuthorizationAsync(string code, google_calendar_oauth_states oauthState, CancellationToken cancellationToken = default)
    {
        var clientId = _configuration["GoogleCalendar:ClientId"]!;
        var clientSecret = _configuration["GoogleCalendar:ClientSecret"]!;
        var redirectUri = _configuration["GoogleCalendar:RedirectUri"]!;
        using var client = _httpClientFactory.CreateClient();
        using var tokenResponse = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["code"] = code, ["client_id"] = clientId, ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri, ["grant_type"] = "authorization_code"
        }), cancellationToken);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            var googleError = TryReadGoogleOAuthError(tokenBody);
            _logger.LogError(
                "Google rechazó el intercambio OAuth. HTTP {StatusCode}. Error {ErrorCode}. Descripción: {ErrorDescription}. RedirectUri configurado: {RedirectUri}.",
                (int)tokenResponse.StatusCode,
                googleError.Error,
                googleError.Description,
                redirectUri);
            throw new InvalidOperationException("Google no ha autorizado el acceso al calendario.");
        }
        var token = JsonSerializer.Deserialize<GoogleTokenResponse>(tokenBody, JsonOptions)
            ?? throw new InvalidOperationException("Respuesta OAuth de Google no válida.");

        var user = await _db.users.AsNoTracking().Where(u => u.id == oauthState.user_id && u.tenant_id != null && u.archived_at == null).Select(u => new { u.id, TenantId = u.tenant_id!.Value }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Usuario no disponible.");

        _logger.LogInformation("Google OAuth: intercambio de código correcto para el usuario {UserId}; preparando persistencia de la conexión.", oauthState.user_id);
        var email = await GetUserEmailAsync(token.access_token, cancellationToken);
        var existing = await _db.google_calendar_connections.SingleOrDefaultAsync(x => x.user_id == user.id && x.tenant_id == user.TenantId, cancellationToken);
        if (existing == null)
        {
            existing = new google_calendar_connections { user_id = user.id, tenant_id = user.TenantId };
            _db.google_calendar_connections.Add(existing);
        }
        existing.google_account_email = email ?? "";
        existing.calendar_id = "primary";
        existing.access_token_encrypted = _protector.Protect(token.access_token);
        if (!string.IsNullOrWhiteSpace(token.refresh_token))
            existing.refresh_token_encrypted = _protector.Protect(token.refresh_token);
        existing.access_token_expires_at = DateTime.UtcNow.AddSeconds(Math.Max(60, token.expires_in - 60));
        existing.sync_token = null;
        existing.updated_at = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Google Calendar: conexión OAuth persistida para el usuario {UserId}, tenant {TenantId}, cuenta {GoogleAccountEmail}.", existing.user_id, existing.tenant_id, existing.google_account_email);
        try
        {
            await SyncUserAsync(existing.user_id, existing.tenant_id, cancellationToken);
        }
        catch (Exception ex)
        {
            // La autorización ya se ha persistido correctamente. Un fallo de la sincronización inicial
            // no debe hacer que Google parezca no conectado; el usuario podrá reintentar la sincronización.
            _logger.LogWarning(ex, "Google Calendar autorizado para el usuario {UserId}, pero falló la sincronización inicial.", existing.user_id);
        }
    }

    public async Task DisconnectAsync(int userId, int tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await _db.google_calendar_connections.SingleOrDefaultAsync(x => x.user_id == userId && x.tenant_id == tenantId, cancellationToken);
        if (connection == null) return;
        _db.external_calendar_events.RemoveRange(_db.external_calendar_events.Where(x => x.user_id == userId && x.tenant_id == tenantId && x.provider == "google"));
        _db.google_calendar_connections.Remove(connection);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GoogleCalendarConnectionDto?> GetConnectionAsync(int userId, int tenantId, CancellationToken cancellationToken = default)
    {
        return await _db.google_calendar_connections.AsNoTracking().Where(x => x.user_id == userId && x.tenant_id == tenantId)
            .Select(x => new GoogleCalendarConnectionDto(true, x.google_account_email, x.calendar_id, x.last_synced_at)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _db.google_calendar_connections.AsNoTracking().Select(x => new { x.user_id, x.tenant_id }).ToListAsync(cancellationToken);
        foreach (var item in ids)
        {
            try { await SyncUserAsync(item.user_id, item.tenant_id, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "Error sincronizando Google Calendar para usuario {UserId}.", item.user_id); }
        }
    }

    // Sincronización bidireccional: importa eventos externos, aplica cambios de horario a citas vinculadas
    // y publica las citas DietoExpress en Google mediante identificadores estables.
    // Sincroniza primero los cambios remotos mediante syncToken y después publica en Google
    // las citas locales; ambas direcciones comparten el identificador estable de DietoExpress.
    public async Task SyncUserAsync(int userId, int tenantId, CancellationToken cancellationToken = default)
    {
        var connection = await _db.google_calendar_connections.SingleOrDefaultAsync(x => x.user_id == userId && x.tenant_id == tenantId, cancellationToken);
        if (connection == null) return;

        var accessToken = await GetValidAccessTokenAsync(connection, cancellationToken);
        var events = await ListEventsAsync(accessToken, connection.calendar_id, connection.sync_token, cancellationToken);

        foreach (var item in events.Items)
        {
            var externalId = item.Id;
            if (string.IsNullOrWhiteSpace(externalId)) continue;
            var linkedId = TryGetAppointmentId(item);
            if (linkedId.HasValue)
            {
                var appointment = await _db.patient_appointments.SingleOrDefaultAsync(a => a.id == linkedId.Value && a.tenant_id == connection.tenant_id && a.nutritionist_id == connection.user_id, cancellationToken);
                if (appointment != null)
                {
                    var starts = ParseDate(item.Start);
                    var ends = ParseDate(item.End);
                    if (starts.HasValue && ends.HasValue && ends > starts)
                    {
                        appointment.starts_at = starts.Value;
                        appointment.ends_at = ends.Value;
                        appointment.updated_at = DateTime.UtcNow;
                    }
                    continue;
                }
            }

            if (item.Status == "cancelled")
            {
                var deleted = await _db.external_calendar_events.SingleOrDefaultAsync(x => x.user_id == connection.user_id && x.tenant_id == connection.tenant_id && x.provider == "google" && x.external_event_id == externalId, cancellationToken);
                if (deleted != null) _db.external_calendar_events.Remove(deleted);
                continue;
            }

            var start = ParseDate(item.Start);
            var end = ParseDate(item.End);
            if (!start.HasValue || !end.HasValue || end <= start) continue;
            var external = await _db.external_calendar_events.SingleOrDefaultAsync(x => x.user_id == connection.user_id && x.tenant_id == connection.tenant_id && x.provider == "google" && x.external_event_id == externalId, cancellationToken);
            if (external == null)
            {
                external = new external_calendar_events { tenant_id = connection.tenant_id, user_id = connection.user_id, provider = "google", external_event_id = externalId };
                _db.external_calendar_events.Add(external);
            }
            external.etag = item.ETag;
            external.title = item.Summary ?? "Evento de Google Calendar";
            external.starts_at = start.Value;
            external.ends_at = end.Value;
            external.is_all_day = item.Start?.Date != null;
            external.updated_at = DateTime.UtcNow;
        }

        await UpsertDietoExpressEventsAsync(accessToken, connection, cancellationToken);
        connection.sync_token = events.NextSyncToken;
        connection.last_synced_at = DateTime.UtcNow;
        connection.updated_at = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    // Se consulta antes de aceptar una reserva. Un evento externo solo bloquea el intervalo: no crea ni
    // cancela automáticamente una cita DietoExpress, evitando convertir un evento personal en una acción clínica.
    // Este método se usa durante una reserva: cualquier evento externo que se solape se trata
    // como ocupado, independientemente de que sea una cita de DietoExpress.
    public async Task<bool> IsBlockedAsync(int nutritionistId, int tenantId, DateTime startsUtc, DateTime endsUtc, CancellationToken cancellationToken = default)
    {
        return await _db.external_calendar_events.AsNoTracking().AnyAsync(x =>
            x.user_id == nutritionistId && x.tenant_id == tenantId &&
            x.starts_at < endsUtc && x.ends_at > startsUtc, cancellationToken);
    }

    private async Task UpsertDietoExpressEventsAsync(string accessToken, google_calendar_connections connection, CancellationToken cancellationToken)
    {
        var appointments = await _db.patient_appointments.AsNoTracking()
            .Where(a => a.tenant_id == connection.tenant_id && a.nutritionist_id == connection.user_id &&
                        a.starts_at < DateTime.UtcNow.AddDays(180) && a.ends_at > DateTime.UtcNow.AddDays(-30))
            .Select(a => new { a.id, a.starts_at, a.ends_at, a.status, ClientName = a.client.full_name })
            .ToListAsync(cancellationToken);

        foreach (var appointment in appointments)
        {
            var eventId = "dietoexpress-" + appointment.id;
            var payload = new Dictionary<string,object?>
            {
                ["summary"] = "DietoExpress · " + (appointment.ClientName ?? "Cita"),
                ["description"] = "Cita gestionada desde DietoExpress. ID " + appointment.id,
                ["start"] = new { dateTime = appointment.starts_at.ToString("o"), timeZone = "UTC" },
                ["end"] = new { dateTime = appointment.ends_at.ToString("o"), timeZone = "UTC" },
                ["extendedProperties"] = new { @private = new Dictionary<string,string> { ["dietoexpressAppointmentId"] = appointment.id.ToString() } }
            };
            if (appointment.status == "cancelled") payload["status"] = "cancelled";
            var existing = await GetEventAsync(accessToken, connection.calendar_id, eventId, cancellationToken);
            await SendEventAsync(accessToken, connection.calendar_id, eventId, payload, existing == null ? "PUT" : "PUT", cancellationToken);
        }
    }

    // Los tokens se almacenan protegidos y se refrescan solo cuando están próximos a caducar;
    // el nuevo token se persiste para que las siguientes sincronizaciones no repitan el refresh.
    // El token de acceso de Google caduca; si está próximo a caducar se renueva usando el refresh token cifrado
    // y se persiste el nuevo valor para que la siguiente sincronización no tenga que repetir el refresh.
    private async Task<string> GetValidAccessTokenAsync(google_calendar_connections connection, CancellationToken cancellationToken)
    {
        if (connection.access_token_expires_at > DateTime.UtcNow.AddMinutes(1))
            return _protector.Unprotect(connection.access_token_encrypted);
        if (string.IsNullOrWhiteSpace(connection.refresh_token_encrypted)) throw new InvalidOperationException("La conexión de Google Calendar no tiene refresh token.");
        var refresh = _protector.Unprotect(connection.refresh_token_encrypted);
        using var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["client_id"] = _configuration["GoogleCalendar:ClientId"]!,
            ["client_secret"] = _configuration["GoogleCalendar:ClientSecret"]!,
            ["refresh_token"] = refresh,
            ["grant_type"] = "refresh_token"
        }), cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("No se pudo renovar el token de Google Calendar.");
        var token = JsonSerializer.Deserialize<GoogleTokenResponse>(await response.Content.ReadAsStringAsync(cancellationToken))!;
        connection.access_token_encrypted = _protector.Protect(token.access_token);
        connection.access_token_expires_at = DateTime.UtcNow.AddSeconds(Math.Max(60, token.expires_in - 60));
        connection.updated_at = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return token.access_token;
    }

    private async Task<string?> GetUserEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.GetAsync("https://www.googleapis.com/oauth2/v3/userinfo", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
    }

    private async Task<GoogleEventsResponse> ListEventsAsync(string accessToken, string calendarId, string? syncToken, CancellationToken cancellationToken)
    {
        using var client = CreateClient(accessToken);
        var url = "https://www.googleapis.com/calendar/v3/calendars/" + Uri.EscapeDataString(calendarId) + "/events?singleEvents=true&showDeleted=true&maxResults=2500";
        if (!string.IsNullOrWhiteSpace(syncToken)) url += "&syncToken=" + Uri.EscapeDataString(syncToken);
        using var response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Gone && !string.IsNullOrWhiteSpace(syncToken))
        {
            _logger.LogWarning("Google Calendar devolvió 410 para el syncToken del usuario; se reiniciará la sincronización incremental.");
            return await ListEventsAsync(accessToken, calendarId, null, cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Google Calendar rechazó la lectura de eventos. HTTP {StatusCode}. CalendarId {CalendarId}. Respuesta: {ResponseBody}",
                (int)response.StatusCode,
                calendarId,
                body);
            throw new InvalidOperationException($"Google Calendar rechazó la lectura de eventos (HTTP {(int)response.StatusCode}).");
        }

        return JsonSerializer.Deserialize<GoogleEventsResponse>(body, JsonOptions) ?? new();
    }

    private async Task<JsonElement?> GetEventAsync(string accessToken, string calendarId, string eventId, CancellationToken cancellationToken)
    {
        using var client = CreateClient(accessToken);
        using var response = await client.GetAsync("https://www.googleapis.com/calendar/v3/calendars/" + Uri.EscapeDataString(calendarId) + "/events/" + Uri.EscapeDataString(eventId), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
    }

    private async Task SendEventAsync(string accessToken, string calendarId, string eventId, Dictionary<string,object?> payload, string method, CancellationToken cancellationToken)
    {
        using var client = CreateClient(accessToken);
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://www.googleapis.com/calendar/v3/calendars/" + Uri.EscapeDataString(calendarId) + "/events/" + Uri.EscapeDataString(eventId));
        request.Content = content;
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Google Calendar rechazó la escritura de un evento. HTTP {StatusCode}. CalendarId {CalendarId}. EventId {EventId}. Respuesta: {ResponseBody}",
                (int)response.StatusCode,
                calendarId,
                eventId,
                body);
            throw new InvalidOperationException($"Google Calendar rechazó la sincronización (HTTP {(int)response.StatusCode}).");
        }
    }

    private static (string? Error, string? Description) TryReadGoogleOAuthError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            var description = root.TryGetProperty("error_description", out var descriptionElement) ? descriptionElement.GetString() : null;
            return (error, description);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static int? TryGetAppointmentId(GoogleCalendarEvent item)
    {
        if (item.ExtendedProperties?.Private_?.TryGetValue("dietoexpressAppointmentId", out var raw) == true && int.TryParse(raw, out var id)) return id;
        return null;
    }

    private static DateTime? ParseDate(GoogleCalendarEventDate? value)
    {
        if (value == null) return null;
        if (!string.IsNullOrWhiteSpace(value.DateTime)) return DateTime.Parse(value.DateTime, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
        if (!string.IsNullOrWhiteSpace(value.Date)) return DateTime.SpecifyKind(DateTime.Parse(value.Date), DateTimeKind.Utc);
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private sealed record GoogleTokenResponse(string access_token, int expires_in, string? refresh_token);
    private sealed class GoogleEventsResponse { public List<GoogleCalendarEvent> Items { get; set; } = []; public string? NextSyncToken { get; set; } }
    private sealed class GoogleCalendarEvent { public string? Id { get; set; } public string? Status { get; set; } public string? Summary { get; set; } public string? ETag { get; set; } public GoogleCalendarEventDate? Start { get; set; } public GoogleCalendarEventDate? End { get; set; } public GoogleExtendedProperties? ExtendedProperties { get; set; } }
    private sealed class GoogleCalendarEventDate { public string? DateTime { get; set; } public string? Date { get; set; } }
    private sealed class GoogleExtendedProperties { [System.Text.Json.Serialization.JsonPropertyName("private")] public Dictionary<string,string>? Private_ { get; set; } }
    public sealed record GoogleCalendarConnectionDto(bool Connected, string Email, string CalendarId, DateTime? LastSyncedAt);
}
