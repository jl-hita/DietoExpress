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

        var publishedEvent = new AutomationEvent(
            eventId, tenantId, eventType, aggregateType, aggregateId,
            AutomationJson.Serialize(payload), DateTime.UtcNow);

        await UpdatePatientLifecycleFromEventAsync(publishedEvent, cancellationToken);
        await ScheduleBuiltInRulesAsync(publishedEvent, cancellationToken);

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

    /// <summary>Actualiza el estado operativo del paciente a partir de eventos de negocio.</summary>
    private async Task UpdatePatientLifecycleFromEventAsync(AutomationEvent evt, CancellationToken cancellationToken)
    {
        int? clientId = null;
        string? status = null;
        switch (evt.EventType)
        {
            case "client.created":
                clientId = AutomationJson.Deserialize<ClientCreatedPayload>(evt.Payload)?.ClientId;
                status = "pending_info";
                break;
            case "patient.checkin.submitted":
                clientId = AutomationJson.Deserialize<CheckinSubmittedPayload>(evt.Payload)?.ClientId;
                status = "follow_up";
                break;
            case "appointment.completed":
                clientId = AutomationJson.Deserialize<AppointmentCompletedPayload>(evt.Payload)?.ClientId;
                status = "active";
                break;
            case "appointment.confirmed":
                {
                    var payload = AutomationJson.Deserialize<AppointmentStatusPayload>(evt.Payload);
                    clientId = payload?.ClientId;
                    if (clientId.HasValue)
                        status = await HasCompletedAppointmentAsync(evt.TenantId, clientId.Value, cancellationToken)
                            ? "active" : "pending_first_appointment";
                    break;
                }
            case "appointment.cancelled":
            case "appointment.no_show":
                {
                    var payload = AutomationJson.Deserialize<AppointmentStatusPayload>(evt.Payload);
                    clientId = payload?.ClientId;
                    if (clientId.HasValue && !await HasCompletedAppointmentAsync(evt.TenantId, clientId.Value, cancellationToken))
                        status = "pending_first_appointment";
                    break;
                }
        }
        if (clientId.HasValue && status is not null)
            await SetPatientLifecycleStatusAsync(evt.TenantId, clientId.Value, status, DateTime.UtcNow, cancellationToken);
    }

    /// <summary>Recalcula periódicamente el ciclo de vida y crea tareas para pacientes sin seguimiento.</summary>
    public async Task RunPatientLifecycleSweepAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<(int ClientId, int TenantId, int? AssignedUserId, string Status)>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("""
            SELECT c.id, c.tenant_id, c.user_id,
                   CASE
                     WHEN c.archived_at IS NOT NULL THEN 'archived'
                     WHEN c.birth_date IS NULL
                          OR NOT EXISTS (SELECT 1 FROM biometrics b WHERE b.client_id=c.id)
                       THEN 'pending_info'
                     WHEN EXISTS (SELECT 1 FROM patient_appointments a
                                   WHERE a.client_id=c.id AND a.tenant_id=c.tenant_id AND a.status='completed')
                          AND GREATEST(
                              COALESCE((SELECT MAX(a.starts_at) FROM patient_appointments a WHERE a.client_id=c.id AND a.tenant_id=c.tenant_id AND a.status='completed'), TIMESTAMPTZ '1970-01-01'),
                              COALESCE((SELECT MAX(COALESCE(pc.submitted_at, pc.created_at)) FROM patient_checkins pc WHERE pc.client_id=c.id), TIMESTAMPTZ '1970-01-01')
                          ) < NOW() - INTERVAL '30 days'
                       THEN 'no_recent_followup'
                     WHEN EXISTS (SELECT 1 FROM patient_appointments a
                                   WHERE a.client_id=c.id AND a.tenant_id=c.tenant_id AND a.status='completed'
                                     AND a.starts_at >= NOW() - INTERVAL '30 days')
                          OR EXISTS (SELECT 1 FROM patient_checkins pc
                                     WHERE pc.client_id=c.id
                                       AND COALESCE(pc.submitted_at, pc.created_at) >= NOW() - INTERVAL '14 days')
                       THEN 'follow_up'
                     ELSE 'pending_first_appointment'
                   END AS lifecycle_status
            FROM clients c
            WHERE c.tenant_id IS NOT NULL AND c.archived_at IS NULL;
            """, connection);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
            candidates.Add((reader.GetInt32(0),reader.GetInt32(1),reader.IsDBNull(2)?null:reader.GetInt32(2),reader.GetString(3)));
        await reader.DisposeAsync();

        foreach(var c in candidates)
        {
            await SetPatientLifecycleStatusAsync(c.TenantId,c.ClientId,c.Status,DateTime.UtcNow,cancellationToken);
            if(c.Status=="no_recent_followup")
                await ScheduleActionAsync(c.TenantId,"create_professional_task",
                    new CreateTaskAction(c.ClientId,c.AssignedUserId,
                        "Contactar paciente sin seguimiento reciente",
                        "El paciente lleva más de 30 días sin una actividad de seguimiento reciente.",
                        DateTime.UtcNow.AddDays(1),"high","automation:patient.lifecycle"),
                    DateTime.UtcNow,null,
                    $"lifecycle:inactive:{c.ClientId}:{DateTime.UtcNow:yyyyMMdd}",
                    cancellationToken:cancellationToken);
        }
    }

    private async Task<bool> HasCompletedAppointmentAsync(int tenantId,int clientId,CancellationToken cancellationToken)
    {
        await using var connection=new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command=new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM patient_appointments
                          WHERE tenant_id=@tenant AND client_id=@client AND status='completed');
            """,connection);
        command.Parameters.AddWithValue("tenant",tenantId);
        command.Parameters.AddWithValue("client",clientId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task SetPatientLifecycleStatusAsync(int tenantId,int clientId,string status,DateTime changedAt,CancellationToken cancellationToken)
    {
        string[] allowed=["pending_info","pending_first_appointment","active","follow_up","no_recent_followup","archived"];
        if(!allowed.Contains(status)) throw new ArgumentException("Estado de ciclo de vida no válido.",nameof(status));
        await using var connection=new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command=new NpgsqlCommand("""
            UPDATE clients
            SET lifecycle_status=@status,
                lifecycle_status_changed_at=CASE WHEN lifecycle_status IS DISTINCT FROM @status THEN @changed ELSE lifecycle_status_changed_at END,
                last_activity_at=CASE WHEN @status IN ('active','follow_up') THEN @changed ELSE last_activity_at END
            WHERE id=@client AND tenant_id=@tenant;
            """,connection);
        command.Parameters.AddWithValue("status",status);
        command.Parameters.AddWithValue("changed",changedAt);
        command.Parameters.AddWithValue("client",clientId);
        command.Parameters.AddWithValue("tenant",tenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            case "appointment.confirmed":
                {
                    var payload = AutomationJson.Deserialize<AppointmentStatusPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para appointment.confirmed.");

                    var firstReminder = payload.StartsAtUtc.AddHours(-24);
                    if (firstReminder < DateTime.UtcNow) firstReminder = DateTime.UtcNow;

                    await ScheduleActionAsync(
                        evt.TenantId,
                        "notify_patient",
                        new NotifyPatientAction(
                            payload.ClientId,
                            "appointment_reminder",
                            "Recordatorio de cita",
                            "Recuerda que tienes una cita con tu nutricionista mañana.",
                            "/patient?tab=appointments"),
                        firstReminder,
                        evt.Id,
                        $"event:{evt.Id}:reminder-24h",
                        cancellationToken: cancellationToken);

                    var secondReminder = payload.StartsAtUtc.AddHours(-2);
                    if (secondReminder > DateTime.UtcNow)
                    {
                        await ScheduleActionAsync(
                            evt.TenantId,
                            "notify_patient",
                            new NotifyPatientAction(
                                payload.ClientId,
                                "appointment_reminder",
                                "Tu cita es en 2 horas",
                                "Recuerda que tienes una cita con tu nutricionista dentro de 2 horas.",
                                "/patient?tab=appointments"),
                            secondReminder,
                            evt.Id,
                            $"event:{evt.Id}:reminder-2h",
                            cancellationToken: cancellationToken);
                    }
                    break;
                }
            case "appointment.cancelled":
                {
                    await CancelJobsForEventAggregateAsync(evt, cancellationToken);
                    break;
                }
            case "appointment.no_show":
                {
                    var payload = AutomationJson.Deserialize<AppointmentStatusPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para appointment.no_show.");
                    await ScheduleActionAsync(
                        evt.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(payload.ClientId, payload.NutritionistId,
                            "Contactar paciente por ausencia a la cita",
                            "La cita ha quedado marcada como no presentada.",
                            DateTime.UtcNow.AddHours(24), "high", "automation:appointment.no_show"),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:create-professional-task",
                        cancellationToken: cancellationToken);
                    break;
                }
        }
    }

    private async Task CancelJobsForEventAggregateAsync(AutomationEvent evt, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE automation_jobs j
            SET status='cancelled', last_error='Evento de origen cancelado', updated_at=NOW()
            WHERE j.tenant_id=@tenant
              AND j.status='pending'
              AND EXISTS (
                  SELECT 1 FROM automation_events e
                  WHERE e.id=j.event_id
                    AND e.tenant_id=@tenant
                    AND e.aggregate_type=@aggregate_type
                    AND e.aggregate_id=@aggregate_id
                    AND e.event_type='appointment.confirmed'
              );
            """, connection);
        command.Parameters.AddWithValue("tenant", evt.TenantId);
        command.Parameters.AddWithValue("aggregate_type", evt.AggregateType);
        command.Parameters.AddWithValue("aggregate_id", evt.AggregateId ?? "");
        await command.ExecuteNonQueryAsync(cancellationToken);
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
    public sealed record AppointmentStatusPayload(int AppointmentId, int ClientId, int? NutritionistId, DateTime StartsAtUtc);
}
