using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Logica;

/// <summary>
/// Núcleo persistente de automatizaciones. Los eventos y trabajos sobreviven a reinicios.
/// Las claves de idempotencia se imponen también en PostgreSQL.
/// </summary>
public sealed class AutomationService
{
    private readonly string _connectionString;
    private readonly ILogger<AutomationService> _logger;

    public AutomationService(IConfiguration configuration, ILogger<AutomationService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
        _logger = logger;
    }

    public async Task<long?> PublishEventAsync(
        int tenantId,
        string eventType,
        string aggregateType,
        string? aggregateId,
        object payload,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0) throw new ArgumentOutOfRangeException(nameof(tenantId));
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("El tipo de evento es obligatorio.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("La clave de idempotencia es obligatoria.", nameof(idempotencyKey));
        if (idempotencyKey.Length > 255) throw new ArgumentException("La clave de idempotencia es demasiado larga.", nameof(idempotencyKey));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        const string sql = """
            INSERT INTO automation_events
                (tenant_id, event_type, aggregate_type, aggregate_id, payload, idempotency_key, occurred_at)
            VALUES
                (@tenant, @event_type, @aggregate_type, @aggregate_id, @payload, @idempotency_key, NOW())
            ON CONFLICT (tenant_id, idempotency_key) DO NOTHING
            RETURNING id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, tx);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("aggregate_type", aggregateType);
        command.Parameters.AddWithValue("aggregate_id", (object?)aggregateId ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = AutomationJson.Serialize(payload) });
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null)
        {
            await tx.CommitAsync(cancellationToken);
            return null;
        }

        var eventId = Convert.ToInt64(result);
        await tx.CommitAsync(cancellationToken);

        await ScheduleBuiltInRulesAsync(
            new AutomationEvent(eventId, tenantId, eventType, aggregateType, aggregateId, AutomationJson.Serialize(payload), DateTime.UtcNow),
            cancellationToken);

        return eventId;
    }

    public async Task<long> ScheduleActionAsync(
        int tenantId,
        string actionType,
        object payload,
        DateTime scheduledAt,
        long? eventId = null,
        string? idempotencyKey = null,
        int maxAttempts = 5,
        CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0) throw new ArgumentOutOfRangeException(nameof(tenantId));
        if (maxAttempts < 1 || maxAttempts > 20) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO automation_jobs
                (tenant_id, event_id, action_type, payload, scheduled_at, status, attempts, max_attempts, idempotency_key, created_at)
            VALUES
                (@tenant, @event_id, @action_type, @payload, @scheduled_at, 'pending', 0, @max_attempts, @idempotency_key, NOW())
            ON CONFLICT (tenant_id, idempotency_key) WHERE idempotency_key IS NOT NULL
            DO NOTHING
            RETURNING id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("event_id", (object?)eventId ?? DBNull.Value);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = AutomationJson.Serialize(payload) });
        command.Parameters.AddWithValue("scheduled_at", scheduledAt);
        command.Parameters.AddWithValue("max_attempts", maxAttempts);
        command.Parameters.AddWithValue("idempotency_key", (object?)idempotencyKey ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null)
        {
            if (idempotencyKey is null) throw new InvalidOperationException("No se pudo crear la tarea de automatización.");
            await using var lookup = new NpgsqlCommand(
                "SELECT id FROM automation_jobs WHERE tenant_id=@tenant AND idempotency_key=@key;",
                connection);
            lookup.Parameters.AddWithValue("tenant", tenantId);
            lookup.Parameters.AddWithValue("key", idempotencyKey);
            return Convert.ToInt64(await lookup.ExecuteScalarAsync(cancellationToken));
        }

        return Convert.ToInt64(result);
    }

    public async Task<long> CreateProfessionalTaskAsync(
        int tenantId,
        ProfessionalTaskCreateRequest request,
        string source,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 250)
            throw new ArgumentException("El título de la tarea es obligatorio y no puede superar 250 caracteres.");
        if (request.Description?.Length > 4000)
            throw new ArgumentException("La descripción no puede superar 4000 caracteres.");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO professional_tasks
                (tenant_id, client_id, assigned_user_id, title, description, due_at, priority, status, source, idempotency_key, created_at)
            VALUES
                (@tenant, @client, @user, @title, @description, @due_at, @priority, 'open', @source, @idempotency_key, NOW())
            ON CONFLICT (tenant_id, idempotency_key) WHERE idempotency_key IS NOT NULL
            DO NOTHING
            RETURNING id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", (object?)request.ClientId ?? DBNull.Value);
        command.Parameters.AddWithValue("user", (object?)request.AssignedUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("title", request.Title.Trim());
        command.Parameters.AddWithValue("description", (object?)request.Description?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("due_at", (object?)request.DueAt ?? DBNull.Value);
        command.Parameters.AddWithValue("priority", NormalizePriority(request.Priority));
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("idempotency_key", (object?)idempotencyKey ?? DBNull.Value);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not null) return Convert.ToInt64(result);
        if (idempotencyKey is null) throw new InvalidOperationException("No se pudo crear la tarea profesional.");

        await using var lookup = new NpgsqlCommand(
            "SELECT id FROM professional_tasks WHERE tenant_id=@tenant AND idempotency_key=@key;",
            connection);
        lookup.Parameters.AddWithValue("tenant", tenantId);
        lookup.Parameters.AddWithValue("key", idempotencyKey);
        return Convert.ToInt64(await lookup.ExecuteScalarAsync(cancellationToken));
    }

    private async Task ScheduleBuiltInRulesAsync(AutomationEvent evt, CancellationToken cancellationToken)
    {
        switch (evt.EventType)
        {
            case "client.created":
                {
                    var payload = AutomationJson.Deserialize<ClientCreatedPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para client.created.");
                    var due = DateTime.UtcNow.AddDays(1);
                    await ScheduleActionAsync(
                        evt.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(payload.ClientId, payload.NutritionistId,
                            "Completar ficha del paciente",
                            "Revisar la información inicial y completar los datos pendientes del paciente.",
                            due, "normal", "automation:client.created"),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:create-professional-task",
                        cancellationToken: cancellationToken);
                    break;
                }
            case "patient.checkin.submitted":
                {
                    var payload = AutomationJson.Deserialize<CheckinSubmittedPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para patient.checkin.submitted.");
                    await ScheduleActionAsync(
                        evt.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(payload.ClientId, payload.NutritionistId,
                            "Revisar check-in semanal",
                            "El paciente ha enviado un nuevo check-in semanal.",
                            DateTime.UtcNow.AddHours(24), "high", "automation:patient.checkin.submitted"),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:create-professional-task",
                        cancellationToken: cancellationToken);
                    break;
                }
            case "appointment.completed":
                {
                    var payload = AutomationJson.Deserialize<AppointmentCompletedPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para appointment.completed.");
                    await ScheduleActionAsync(
                        evt.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(payload.ClientId, payload.NutritionistId,
                            "Preparar seguimiento de la cita",
                            "Revisar la cita completada y preparar la siguiente acción de seguimiento.",
                            DateTime.UtcNow.AddHours(24), "normal", "automation:appointment.completed"),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:create-professional-task",
                        cancellationToken: cancellationToken);
                    break;
                }
        }
    }

    private static string NormalizePriority(string? priority) =>
        priority?.Trim().ToLowerInvariant() switch
        {
            "low" => "low",
            "high" => "high",
            "urgent" => "urgent",
            _ => "normal"
        };

    public sealed record ClientCreatedPayload(int ClientId, int? NutritionistId);
    public sealed record CheckinSubmittedPayload(int ClientId, int? NutritionistId);
    public sealed record AppointmentCompletedPayload(int AppointmentId, int ClientId, int? NutritionistId);
}
