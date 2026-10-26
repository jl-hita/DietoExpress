using System.Text.Json;
using Anguloso.Server.Logica;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anguloso.Server.Logica;

/// <summary>
/// Persiste efectos secundarios en la misma transacción que el cambio de negocio.
/// El consumidor es at-least-once; cada efecto debe usar una clave estable.
/// </summary>
internal static class TransactionalOutbox
{
    public static Task EnqueueAsync(
        Anguloso.Server.Models.angulosodbContext db,
        int tenantId,
        string eventType,
        object payload,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(payload);
        return db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO integration_outbox
    (tenant_id, event_type, payload, idempotency_key, status, attempts, max_attempts, available_at, created_at)
VALUES
    ({tenantId}, {eventType}, CAST({json} AS jsonb), {idempotencyKey}, 'pending', 0, 10, NOW(), NOW())
ON CONFLICT (tenant_id, idempotency_key) DO NOTHING;", cancellationToken);
    }
}

public sealed class TransactionalOutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _connectionString;
    private readonly ILogger<TransactionalOutboxWorker> _logger;

    public TransactionalOutboxWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TransactionalOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TransactionalOutboxWorker iniciado.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await ClaimNextAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                try
                {
                    await ProcessAsync(job, stoppingToken);
                    await MarkCompletedAsync(job.Id, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await MarkFailedOrRetryAsync(job, ex, stoppingToken);
                    _logger.LogError(ex, "Falló el efecto outbox {OutboxId} ({EventType}), intento {Attempt}/{MaxAttempts}.",
                        job.Id, job.EventType, job.Attempts, job.MaxAttempts);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando la cola transaccional outbox.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<OutboxJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using (var recover = new NpgsqlCommand("""
            UPDATE integration_outbox
            SET status = CASE WHEN attempts >= max_attempts THEN 'failed' ELSE 'pending' END,
                locked_at = NULL,
                available_at = CASE WHEN attempts >= max_attempts THEN available_at ELSE NOW() END,
                last_error = CASE WHEN attempts >= max_attempts
                    THEN COALESCE(last_error, 'El worker quedó huérfano tras agotar los intentos.')
                    ELSE last_error END,
                updated_at = NOW()
            WHERE status = 'processing'
              AND locked_at < NOW() - INTERVAL '10 minutes';
            """, connection, tx))
        {
            await recover.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand("""
            SELECT id, tenant_id, event_type, payload::text, idempotency_key, attempts, max_attempts
            FROM integration_outbox
            WHERE status = 'pending' AND available_at <= NOW()
            ORDER BY id
            FOR UPDATE SKIP LOCKED
            LIMIT 1;
            """, connection, tx);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await tx.CommitAsync(cancellationToken);
            return null;
        }

        var job = new OutboxJob(
            reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetInt32(5) + 1,
            reader.GetInt32(6));
        await reader.DisposeAsync();

        await using var update = new NpgsqlCommand("""
            UPDATE integration_outbox
            SET status='processing', attempts=attempts+1, locked_at=NOW(), updated_at=NOW()
            WHERE id=@id AND status='pending';
            """, connection, tx);
        update.Parameters.AddWithValue("id", job.Id);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await tx.RollbackAsync(cancellationToken);
            return null;
        }

        await tx.CommitAsync(cancellationToken);
        return job;
    }

    private async Task ProcessAsync(OutboxJob job, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(job.Payload);
        switch (job.EventType)
        {
            case "patient_checkin_submitted":
            {
                var clientId = payload.RootElement.GetProperty("ClientId").GetInt32();
                var nutritionistIdElement = payload.RootElement.GetProperty("NutritionistId");
                int? nutritionistId = nutritionistIdElement.ValueKind == JsonValueKind.Null
                    ? null : nutritionistIdElement.GetInt32();
                var checkinId = payload.RootElement.GetProperty("CheckinId").GetInt64();
                var submittedAtTicks = payload.RootElement.GetProperty("SubmittedAtTicks").GetInt64();

                using var scope = _scopeFactory.CreateScope();
                var automation = scope.ServiceProvider.GetRequiredService<AutomationService>();
                await automation.PublishEventAsync(
                    job.TenantId, "patient.checkin.submitted", "patient_checkin",
                    checkinId.ToString(),
                    new AutomationService.CheckinSubmittedPayload(clientId, nutritionistId),
                    $"checkin:{checkinId}:submitted:{submittedAtTicks}", cancellationToken);
                return;
            }
            case "patient_message_created":
            {
                var clientId = payload.RootElement.GetProperty("ClientId").GetInt32();
                var senderElement = payload.RootElement.GetProperty("SenderUserId");
                int? senderUserId = senderElement.ValueKind == JsonValueKind.Null ? null : senderElement.GetInt32();
                var patientName = payload.RootElement.GetProperty("PatientName").GetString() ?? "Paciente";
                var body = payload.RootElement.GetProperty("Body").GetString() ?? string.Empty;
                using var scope = _scopeFactory.CreateScope();

                if (senderUserId.HasValue)
                {
                    var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                    await notifications.CreateForPatientAsync(
                        job.TenantId, clientId, "message", "Nuevo mensaje de tu nutricionista",
                        body.Length > 180 ? body[..180] + "…" : body,
                        "/patient?tab=messages",
                        idempotencyKey: $"outbox:{job.Id}:patient-notification");
                    return;
                }

                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand("""
                    SELECT u.email
                    FROM client_nutritionist_assignments a
                    JOIN users u ON u.id = a.nutritionist_id
                    WHERE a.client_id=@client AND a.is_active AND u.tenant_id=@tenant
                    ORDER BY a.assigned_at DESC
                    LIMIT 1;
                    """, connection);
                command.Parameters.AddWithValue("client", clientId);
                command.Parameters.AddWithValue("tenant", job.TenantId);
                var email = await command.ExecuteScalarAsync(cancellationToken) as string;
                if (string.IsNullOrWhiteSpace(email))
                {
                    var professionalPush = scope.ServiceProvider.GetRequiredService<ProfessionalPushService>();
                    await professionalPush.SendNotificationAsync(job.TenantId, await GetAssignedProfessionalIdAsync(clientId, job.TenantId, cancellationToken),
                        $"Nuevo mensaje de {patientName}", body.Length > 180 ? body[..180] + "…" : body, "/messages", cancellationToken);
                    _logger.LogWarning("No hay email profesional disponible para el mensaje de paciente {ClientId}; se ha intentado el push.", clientId);
                    return;
                }

                var safePatient = System.Net.WebUtility.HtmlEncode(patientName);
                var safeBody = System.Net.WebUtility.HtmlEncode(body);
                var emailServ = scope.ServiceProvider.GetRequiredService<EmailServ>();
                var result = await emailServ.SendEmailAsync(
                    email, $"Nuevo mensaje de {patientName}",
                    $"<h2>Nuevo mensaje de {safePatient}</h2><p>{safeBody}</p><p>Entra en DietoExpress para responder al paciente.</p>",
                    $"outbox:{job.Id}:professional-email");
                if (!result.Exito) throw new InvalidOperationException(result.Mensaje);
                var push = scope.ServiceProvider.GetRequiredService<ProfessionalPushService>();
                await push.SendNotificationAsync(job.TenantId, await GetAssignedProfessionalIdAsync(clientId, job.TenantId, cancellationToken),
                    $"Nuevo mensaje de {patientName}", body.Length > 180 ? body[..180] + "…" : body, "/messages", cancellationToken);
                return;
            }
            default:
                throw new InvalidOperationException($"Tipo de evento outbox no soportado: {job.EventType}");
        }
    }

    private async Task<int> GetAssignedProfessionalIdAsync(int clientId, int tenantId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT a.nutritionist_id
            FROM client_nutritionist_assignments a
            JOIN users u ON u.id=a.nutritionist_id AND u.tenant_id=@tenant
            WHERE a.client_id=@client AND a.is_active
            ORDER BY a.assigned_at DESC LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("tenant", tenantId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null ? throw new InvalidOperationException("No hay nutricionista asignado para el mensaje.") : Convert.ToInt32(value);
    }

    private async Task MarkCompletedAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE integration_outbox
            SET status='completed', processed_at=NOW(), locked_at=NULL, updated_at=NOW(), last_error=NULL
            WHERE id=@id;
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkFailedOrRetryAsync(OutboxJob job, Exception exception, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var failed = job.Attempts >= job.MaxAttempts;
        var delaySeconds = Math.Min(3600, 5 * (int)Math.Pow(2, Math.Min(job.Attempts - 1, 9)));
        await using var command = new NpgsqlCommand("""
            UPDATE integration_outbox
            SET status=@status,
                available_at=CASE WHEN @failed THEN available_at ELSE NOW() + (@delay * INTERVAL '1 second') END,
                locked_at=NULL, last_error=@error, updated_at=NOW()
            WHERE id=@id;
            """, connection);
        command.Parameters.AddWithValue("status", failed ? "failed" : "pending");
        command.Parameters.AddWithValue("failed", failed);
        command.Parameters.AddWithValue("delay", delaySeconds);
        command.Parameters.AddWithValue("error", exception.ToString().Length > 4000 ? exception.ToString()[..4000] : exception.ToString());
        command.Parameters.AddWithValue("id", job.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record OutboxJob(long Id, int TenantId, string EventType, string Payload,
        string IdempotencyKey, int Attempts, int MaxAttempts);
}
