using System.Text.Json;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebPush;

namespace Anguloso.Server.Logica;

/// <summary>Registro y envío de Web Push para usuarios profesionales.</summary>
public sealed class ProfessionalPushService
{
    private readonly string _connectionString;
    private readonly ConfigServ _config;
    private readonly ILogger<ProfessionalPushService> _logger;

    public ProfessionalPushService(IConfiguration configuration, ConfigServ config, ILogger<ProfessionalPushService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
        _config = config;
        _logger = logger;
    }

    public string? GetVapidPublicKey()
    {
        var value = _config.GetConfigString("webPushPublicKey");
        return string.IsNullOrWhiteSpace(value) || value.StartsWith("__CONFIGURE_", StringComparison.Ordinal) ? null : value;
    }

    public async Task RegisterAsync(int tenantId, int userId, PushSubscriptionDto subscription, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subscription.Endpoint) || string.IsNullOrWhiteSpace(subscription.P256dh) || string.IsNullOrWhiteSpace(subscription.Auth))
            throw new ArgumentException("La suscripción push no es válida.");
        if (subscription.Endpoint.Length > 2000 || subscription.P256dh.Length > 500 || subscription.Auth.Length > 500)
            throw new ArgumentException("La suscripción push supera los límites permitidos.");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO professional_push_subscriptions(tenant_id,user_id,endpoint,p256dh,auth,created_at,updated_at)
            VALUES(@tenant,@user,@endpoint,@p256dh,@auth,NOW(),NOW())
            ON CONFLICT(endpoint) DO UPDATE SET tenant_id=EXCLUDED.tenant_id,user_id=EXCLUDED.user_id,
                p256dh=EXCLUDED.p256dh,auth=EXCLUDED.auth,updated_at=NOW();
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("endpoint", subscription.Endpoint);
        command.Parameters.AddWithValue("p256dh", subscription.P256dh);
        command.Parameters.AddWithValue("auth", subscription.Auth);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(int tenantId, int userId, string endpoint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("DELETE FROM professional_push_subscriptions WHERE tenant_id=@tenant AND user_id=@user AND endpoint=@endpoint;", connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("endpoint", endpoint);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SendAppointmentReminderAsync(int tenantId, int userId, int appointmentId, string clientName, DateTime startsAtUtc, CancellationToken ct = default)
    {
        var subject = _config.GetConfigString("webPushSubject");
        var publicKey = GetVapidPublicKey();
        var privateKey = _config.GetConfigString("webPushPrivateKey");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey) ||
            subject.StartsWith("__CONFIGURE_", StringComparison.Ordinal) || privateKey.StartsWith("__CONFIGURE_", StringComparison.Ordinal))
        {
            _logger.LogWarning("No se envía push de recordatorio al profesional {UserId}: VAPID no está configurado.", userId);
            return;
        }

        var subscriptions = new List<(long Id, string Endpoint, string P256dh, string Auth)>();
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT id,endpoint,p256dh,auth FROM professional_push_subscriptions WHERE tenant_id=@tenant AND user_id=@user;", connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("user", userId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) subscriptions.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        if (subscriptions.Count == 0) return;
        var webPush = new WebPushClient();
        var vapid = new VapidDetails(subject, publicKey, privateKey);
        var localTime = startsAtUtc.ToString("HH:mm 'UTC'");
        var json = JsonSerializer.Serialize(new { title = "Cita en 15 minutos", body = $"Tienes una cita con {clientName} a las {localTime}.", url = $"/appointments" });
        foreach (var item in subscriptions)
        {
            try
            {
                await webPush.SendNotificationAsync(new PushSubscription(item.Endpoint, item.P256dh, item.Auth), json, vapid);
            }
            catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await RemoveAsync(tenantId, userId, item.Endpoint, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo enviar recordatorio push al profesional {UserId} para la cita {AppointmentId}.", userId, appointmentId);
            }
        }
    }
}
