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

    // Persiste primero el evento y, solo si se inserta por primera vez, ejecuta las reglas derivadas.
    // La restricción UNIQUE de PostgreSQL evita duplicados incluso con peticiones concurrentes.
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

        // El evento ya está comprometido antes de ejecutar las reglas derivadas: un fallo de una regla
        // no puede deshacer la publicación original y el evento persistido puede procesarse de nuevo.
        await UpdatePatientLifecycleFromEventAsync(publishedEvent, cancellationToken);
        await ScheduleBuiltInRulesAsync(publishedEvent, cancellationToken);

        return eventId;
    }

    // Los trabajos se guardan en PostgreSQL, no en memoria: sobreviven a reinicios y pueden ser
    // reclamados por el worker con control de concurrencia e idempotencia.
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
            case "patient.onboarding.completed":
                {
                    var payload = AutomationJson.Deserialize<ClientOnboardingCompletedPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para patient.onboarding.completed.");
                    await CancelPendingJobsByIdempotencyPrefixAsync(
                        evt.TenantId,
                        $"onboarding:info-reminder:{payload.ClientId}:",
                        cancellationToken);
                    await CancelPendingJobsByIdempotencyPrefixAsync(
                        evt.TenantId,
                        $"onboarding:info-task:{payload.ClientId}:",
                        cancellationToken);
                    break;
                }
            case "patient.checkin.submitted":
                clientId = AutomationJson.Deserialize<CheckinSubmittedPayload>(evt.Payload)?.ClientId;
                status = "follow_up";
                break;
            case "diet.published":
            case "diet.changed":
                {
                    var payload = AutomationJson.Deserialize<DietAutomationPayload>(evt.Payload)
                        ?? throw new InvalidOperationException($"Payload inválido para {evt.EventType}.");
                    await ScheduleActionAsync(
                        evt.TenantId,
                        "notify_patient",
                        new NotifyPatientAction(
                            payload.ClientId,
                            evt.EventType == "diet.published" ? "Nueva dieta disponible" : "Tu dieta ha sido actualizada",
                            evt.EventType == "diet.published"
                                ? $"Tu nutricionista ha publicado la dieta \"{payload.DietName}\"."
                                : $"Tu nutricionista ha actualizado la dieta \"{payload.DietName}\".",
                            evt.EventType == "diet.published"
                                ? "Ya puedes consultarla desde tu portal."
                                : "Consulta los cambios desde tu portal.",
                            "/patient?tab=diet"),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:patient-notification",
                        cancellationToken: cancellationToken);
                    break;
                }
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
                    if (payload is not null)
                        await CancelPendingJobsByIdempotencyPrefixAsync(
                            evt.TenantId,
                            $"onboarding:first-appointment-reminder:{payload.ClientId}:",
                            cancellationToken);

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


    /// <summary>
    /// Reconciliación del onboarding del paciente: recuerda información/consentimiento pendientes
    /// y, cuando la ficha está completa, guía hacia la primera cita. Los hechos persistidos son
    /// la fuente de verdad; por eso los recordatorios dejan de generarse automáticamente al resolverse.
    /// </summary>
    public async Task RunPatientOnboardingAutomationSweepAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<(int ClientId, int TenantId, int? AssignedUserId, bool InfoComplete, bool HasFutureAppointment, DateTime CreatedAt)>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT c.id,
                   c.tenant_id,
                   c.user_id,
                   (
                       c.birth_date IS NOT NULL
                       AND NULLIF(TRIM(c.gender), '') IS NOT NULL
                       AND EXISTS (
                           SELECT 1 FROM biometrics b
                           WHERE b.client_id=c.id
                             AND b.measurement_date IS NOT NULL
                       )
                       AND c.onboarding_consent_at IS NOT NULL
                   ) AS info_complete,
                   EXISTS (
                       SELECT 1 FROM patient_appointments a
                       WHERE a.client_id=c.id
                         AND a.tenant_id=c.tenant_id
                         AND a.status IN ('requested','confirmed')
                         AND a.starts_at > NOW()
                   ) AS has_future_appointment,
                   COALESCE(c.created_at, NOW()) AS created_at
            FROM clients c
            WHERE c.tenant_id IS NOT NULL
              AND c.archived_at IS NULL;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add((
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetDateTime(5)));
        }

        foreach (var c in candidates)
        {
            var now = DateTime.UtcNow;
            if (!c.InfoComplete)
            {
                await ScheduleActionAsync(
                    c.TenantId,
                    "notify_patient",
                    new NotifyPatientAction(
                        c.ClientId,
                        "onboarding_info_reminder",
                        "Completa tu información inicial",
                        "Faltan algunos datos de tu ficha inicial. Completa la información solicitada en tu portal para que tu nutricionista pueda preparar tu seguimiento.",
                        "/patient?tab=profile"),
                    now,
                    null,
                    $"onboarding:info-reminder:{c.ClientId}:{now:yyyyMMdd}",
                    cancellationToken: cancellationToken);

                if (c.CreatedAt <= now.AddDays(-3))
                {
                    await ScheduleActionAsync(
                        c.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(
                            c.ClientId,
                            c.AssignedUserId,
                            "Revisar información inicial pendiente",
                            "El paciente todavía no ha completado los datos iniciales y/o el consentimiento del portal.",
                            now.AddDays(1),
                            "normal",
                            "automation:onboarding.info"),
                        now,
                        null,
                        $"onboarding:info-task:{c.ClientId}:{now:yyyyMMdd}",
                        cancellationToken: cancellationToken);
                }

                continue;
            }

            if (!c.HasFutureAppointment)
            {
                await ScheduleActionAsync(
                    c.TenantId,
                    "notify_patient",
                    new NotifyPatientAction(
                        c.ClientId,
                        "first_appointment_reminder",
                        "Ya puedes reservar tu primera cita",
                        "Tu ficha inicial está completa. Reserva tu primera cita con tu nutricionista desde el portal.",
                        "/patient?tab=appointments"),
                    now,
                    null,
                    $"onboarding:first-appointment-reminder:{c.ClientId}:{now:yyyyMMdd}",
                    cancellationToken: cancellationToken);

                if (c.CreatedAt <= now.AddDays(-3))
                {
                    await ScheduleActionAsync(
                        c.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(
                            c.ClientId,
                            c.AssignedUserId,
                            "Proponer primera cita",
                            "El paciente ha completado la información inicial pero todavía no tiene una cita futura solicitada o confirmada.",
                            now.AddDays(1),
                            "normal",
                            "automation:onboarding.first-appointment"),
                        now,
                        null,
                        $"onboarding:first-appointment-task:{c.ClientId}:{now:yyyyMMdd}",
                        cancellationToken: cancellationToken);
                }
            }
        }
    }

    /// <summary>Programa recordatorios persistentes de check-in y tareas de seguimiento.</summary>
    // Recorre periódicamente los pacientes activos para convertir la falta de seguimiento en
    // recordatorios para el paciente y tareas para el profesional, usando idempotencia por semana.
    public async Task RunFollowUpAutomationSweepAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<(int ClientId, int TenantId, int? AssignedUserId, DateTime? LastCheckin)>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT c.id,
                   c.tenant_id,
                   c.user_id,
                   (SELECT MAX(pc.submitted_at)
                      FROM patient_checkins pc
                     WHERE pc.client_id=c.id
                       AND pc.tenant_id=c.tenant_id) AS last_checkin
            FROM clients c
            WHERE c.tenant_id IS NOT NULL
              AND c.archived_at IS NULL
              AND c.lifecycle_status IN ('active','follow_up')
              AND NOT EXISTS (
                  SELECT 1
                    FROM patient_appointments a
                   WHERE a.client_id=c.id
                     AND a.tenant_id=c.tenant_id
                     AND a.status IN ('cancelled','no_show')
                     AND a.starts_at > NOW() - INTERVAL '7 days'
              );
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add((
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
        }

        foreach (var c in candidates)
        {
            var now = DateTime.UtcNow;
            var needsCheckin = !c.LastCheckin.HasValue || c.LastCheckin.Value < now.AddDays(-7);
            if (!needsCheckin) continue;

            var weekKey = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7)).ToString("yyyyMMdd");

            await ScheduleActionAsync(
                c.TenantId,
                "notify_patient",
                new NotifyPatientAction(
                    c.ClientId,
                    "checkin_reminder",
                    "Tienes un check-in pendiente",
                    "Completa tu check-in semanal para que tu nutricionista pueda revisar tu evolución.",
                    "/patient?tab=checkins"),
                now,
                null,
                $"followup:checkin-reminder:{c.ClientId}:{weekKey}",
                cancellationToken: cancellationToken);

            if (c.LastCheckin.HasValue && c.LastCheckin.Value < now.AddDays(-10))
            {
                await ScheduleActionAsync(
                    c.TenantId,
                    "create_professional_task",
                    new CreateTaskAction(
                        c.ClientId,
                        c.AssignedUserId,
                        "Revisar seguimiento pendiente",
                        "El paciente lleva más de 10 días sin enviar el check-in semanal.",
                        now.AddDays(1),
                        "normal",
                        "automation:followup.checkin"),
                    now,
                    null,
                    $"followup:checkin-task:{c.ClientId}:{weekKey}",
                    cancellationToken: cancellationToken);
            }
        }
    }

    /// <summary>Recalcula periódicamente el ciclo de vida y crea tareas para pacientes sin seguimiento.</summary>
    // Barrido periódico de reconciliación: aunque un evento no llegue a procesarse, el estado puede
    // reconstruirse desde los datos persistidos. Esto hace el ciclo de vida resistente a reinicios.
    // Recalcula el estado clínico-operativo del paciente a partir de actividad reciente.
    // Este barrido corrige estados que no hayan podido actualizarse por un evento puntual.
    public async Task RunPatientLifecycleSweepAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<(int ClientId, int TenantId, int? AssignedUserId, string Status, bool HasFutureAppointment)>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // El estado se deriva de hechos persistidos, no de un contador mantenido por los eventos.
        // Así el barrido puede reparar inconsistencias y volver a calcular el estado tras reinicios.
        await using var command = new NpgsqlCommand("""
            SELECT c.id, c.tenant_id, c.user_id,
                   EXISTS (
                       SELECT 1 FROM patient_appointments fa
                       WHERE fa.client_id=c.id
                         AND fa.tenant_id=c.tenant_id
                         AND fa.status IN ('requested','confirmed')
                         AND fa.starts_at > NOW()
                   ) AS has_future_appointment,
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
            candidates.Add((reader.GetInt32(0),reader.GetInt32(1),reader.IsDBNull(2)?null:reader.GetInt32(2),reader.GetString(4),reader.GetBoolean(3)));
        await reader.DisposeAsync();

        foreach(var c in candidates)
        {
            var changed=await SetPatientLifecycleStatusAsync(c.TenantId,c.ClientId,c.Status,DateTime.UtcNow,cancellationToken);
            if(!changed) continue;

            (string Title,string Description,string Priority)? task=c.Status switch
            {
                "pending_info" => (
                    "Completar información inicial del paciente",
                    "Revisar y completar los datos personales y biométricos necesarios antes de continuar el seguimiento.",
                    "normal"),
                "pending_first_appointment" when !c.HasFutureAppointment => (
                    "Proponer primera cita al paciente",
                    "El paciente todavía no tiene una primera cita futura solicitada o confirmada. Revisar y proponer el siguiente paso.",
                    "normal"),
                "no_recent_followup" => (
                    "Contactar paciente sin seguimiento reciente",
                    "El paciente lleva más de 30 días sin una actividad de seguimiento reciente.",
                    "high"),
                _ => null
            };

            if(task.HasValue)
                await ScheduleActionAsync(
                    c.TenantId,
                    "create_professional_task",
                    new CreateTaskAction(
                        c.ClientId,
                        c.AssignedUserId,
                        task.Value.Title,
                        task.Value.Description,
                        DateTime.UtcNow.AddDays(1),
                        task.Value.Priority,
                        "automation:patient.lifecycle"),
                    DateTime.UtcNow,
                    null,
                    $"lifecycle:{c.Status}:{c.ClientId}:{DateTime.UtcNow:yyyyMMdd}",
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

    // last_activity_at solo avanza para estados de seguimiento: pasar a pending_info/pending_first_appointment
    // no debe borrar la última actividad real del paciente ni hacer que un cambio administrativo parezca actividad clínica.
    private async Task<bool> SetPatientLifecycleStatusAsync(int tenantId,int clientId,string status,DateTime changedAt,CancellationToken cancellationToken)
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
            WHERE id=@client AND tenant_id=@tenant
            RETURNING lifecycle_status_changed_at=@changed;
            """,connection);
        command.Parameters.AddWithValue("status",status);
        command.Parameters.AddWithValue("changed",changedAt);
        command.Parameters.AddWithValue("client",clientId);
        command.Parameters.AddWithValue("tenant",tenantId);
        var result=await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    // Traduce eventos de negocio a acciones persistentes (avisos, emails y tareas). Las claves de
    // idempotencia evitan que una repetición del mismo evento genere acciones duplicadas.
    private async Task ScheduleBuiltInRulesAsync(AutomationEvent evt, CancellationToken cancellationToken)
    {
        // Las reglas incorporadas se traducen a jobs persistentes; no se ejecutan directamente dentro
        // de la petición que generó el evento para mantener la respuesta independiente de email/push.
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

                    // Si la confirmación llega tarde, el recordatorio de 24 h se ejecuta cuanto antes; no se descarta
                    // por haber pasado su hora teórica, mientras que el de 2 h sí se omite si ya quedó atrás.
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
                        // El recordatorio de 2 h solo tiene sentido si todavía queda tiempo suficiente para enviarlo; si no,
                        // evitamos crear un job inmediatamente vencido que no aportaría valor al paciente.
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
                    // Al cancelar una cita se invalidan los recordatorios pendientes asociados a la confirmación original,
                    // pero nunca jobs que ya estén en processing/completed o pertenezcan a otro agregado.
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
            case "billing.subscription_started":
            case "billing.payment_succeeded":
            case "billing.payment_failed":
            case "billing.plan_changed":
            case "billing.subscription_cancelled":
            case "billing.trial_ending":
            case "billing.trial_ended":
            case "billing.payment_method_missing":
                {
                    var payload = AutomationJson.Deserialize<BillingAutomationPayload>(evt.Payload)
                        ?? throw new InvalidOperationException($"Payload inválido para {evt.EventType}.");

                    var (subject, htmlBody) = BuildBillingEmail(evt.EventType, payload);
                    await ScheduleActionAsync(
                        evt.TenantId,
                        "email_billing_contact",
                        new BillingEmailAction(payload.AssignedUserId, subject, htmlBody),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:billing-email",
                        cancellationToken: cancellationToken);

                    if (evt.EventType is "billing.payment_failed" or "billing.payment_method_missing" or
                        "billing.subscription_cancelled" or "billing.trial_ended")
                    {
                        var title = evt.EventType switch
                        {
                            "billing.payment_failed" => "Pago de suscripción fallido",
                            "billing.payment_method_missing" => "Falta el método de pago",
                            "billing.subscription_cancelled" => "Suscripción cancelada",
                            _ => "Prueba de suscripción finalizada"
                        };
                        var description = evt.EventType switch
                        {
                            "billing.payment_failed" => "Revisar el pago de la suscripción y resolver el problema de facturación.",
                            "billing.payment_method_missing" => "Revisar el método de pago de Stripe para evitar la interrupción del servicio.",
                            "billing.subscription_cancelled" => "Revisar la cancelación de la suscripción y contactar con la cuenta si es necesario.",
                            _ => "Revisar el estado de la cuenta tras finalizar el periodo de prueba."
                        };
                        await ScheduleActionAsync(
                            evt.TenantId,
                            "create_professional_task",
                            new CreateTaskAction(null, payload.AssignedUserId, title, description,
                                DateTime.UtcNow.AddHours(24), "high", $"automation:{evt.EventType}"),
                            DateTime.UtcNow,
                            evt.Id,
                            $"event:{evt.Id}:billing-task",
                            cancellationToken: cancellationToken);
                    }
                    break;
                }
        }
    }


    /// <summary>Revisa dietas activas próximas a finalizar o ya vencidas.</summary>
    // Genera avisos y tareas derivados de la fecha de finalización de las dietas activas.
    // Las claves de idempotencia impiden duplicar acciones en ejecuciones sucesivas.
    public async Task RunDietAutomationSweepAsync(CancellationToken cancellationToken = default)
    {
        // Esta tarea periódica complementa los eventos de dieta: sirve como red de seguridad para
        // recordatorios que dependen del tiempo transcurrido y no de una única modificación de datos.
        var candidates = new List<(int AssignmentId, int ClientId, int TenantId, int? NutritionistId, DateOnly? EndDate, string DietName)>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT cd.id,
                   cd.client_id,
                   cd.diet_id,
                   cd.end_date,
                   c.tenant_id,
                   d.name,
                   (
                       SELECT a.nutritionist_id
                         FROM client_nutritionist_assignments a
                        WHERE a.client_id=cd.client_id
                          AND a.is_active
                          AND a.nutritionist_id IS NOT NULL
                        ORDER BY a.assigned_at DESC
                        LIMIT 1
                   ) AS nutritionist_id
              FROM client_diets cd
              JOIN clients c ON c.id=cd.client_id
              JOIN diets d ON d.id=cd.diet_id
             WHERE cd.is_active=true
               AND c.archived_at IS NULL
               AND d.archived_at IS NULL
               AND c.tenant_id IS NOT NULL
               AND cd.end_date IS NOT NULL;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add((
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(4),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(5) ? "Dieta" : reader.GetString(5)));
        }

        // Las fechas de finalización de dieta son fechas de calendario, por lo que el barrido las compara con UTC
        // de forma consistente con el resto del worker y evita depender de la zona horaria del servidor.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var diet in candidates)
        {
            if (!diet.EndDate.HasValue) continue;

            var daysRemaining = diet.EndDate.Value.DayNumber - today.DayNumber;
            // Se avisa durante los tres días anteriores y también el propio día de vencimiento; la clave usa
            // la fecha de fin, por lo que una ejecución diaria no repite el aviso para la misma asignación.
            if (daysRemaining is >= 0 and <= 3)
            {
                await ScheduleActionAsync(
                    diet.TenantId,
                    "notify_patient",
                    new NotifyPatientAction(
                        diet.ClientId,
                        "diet_expiring",
                        "Tu dieta está próxima a finalizar",
                        $"Tu dieta \"{diet.DietName}\" finaliza en {Math.Max(0, daysRemaining)} día(s). Consulta con tu nutricionista si necesitas continuar o hacer cambios.",
                        "/patient?tab=diet"),
                    DateTime.UtcNow,
                    null,
                    $"diet:expiring:{diet.AssignmentId}:{diet.EndDate:yyyyMMdd}",
                    cancellationToken: cancellationToken);
            }

            // Una dieta vencida genera tarea profesional inmediata en lugar de modificar automáticamente la dieta:
            // la decisión clínica de renovar, sustituir o finalizar queda deliberadamente en manos del profesional.
            if (diet.EndDate.Value < today)
            {
                await ScheduleActionAsync(
                    diet.TenantId,
                    "create_professional_task",
                    new CreateTaskAction(
                        diet.ClientId,
                        diet.NutritionistId,
                        "Revisar dieta vencida",
                        $"La dieta \"{diet.DietName}\" ha superado su fecha de finalización y necesita revisión profesional.",
                        DateTime.UtcNow,
                        "high",
                        "automation:diet.expired"),
                    DateTime.UtcNow,
                    null,
                    $"diet:expired:{diet.AssignmentId}:{diet.EndDate:yyyyMMdd}",
                    cancellationToken: cancellationToken);
            }
        }
    }


    /// <summary>Invalida jobs pendientes de una familia funcional cuando su condición ya no se cumple.</summary>
    private async Task CancelPendingJobsByIdempotencyPrefixAsync(
        int tenantId,
        string prefix,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE automation_jobs
            SET status='cancelled', locked_at=NULL, updated_at=NOW()
            WHERE tenant_id=@tenant
              AND status='pending'
              AND idempotency_key LIKE @prefix;
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("prefix", prefix + "%");
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    // Marcar el check-in y cerrar su tarea automática forman una única transacción: no queremos dejar el check-in
    // como revisado mientras la tarea asociada continúa abierta, ni al contrario.
    public async Task<bool> ReviewPatientCheckinAsync(
        int tenantId,
        int checkinId,
        int reviewerUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var update = new NpgsqlCommand("""
            UPDATE patient_checkins
               SET reviewed_at = NOW(),
                   reviewed_by_user_id = @reviewer
             WHERE id=@id
               AND tenant_id=@tenant
               AND reviewed_at IS NULL
            RETURNING client_id;
            """, connection, transaction);
        update.Parameters.AddWithValue("id", checkinId);
        update.Parameters.AddWithValue("tenant", tenantId);
        update.Parameters.AddWithValue("reviewer", reviewerUserId);

        var clientResult = await update.ExecuteScalarAsync(cancellationToken);
        if (clientResult is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var clientId = Convert.ToInt32(clientResult);
        await using var complete = new NpgsqlCommand("""
            UPDATE professional_tasks
               SET status='completed',
                   completed_at=COALESCE(completed_at,NOW()),
                   updated_at=NOW()
             WHERE tenant_id=@tenant
               AND client_id=@client
               AND status IN ('open','in_progress')
               AND source='automation:patient.checkin.submitted'
               AND title='Revisar check-in semanal';
            """, connection, transaction);
        complete.Parameters.AddWithValue("tenant", tenantId);
        complete.Parameters.AddWithValue("client", clientId);
        await complete.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static string NormalizePriority(string? priority) =>
        priority?.Trim().ToLowerInvariant() switch
        {
            "low" => "low",
            "high" => "high",
            "urgent" => "urgent",
            _ => "normal"
        };

    public sealed record ClientOnboardingCompletedPayload(int ClientId, int? NutritionistId);
    public sealed record ClientCreatedPayload(int ClientId, int? NutritionistId);
    public sealed record CheckinSubmittedPayload(int ClientId, int? NutritionistId);
    public sealed record DietAutomationPayload(int ClientId, int AssignmentId, string DietName);
    public sealed record AppointmentCompletedPayload(int AppointmentId, int ClientId, int? NutritionistId);
    public sealed record AppointmentStatusPayload(int AppointmentId, int ClientId, int? NutritionistId, DateTime StartsAtUtc);
    public sealed record BillingAutomationPayload(int SubscriptionId, string PlanCode, string Status, DateTime? CurrentPeriodEnd, DateTime? TrialEnd, int? AssignedUserId);
    public sealed record BillingEmailAction(int? UserId, string Subject, string HtmlBody);

    private static (string Subject, string HtmlBody) BuildBillingEmail(string eventType, BillingAutomationPayload payload)
    {
        var title = eventType switch
        {
            "billing.subscription_started" => "Suscripción activada",
            "billing.payment_succeeded" => "Pago de DietoExpress recibido",
            "billing.payment_failed" => "Problema con el pago de DietoExpress",
            "billing.plan_changed" => "Suscripción actualizada",
            "billing.subscription_cancelled" => "Suscripción cancelada",
            "billing.trial_ending" => "Tu periodo de prueba está a punto de terminar",
            "billing.trial_ended" => "Tu periodo de prueba ha terminado",
            "billing.payment_method_missing" => "Revisa el método de pago de DietoExpress",
            _ => "Actualización de la suscripción"
        };

        var detail = eventType switch
        {
            "billing.payment_failed" => "Stripe ha informado de un pago fallido. Revisa el método de pago y el estado de la suscripción.",
            "billing.payment_method_missing" => "La suscripción no tiene un método de pago disponible o Stripe no ha podido identificarlo. Revísalo para evitar una interrupción del servicio.",
            "billing.subscription_cancelled" => "La suscripción ha sido cancelada en Stripe.",
            "billing.trial_ending" => $"El periodo de prueba termina el {payload.TrialEnd:dd/MM/yyyy}. Revisa la suscripción si quieres continuar con el servicio.",
            "billing.trial_ended" => "El periodo de prueba ha finalizado. Revisa la suscripción para continuar utilizando las funciones de pago.",
            "billing.plan_changed" => $"La cuenta está asociada al plan {payload.PlanCode}.",
            "billing.subscription_started" => $"La suscripción al plan {payload.PlanCode} ha sido activada.",
            _ => "El pago de la suscripción se ha procesado correctamente."
        };

        return (title, $"<p>{detail}</p><p>Puedes revisar el estado de la cuenta desde DietoExpress.</p>");
    }
}
