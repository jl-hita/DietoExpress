using System.Text.Json;
using Npgsql;
using WebPush;

namespace Anguloso.Server.Logica;

// La notificación persistida es la fuente durable; los canales de entrega se ejecutan como complemento y no deben hacer desaparecer el registro.
public sealed class NotificationService
{
    private readonly string _connectionString;
    private readonly ConfigServ _configServ;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ConfigServ configServ, IConfiguration configuration, ILogger<NotificationService> logger)
    {
        _configServ = configServ;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
    }

    // La notificación persistida es la fuente de verdad; el push es un canal adicional y no debe impedir guardar la notificación.
    public async Task<PatientCommunicationPreferences> GetCommunicationPreferencesAsync(int tenantId, int clientId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT in_app_enabled, email_enabled, push_enabled FROM patient_communication_preferences WHERE tenant_id=@tenant AND client_id=@client LIMIT 1;", connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", clientId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new PatientCommunicationPreferences(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2))
            : new PatientCommunicationPreferences(true, true, true);
    }

    public async Task SetCommunicationPreferencesAsync(int tenantId, int clientId, PatientCommunicationPreferences preferences)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO patient_communication_preferences(client_id, tenant_id, in_app_enabled, email_enabled, push_enabled, updated_at)
            VALUES(@client,@tenant,@inapp,@email,@push,NOW())
            ON CONFLICT (client_id) DO UPDATE SET tenant_id=EXCLUDED.tenant_id, in_app_enabled=EXCLUDED.in_app_enabled, email_enabled=EXCLUDED.email_enabled, push_enabled=EXCLUDED.push_enabled, updated_at=NOW();
            """, connection);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("inapp", preferences.InAppEnabled);
        command.Parameters.AddWithValue("email", preferences.EmailEnabled);
        command.Parameters.AddWithValue("push", preferences.PushEnabled);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> CreateForPatientAsync(int tenantId, int clientId, string type, string title, string message, string? actionUrl = null, bool sendPush = true)
    {
        var preferences = await GetCommunicationPreferencesAsync(tenantId, clientId);
        if (!preferences.InAppEnabled) return 0;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
INSERT INTO patient_notifications (tenant_id, client_id, type, title, message, action_url, created_at)
VALUES (@tenant, @client, @type, @title, @message, @action, NOW())
RETURNING id;", connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("message", message);
        command.Parameters.AddWithValue("action", (object?)actionUrl ?? DBNull.Value);
        // La notificación in-app queda persistida antes de intentar push: un fallo del proveedor no debe hacer
        // desaparecer el aviso que el paciente puede consultar desde el portal.
        var id = Convert.ToInt64(await command.ExecuteScalarAsync());
        // El push se intenta después de confirmar la notificación in-app; así el canal efímero nunca define si el aviso existe.
        if (sendPush && preferences.PushEnabled) await SendPushAsync(tenantId, clientId, new PushPayload(title, message, actionUrl));
        return id;
    }

    public async Task<IReadOnlyList<PatientNotificationDto>> GetForPatientAsync(int tenantId, int clientId, int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);
        var result = new List<PatientNotificationDto>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
SELECT id, type, title, message, action_url, created_at, read_at
FROM patient_notifications
WHERE tenant_id = @tenant AND client_id = @client
ORDER BY created_at DESC
LIMIT @limit;", connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PatientNotificationDto
            {
                Id = reader.GetInt64(0),
                Type = reader.GetString(1),
                Title = reader.GetString(2),
                Message = reader.GetString(3),
                ActionUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                CreatedAt = reader.GetDateTime(5),
                ReadAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
            });
        }
        return result;
    }

    public async Task<bool> MarkAsReadAsync(int tenantId, int clientId, long notificationId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
UPDATE patient_notifications
SET read_at = COALESCE(read_at, NOW())
WHERE id = @id AND tenant_id = @tenant AND client_id = @client
RETURNING id;", connection);
        command.Parameters.AddWithValue("id", notificationId);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", clientId);
        return await command.ExecuteScalarAsync() != null;
    }

    // El endpoint identifica de forma única la suscripción del navegador; al volver a registrarlo se actualizan sus claves y propietario.
    // Esto permite renovar una suscripción sin acumular registros obsoletos para el mismo endpoint.
    public async Task RegisterPushSubscriptionAsync(int tenantId, int clientId, PushSubscriptionDto subscription)
    {
        if (string.IsNullOrWhiteSpace(subscription.Endpoint) ||
            string.IsNullOrWhiteSpace(subscription.P256dh) ||
            string.IsNullOrWhiteSpace(subscription.Auth))
            throw new ArgumentException("La suscripción push no es válida.");
        if (subscription.Endpoint.Length > 2000 || subscription.P256dh.Length > 500 || subscription.Auth.Length > 500)
            throw new ArgumentException("La suscripción push supera los límites permitidos.");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
INSERT INTO patient_push_subscriptions (tenant_id, client_id, endpoint, p256dh, auth, created_at, updated_at)
VALUES (@tenant, @client, @endpoint, @p256dh, @auth, NOW(), NOW())
ON CONFLICT (endpoint) DO UPDATE SET
    tenant_id = EXCLUDED.tenant_id,
    client_id = EXCLUDED.client_id,
    p256dh = EXCLUDED.p256dh,
    auth = EXCLUDED.auth,
    updated_at = NOW();", connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("endpoint", subscription.Endpoint);
        command.Parameters.AddWithValue("p256dh", subscription.P256dh);
        command.Parameters.AddWithValue("auth", subscription.Auth);
        await command.ExecuteNonQueryAsync();
    }

    public async Task RemovePushSubscriptionAsync(int tenantId, int clientId, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(@"
DELETE FROM patient_push_subscriptions
WHERE tenant_id = @tenant AND client_id = @client AND endpoint = @endpoint;", connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("endpoint", endpoint);
        await command.ExecuteNonQueryAsync();
    }

    public string? GetVapidPublicKey()
    {
        var key = _configServ.GetConfigString("webPushPublicKey");
        return IsPlaceholder(key) ? null : key;
    }

    private string? GetWebPushConfig(string name)
    {
        var value = _configServ.GetConfigString(name);
        return IsPlaceholder(value) ? null : value;
    }

    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("__CONFIGURE_", StringComparison.Ordinal);

    private async Task SendPushAsync(int tenantId, int clientId, PushPayload payload)
    {
        var subject = GetWebPushConfig("webPushSubject");
        var publicKey = GetWebPushConfig("webPushPublicKey");
        var privateKey = GetWebPushConfig("webPushPrivateKey");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
            return;

        // Se carga primero la lista para no mantener una conexión SQL abierta mientras se realizan peticiones HTTP al proveedor push.
        // Las suscripciones que el proveedor marca como inexistentes se eliminan para evitar reintentos futuros.
        var subscriptions = new List<(long Id, string Endpoint, string P256dh, string Auth)>();
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(@"
SELECT id, endpoint, p256dh, auth
FROM patient_push_subscriptions
WHERE tenant_id = @tenant AND client_id = @client;", connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("client", clientId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                subscriptions.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        if (subscriptions.Count == 0) return;

        // Las credenciales VAPID se leen una sola vez por lote; no se consulta configuración ni se crea el cliente HTTP por suscripción.
        var vapid = new VapidDetails(subject, publicKey, privateKey);
        var webPush = new WebPushClient();
        var json = JsonSerializer.Serialize(new { title = payload.Title, body = payload.Body, url = payload.ActionUrl ?? "/patient" });

        foreach (var item in subscriptions)
        {
            try
            {
                await webPush.SendNotificationAsync(new PushSubscription(item.Endpoint, item.P256dh, item.Auth), json, vapid);
            }
            catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await DeleteSubscriptionAsync(item.Id);
            }
            catch (Exception ex)
            {
                // Un fallo puntual de push no invalida la notificación persistida ni debe bloquear otras suscripciones.
                // El portal sigue siendo el canal durable aunque el proveedor push esté temporalmente degradado.
                _logger.LogWarning(ex, "No se pudo enviar push al paciente {ClientId}.", clientId);
            }
        }
    }

    private async Task DeleteSubscriptionAsync(long id)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("DELETE FROM patient_push_subscriptions WHERE id = @id;", connection);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo eliminar una suscripción push caducada.");
        }
    }
}

public sealed record PushPayload(string Title, string Body, string? ActionUrl);

public sealed class PatientNotificationDto
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public sealed class PushSubscriptionDto
{
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

public sealed record PatientCommunicationPreferences(bool InAppEnabled, bool EmailEnabled, bool PushEnabled);
