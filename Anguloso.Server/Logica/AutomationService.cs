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

    private sealed record AutomationRuleConfig(bool Enabled, int? DelayMinutes, string RecipientScope, string[] Channels);

    private async Task<AutomationRuleConfig> GetRuleConfigAsync(int tenantId, string ruleKey, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT enabled, delay_minutes, recipient_scope, channels
            FROM automation_rules
            WHERE tenant_id=@tenant AND rule_key=@rule
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("rule", ruleKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            var defaults = ruleKey switch
            {
                "patient.checkin.reviewed" => new AutomationRuleConfig(true, null, "patient", ["in_app"]),
                "appointment.completed" => new AutomationRuleConfig(true, null, "both", ["in_app"]),
                "appointment.reminder.24h" => new AutomationRuleConfig(true, null, "patient", ["in_app"]),
                "appointment.reminder.2h" => new AutomationRuleConfig(true, null, "patient", ["in_app"]),
                "onboarding.info.reminder" => new AutomationRuleConfig(true, null, "patient", ["in_app"]),
                "onboarding.info.escalation" => new AutomationRuleConfig(true, null, "assigned_professional", ["in_app"]),
                "onboarding.first_appointment.reminder" => new AutomationRuleConfig(true, null, "patient", ["in_app"]),
                "onboarding.first_appointment.escalation" => new AutomationRuleConfig(true, null, "assigned_professional", ["in_app"]),
                "followup.checkin.reminder" => new AutomationRuleConfig(true, null, "patient", ["in_app"]),
                "followup.checkin.escalation" => new AutomationRuleConfig(true, null, "assigned_professional", ["in_app"]),
                "diet.expiry.reminder" => new AutomationRuleConfig(true, null, "patient", ["in_app"]),
                "diet.expired" => new AutomationRuleConfig(true, null, "both", ["in_app"]),
                "diet.renewal" => new AutomationRuleConfig(true, null, "both", ["in_app"]),
                "biometrics.review_due" => new AutomationRuleConfig(true, null, "assigned_professional", ["in_app"]),
                "biometrics.evolution" => new AutomationRuleConfig(true, null, "assigned_professional", ["in_app"]),
                "documents.pending.reminder" => new AutomationRuleConfig(true, null, "patient", ["in_app"]),
                "documents.completed" => new AutomationRuleConfig(true, null, "assigned_professional", ["in_app"]),
                _ => new AutomationRuleConfig(true, null, "assigned_professional", ["in_app"])
            };
            return defaults;
        }
        var channels = reader.IsDBNull(3)
            ? ["in_app"]
            : (JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? ["in_app"]);
        return new AutomationRuleConfig(reader.GetBoolean(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1),
            reader.GetString(2), channels);
    }

    private sealed record AutomationTemplate(string? PatientTitle, string? PatientMessage, string? ProfessionalTitle, string? ProfessionalMessage, string? EmailSubject, string? EmailHtml);

    private async Task<AutomationTemplate?> GetTemplateAsync(int tenantId, string ruleKey, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT patient_title, patient_message, professional_title, professional_message, email_subject, email_html
            FROM automation_templates
            WHERE tenant_id=@tenant AND rule_key=@rule
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("rule", ruleKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AutomationTemplate(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private static string RenderTemplate(string template, string title, string message, string? actionUrl = null)
        => template.Replace("{title}", title, StringComparison.OrdinalIgnoreCase)
                   .Replace("{message}", message, StringComparison.OrdinalIgnoreCase)
                   .Replace("{action_url}", actionUrl ?? "", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsRuleEnabledAsync(int tenantId, string ruleKey, CancellationToken cancellationToken)
        => (await GetRuleConfigAsync(tenantId, ruleKey, cancellationToken)).Enabled;

    private async Task<DateTime> ApplyConfiguredDelayAsync(int tenantId, string ruleKey, DateTime defaultDueAt, CancellationToken cancellationToken)
    {
        var config = await GetRuleConfigAsync(tenantId, ruleKey, cancellationToken);
        return config.DelayMinutes.HasValue
            ? DateTime.UtcNow.AddMinutes(Math.Max(0, config.DelayMinutes.Value))
            : defaultDueAt;
    }

    private async Task<DateTime> ApplyConfiguredLeadTimeAsync(
        int tenantId,
        string ruleKey,
        DateTime appointmentStartUtc,
        int defaultLeadMinutes,
        CancellationToken cancellationToken)
    {
        var config = await GetRuleConfigAsync(tenantId, ruleKey, cancellationToken);
        var leadMinutes = config.DelayMinutes ?? defaultLeadMinutes;
        var scheduledAt = appointmentStartUtc.AddMinutes(-Math.Max(0, leadMinutes));
        return scheduledAt < DateTime.UtcNow ? DateTime.UtcNow : scheduledAt;
    }

    // La configuración de destinatarios y canales se aplica al crear el job, no al ejecutarlo: así el worker sigue siendo
    // un ejecutor ciego y los jobs persistidos representan exactamente las entregas solicitadas por la regla.
    private async Task<long> ScheduleConfiguredActionAsync(
        int tenantId, string actionType, object payload, DateTime scheduledAt, long? eventId,
        string? idempotencyKey, int maxAttempts, CancellationToken cancellationToken)
    {
        var ruleKey = ResolveRuleKey(actionType, payload, idempotencyKey);
        if (ruleKey is null)
            return await ScheduleRawActionAsync(tenantId, actionType, payload, scheduledAt, eventId, idempotencyKey, maxAttempts, cancellationToken);

        var config = await GetRuleConfigAsync(tenantId, ruleKey, cancellationToken);
        if (!config.Enabled)
            return 0;

        var channels = config.Channels
            .Where(c => c is not null)
            .Select(c => c.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scope = config.RecipientScope.Trim().ToLowerInvariant();
        var wantsPatient = scope is "patient" or "both";
        var wantsProfessional = scope is "assigned_professional" or "clinic_admin" or "both";

        var template = await GetTemplateAsync(tenantId, ruleKey, cancellationToken);

        async Task SchedulePatientAsync(NotifyPatientAction action)
        {
            var title = string.IsNullOrWhiteSpace(template?.PatientTitle) ? action.Title : RenderTemplate(template.PatientTitle, action.Title, action.Message, action.ActionUrl);
            var message = string.IsNullOrWhiteSpace(template?.PatientMessage) ? action.Message : RenderTemplate(template.PatientMessage, action.Title, action.Message, action.ActionUrl);
            if (channels.Contains("in_app") || channels.Contains("push"))
            {
                await ScheduleRawActionAsync(tenantId, "notify_patient",
                    action with { Title = title, Message = message, SendPush = channels.Contains("push") }, scheduledAt, eventId,
                    idempotencyKey is null ? null : $"{idempotencyKey}:patient:in-app", maxAttempts, cancellationToken);
            }
            if (channels.Contains("email"))
            {
                var subject = string.IsNullOrWhiteSpace(template?.EmailSubject) ? title : RenderTemplate(template.EmailSubject, title, message, action.ActionUrl);
                var html = string.IsNullOrWhiteSpace(template?.EmailHtml)
                    ? $"<p>{System.Net.WebUtility.HtmlEncode(message)}</p>"
                    : RenderTemplate(template.EmailHtml, title, message, action.ActionUrl);
                await ScheduleRawActionAsync(tenantId, "email_patient",
                    new EmailPatientAction(action.ClientId, subject, html), scheduledAt, eventId,
                    idempotencyKey is null ? null : $"{idempotencyKey}:patient:email", maxAttempts, cancellationToken);
            }
        }

        async Task ScheduleProfessionalAsync(int? assignedUserId, int? clientId, string title, string? description, DateTime? dueAt, string priority, string source)
        {
            var recipientIds = await ResolveProfessionalRecipientsAsync(tenantId, scope, assignedUserId, cancellationToken);
            var professionalTitle = string.IsNullOrWhiteSpace(template?.ProfessionalTitle) ? title : RenderTemplate(template.ProfessionalTitle, title, description ?? title);
            var professionalMessage = string.IsNullOrWhiteSpace(template?.ProfessionalMessage) ? (description ?? title) : RenderTemplate(template.ProfessionalMessage, title, description ?? title);
            foreach (var recipientId in recipientIds)
            {
                if (channels.Contains("in_app"))
                {
                    await ScheduleRawActionAsync(tenantId, "create_professional_task",
                        new CreateTaskAction(clientId, recipientId, professionalTitle, professionalMessage, dueAt, priority, source),
                        scheduledAt, eventId,
                        idempotencyKey is null ? null : $"{idempotencyKey}:professional:in-app:{recipientId}", maxAttempts, cancellationToken);
                }
                if (channels.Contains("email"))
                {
                    await ScheduleRawActionAsync(tenantId, "email_professional",
                        new ProfessionalEmailAction(recipientId,
                            string.IsNullOrWhiteSpace(template?.EmailSubject) ? professionalTitle : RenderTemplate(template.EmailSubject, professionalTitle, professionalMessage ?? professionalTitle),
                            string.IsNullOrWhiteSpace(template?.EmailHtml) ? $"<p>{System.Net.WebUtility.HtmlEncode(professionalMessage ?? professionalTitle)}</p>" : RenderTemplate(template.EmailHtml, professionalTitle, professionalMessage ?? professionalTitle)),
                        scheduledAt, eventId,
                        idempotencyKey is null ? null : $"{idempotencyKey}:professional:email:{recipientId}", maxAttempts, cancellationToken);
                }
            }
        }

        switch (payload)
        {
            case NotifyPatientAction patientAction:
                if (wantsPatient) await SchedulePatientAsync(patientAction);
                if (wantsProfessional)
                    await ScheduleProfessionalAsync(null, patientAction.ClientId, patientAction.Title, patientAction.Message, scheduledAt, "normal", $"automation:{ruleKey}");
                break;
            case EmailPatientAction emailAction:
                if (wantsPatient && channels.Contains("email"))
                    await ScheduleRawActionAsync(tenantId, "email_patient", emailAction, scheduledAt, eventId,
                        idempotencyKey is null ? null : $"{idempotencyKey}:patient:email", maxAttempts, cancellationToken);
                if (wantsPatient && (channels.Contains("in_app") || channels.Contains("push")))
                    await SchedulePatientAsync(new NotifyPatientAction(emailAction.ClientId, ruleKey, emailAction.Subject, StripHtml(emailAction.HtmlBody), null));
                break;
            case CreateTaskAction taskAction:
                if (wantsProfessional)
                    await ScheduleProfessionalAsync(taskAction.AssignedUserId, taskAction.ClientId, taskAction.Title, taskAction.Description, taskAction.DueAt, taskAction.Priority, taskAction.Source);
                if (wantsPatient && channels.Contains("email"))
                    await ScheduleRawActionAsync(tenantId, "email_patient",
                        new EmailPatientAction(taskAction.ClientId ?? throw new InvalidOperationException("La regla requiere un paciente."), taskAction.Title,
                            $"<p>{System.Net.WebUtility.HtmlEncode(taskAction.Description ?? taskAction.Title)}</p>"),
                        scheduledAt, eventId,
                        idempotencyKey is null ? null : $"{idempotencyKey}:patient:email", maxAttempts, cancellationToken);
                break;
            default:
                return await ScheduleRawActionAsync(tenantId, actionType, payload, scheduledAt, eventId, idempotencyKey, maxAttempts, cancellationToken);
        }

        return 0;
    }

    private async Task<int[]> ResolveProfessionalRecipientsAsync(int tenantId, string scope, int? assignedUserId, CancellationToken cancellationToken)
    {
        if (scope == "assigned_professional")
            return assignedUserId.HasValue ? [assignedUserId.Value] : [];

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id
            FROM users
            WHERE tenant_id=@tenant
              AND archived_at IS NULL
              AND role <> 'superadmin'
              AND (@scope <> 'clinic_admin' OR role='clinic_admin')
            ORDER BY CASE WHEN role='clinic_admin' THEN 0 ELSE 1 END, created_at, id;
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("scope", scope);
        var result = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (scope == "both" && assignedUserId.HasValue && reader.GetInt32(0) != assignedUserId.Value)
                continue;
            result.Add(reader.GetInt32(0));
        }
        return result.ToArray();
    }

    private static string? ResolveRuleKey(string actionType, object payload, string? idempotencyKey)
    {
        if (idempotencyKey?.StartsWith("onboarding:info-reminder:", StringComparison.Ordinal) == true) return "onboarding.info.reminder";
        if (idempotencyKey?.StartsWith("onboarding:info-task:", StringComparison.Ordinal) == true) return "onboarding.info.escalation";
        if (idempotencyKey?.StartsWith("onboarding:first-appointment-reminder:", StringComparison.Ordinal) == true) return "onboarding.first_appointment.reminder";
        if (idempotencyKey?.StartsWith("onboarding:first-appointment-task:", StringComparison.Ordinal) == true) return "onboarding.first_appointment.escalation";
        if (idempotencyKey?.StartsWith("followup:checkin-reminder:", StringComparison.Ordinal) == true) return "followup.checkin.reminder";
        if (idempotencyKey?.StartsWith("followup:checkin-task:", StringComparison.Ordinal) == true) return "followup.checkin.escalation";
        if (idempotencyKey?.StartsWith("diet:expiring:", StringComparison.Ordinal) == true) return "diet.expiry.reminder";
        if (idempotencyKey?.StartsWith("diet:expired:", StringComparison.Ordinal) == true || idempotencyKey?.StartsWith("diet:expired-notification:", StringComparison.Ordinal) == true) return "diet.expired";
        if (idempotencyKey?.StartsWith("diet:renewal:", StringComparison.Ordinal) == true) return "diet.renewal";
        if (idempotencyKey?.StartsWith("diet:renewal-task:", StringComparison.Ordinal) == true) return "diet.renewal";
        if (idempotencyKey?.StartsWith("biometrics:review_due:", StringComparison.Ordinal) == true) return "biometrics.review_due";
        if (idempotencyKey?.StartsWith("biometrics:evolution:", StringComparison.Ordinal) == true) return "biometrics.evolution";
        if (idempotencyKey?.StartsWith("documents:pending-reminder:", StringComparison.Ordinal) == true) return "documents.pending.reminder";
        if (idempotencyKey?.StartsWith("documents:completed:", StringComparison.Ordinal) == true) return "documents.completed";
        if (idempotencyKey?.EndsWith(":reminder-24h", StringComparison.Ordinal) == true) return "appointment.reminder.24h";
        if (idempotencyKey?.EndsWith(":reminder-2h", StringComparison.Ordinal) == true) return "appointment.reminder.2h";
        if (idempotencyKey?.StartsWith("postappointment:checkin:", StringComparison.Ordinal) == true) return "appointment.completed";
        if (idempotencyKey?.StartsWith("postappointment:next:", StringComparison.Ordinal) == true) return "appointment.completed";

        return payload switch
        {
            CreateTaskAction task when task.Source == "automation:client.created" => "client.created",
            CreateTaskAction task when task.Source == "automation:patient.checkin.submitted" => "patient.checkin.submitted",
            CreateTaskAction task when task.Source == "automation:patient.checkin.reviewed" => "patient.checkin.reviewed",
            CreateTaskAction task when task.Source == "automation:appointment.completed" => "appointment.completed",
            CreateTaskAction task when task.Source == "automation:appointment.no_show" => "appointment.no_show",
            CreateTaskAction task when task.Source == "automation:onboarding.info" => "onboarding.info.escalation",
            CreateTaskAction task when task.Source == "automation:onboarding:first-appointment" => "onboarding.first_appointment.escalation",
            CreateTaskAction task when task.Source == "automation:followup.checkin" => "followup.checkin.escalation",
            CreateTaskAction task when task.Source == "automation:diet.expired" => "diet.expired",
            CreateTaskAction task when task.Source == "automation:diet.renewal" => "diet.renewal",
            CreateTaskAction task when task.Source == "automation:biometrics.review_due" => "biometrics.review_due",
            CreateTaskAction task when task.Source == "automation:biometrics.evolution" => "biometrics.evolution",
            NotifyPatientAction notification when notification.Type == "checkin_reviewed" => "patient.checkin.reviewed",
            NotifyPatientAction notification when notification.Type == "post_appointment_checkin" => "appointment.completed",
            NotifyPatientAction notification when notification.Type == "appointment_reminder" => idempotencyKey?.EndsWith(":reminder-2h", StringComparison.Ordinal) == true ? "appointment.reminder.2h" : "appointment.reminder.24h",
            NotifyPatientAction notification when notification.Type == "onboarding_info_reminder" => "onboarding.info.reminder",
            NotifyPatientAction notification when notification.Type == "first_appointment_reminder" => "onboarding.first_appointment.reminder",
            NotifyPatientAction notification when notification.Type == "checkin_reminder" => "followup.checkin.reminder",
            NotifyPatientAction notification when notification.Type == "diet_expiring" => "diet.expiry.reminder",
            NotifyPatientAction notification when notification.Type == "diet_expired" => "diet.expired",
            NotifyPatientAction notification when notification.Type == "diet_renewal" => "diet.renewal",
            _ => null
        };
    }

    private static string StripHtml(string html) => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();

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
        => await ScheduleConfiguredActionAsync(tenantId, actionType, payload, scheduledAt, eventId, idempotencyKey, maxAttempts, cancellationToken);

    private async Task<long> ScheduleRawActionAsync(
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
                    clientId = payload.ClientId;
                    status = "pending_first_appointment";
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
                // Un check-in es actividad clínica explícita: si el paciente estaba en no_recent_followup,
                // esta interacción lo reactiva sin tocar pacientes archivados.
                status = "follow_up";
                break;
            case "diet.published":
            case "diet.changed":
                {
                    var payload = AutomationJson.Deserialize<DietAutomationPayload>(evt.Payload)
                        ?? throw new InvalidOperationException($"Payload inválido para {evt.EventType}.");
                    await CancelPendingJobsByIdempotencyPrefixAsync(evt.TenantId, $"diet:expiring:{payload.AssignmentId}:", cancellationToken);
                    await CancelPendingJobsByIdempotencyPrefixAsync(evt.TenantId, $"diet:expired:{payload.AssignmentId}:", cancellationToken);
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
            case "patient.checkin.reviewed":
                {
                    var payload = AutomationJson.Deserialize<CheckinReviewedPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para patient.checkin.reviewed.");

                    await ScheduleActionAsync(
                        evt.TenantId,
                        "notify_patient",
                        new NotifyPatientAction(
                            payload.ClientId,
                            "checkin_reviewed",
                            "Tu check-in ha sido revisado",
                            "Tu nutricionista ya ha revisado tu último check-in. Puedes consultar tu evolución desde el portal.",
                            "/patient?tab=checkins"),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:patient-notification",
                        cancellationToken: cancellationToken);

                    if (!await HasFutureAppointmentAsync(evt.TenantId, payload.ClientId, cancellationToken))
                    {
                        await ScheduleActionAsync(
                            evt.TenantId,
                            "create_professional_task",
                            new CreateTaskAction(
                                payload.ClientId,
                                payload.NutritionistId,
                                "Planificar siguiente paso tras el check-in",
                                "El último check-in ha sido revisado y el paciente no tiene una cita futura. Valorar seguimiento, nueva cita o actualización de dieta.",
                                DateTime.UtcNow.AddDays(1),
                                "normal",
                                "automation:patient.checkin.reviewed"),
                            DateTime.UtcNow,
                            evt.Id,
                            $"event:{evt.Id}:next-action-task",
                            cancellationToken: cancellationToken);
                    }
                    break;
                }
            case "appointment.completed":
                {
                    if (!await IsRuleEnabledAsync(evt.TenantId, "appointment.completed", cancellationToken)) break;
                    var payload = AutomationJson.Deserialize<AppointmentCompletedPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para appointment.completed.");

                    await ScheduleActionAsync(
                        evt.TenantId,
                        "notify_patient",
                        new NotifyPatientAction(
                            payload.ClientId,
                            "post_appointment_checkin",
                            "Tu cita ha terminado",
                            "En unos días podrás completar tu check-in para contarle a tu nutricionista cómo ha ido esta semana.",
                            "/patient?tab=checkins"),
                        DateTime.UtcNow.AddDays(3),
                        evt.Id,
                        $"postappointment:checkin:{payload.ClientId}:{evt.Id}",
                        cancellationToken: cancellationToken);

                    if (!await HasFutureAppointmentAsync(evt.TenantId, payload.ClientId, cancellationToken))
                    {
                        await ScheduleActionAsync(
                            evt.TenantId,
                            "create_professional_task",
                            new CreateTaskAction(
                                payload.ClientId,
                                payload.NutritionistId,
                                "Planificar próxima cita",
                                "La última cita se ha completado y no existe una cita futura. Valorar la siguiente revisión y, si procede, la actualización de la dieta.",
                                DateTime.UtcNow.AddDays(14),
                                "normal",
                                "automation:appointment.completed"),
                            DateTime.UtcNow.AddDays(14),
                            evt.Id,
                            $"postappointment:next:{payload.ClientId}:{payload.AppointmentId}",
                            cancellationToken: cancellationToken);
                    }

                    await ScheduleActionAsync(
                        evt.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(
                            payload.ClientId,
                            payload.NutritionistId,
                            "Revisar resultado de la cita",
                            "Revisar la cita completada y confirmar si el plan de seguimiento, check-in o dieta requiere alguna acción.",
                            DateTime.UtcNow.AddDays(1),
                            "normal",
                            "automation:appointment.completed"),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:create-professional-task",
                        cancellationToken: cancellationToken);
                    break;
                }
            case "appointment.requested":
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
                if (!await IsRuleEnabledAsync(c.TenantId, "onboarding.info.reminder", cancellationToken)) continue;
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

                if (c.CreatedAt <= now.AddDays(-3) &&
                    await IsRuleEnabledAsync(c.TenantId, "onboarding.info.escalation", cancellationToken))
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
                if (!await IsRuleEnabledAsync(c.TenantId, "onboarding.first_appointment.reminder", cancellationToken)) continue;
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

                if (c.CreatedAt <= now.AddDays(-3) &&
                    await IsRuleEnabledAsync(c.TenantId, "onboarding.first_appointment.escalation", cancellationToken))
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
            if (!needsCheckin || !await IsRuleEnabledAsync(c.TenantId, "followup.checkin.reminder", cancellationToken)) continue;

            var weekKey = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7)).ToString("yyyyMMdd");
            var daysSinceCheckin = c.LastCheckin.HasValue ? (int)Math.Floor((now - c.LastCheckin.Value).TotalDays) : int.MaxValue;
            var level = daysSinceCheckin >= 21 ? "critical" : daysSinceCheckin >= 14 ? "escalated" : daysSinceCheckin >= 10 ? "overdue" : "weekly";
            var message = level switch
            {
                "critical" => "Llevas varias semanas sin completar tu seguimiento. Entra en tu portal para retomarlo.",
                "escalated" => "Tu seguimiento lleva más de dos semanas pendiente. Completa el check-in para que tu nutricionista pueda revisar tu evolución.",
                "overdue" => "Tu check-in lleva más de 10 días pendiente. Completa el seguimiento desde tu portal.",
                _ => "Completa tu check-in semanal para que tu nutricionista pueda revisar tu evolución."
            };

            await ScheduleActionAsync(
                c.TenantId,
                "notify_patient",
                new NotifyPatientAction(c.ClientId, "checkin_reminder", "Tienes un check-in pendiente", message, "/patient?tab=checkins"),
                now,
                null,
                $"followup:checkin-reminder:{c.ClientId}:{weekKey}:{level}",
                cancellationToken: cancellationToken);

            if (daysSinceCheckin >= 10 &&
                await IsRuleEnabledAsync(c.TenantId, "followup.checkin.escalation", cancellationToken))
            {
                await ScheduleActionAsync(
                    c.TenantId,
                    "create_professional_task",
                    new CreateTaskAction(
                        c.ClientId,
                        c.AssignedUserId,
                        daysSinceCheckin >= 21 ? "Escalar paciente sin seguimiento" : "Revisar seguimiento pendiente",
                        daysSinceCheckin >= 21 ? "El paciente lleva al menos 21 días sin enviar un check-in. Valorar contacto directo o reactivación." : "El paciente lleva más de 10 días sin enviar el check-in semanal.",
                        now.AddDays(1),
                        daysSinceCheckin >= 21 ? "high" : "normal",
                        "automation:followup.checkin"),
                    now,
                    null,
                    $"followup:checkin-task:{c.ClientId}:{weekKey}:{level}",
                    cancellationToken: cancellationToken);
            }
        }
    }

    /// <summary>
    /// Reconciliación de automatizaciones clínicas avanzadas. No depende de eventos puntuales:
    /// reconstruye recordatorios a partir de las últimas mediciones y dietas persistidas.
    /// </summary>
    public async Task RunAdvancedAutomationSweepAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var patients = new List<(int TenantId, int ClientId, int? UserId)>();
        await using (var command = new NpgsqlCommand("""
            SELECT id, tenant_id, user_id
            FROM clients
            WHERE archived_at IS NULL AND tenant_id IS NOT NULL;
            """, connection))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                patients.Add((reader.GetInt32(1), reader.GetInt32(0), reader.IsDBNull(2) ? null : reader.GetInt32(2)));
        }

        foreach (var patient in patients)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsRuleEnabledAsync(patient.TenantId, "biometrics.review_due", cancellationToken))
            {
                await using var biometricCommand = new NpgsqlCommand("""
                    SELECT measurement_date
                    FROM biometrics
                    WHERE client_id=@client
                    ORDER BY measurement_date DESC
                    LIMIT 1;
                    """, connection);
                biometricCommand.Parameters.AddWithValue("client", patient.ClientId);
                var latest = await biometricCommand.ExecuteScalarAsync(cancellationToken);

                if (latest is DateTime latestDate && latestDate.Date <= DateTime.UtcNow.Date.AddDays(-30)
                    || latest is DateOnly latestDateOnly && latestDateOnly <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)))
                {
                    var keyDate = latest is DateTime dt ? dt.ToString("yyyyMMdd") :
                        latest is DateOnly d ? d.ToString("yyyyMMdd") : "unknown";
                    await ScheduleConfiguredActionAsync(
                        patient.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(
                            patient.ClientId,
                            patient.UserId,
                            "Revisar mediciones antropométricas",
                            "No hay una medición antropométrica reciente. Revisar si corresponde programar una nueva valoración.",
                            DateTime.UtcNow,
                            "normal",
                            "automation:biometrics.review_due"),
                        DateTime.UtcNow,
                        null,
                        $"biometrics:review_due:{patient.ClientId}:{keyDate}",
                        3,
                        cancellationToken);
                }
            }

            if (await IsRuleEnabledAsync(patient.TenantId, "biometrics.evolution", cancellationToken))
            {
                await using var evolutionCommand = new NpgsqlCommand("""
                    SELECT measurement_date, weight, body_fat
                    FROM biometrics
                    WHERE client_id=@client
                    ORDER BY measurement_date DESC, id DESC
                    LIMIT 2;
                    """, connection);
                evolutionCommand.Parameters.AddWithValue("client", patient.ClientId);

                var measurements = new List<(DateOnly Date, double? Weight, double? BodyFat)>();
                await using var evolutionReader = await evolutionCommand.ExecuteReaderAsync(cancellationToken);
                while (await evolutionReader.ReadAsync(cancellationToken))
                    measurements.Add((evolutionReader.GetFieldValue<DateOnly>(0),
                        evolutionReader.IsDBNull(1) ? null : evolutionReader.GetDouble(1),
                        evolutionReader.IsDBNull(2) ? null : evolutionReader.GetDouble(2)));

                if (measurements.Count == 2)
                {
                    var current = measurements[0];
                    var previous = measurements[1];
                    var weightChange = current.Weight.HasValue && previous.Weight is > 0
                        ? Math.Abs(current.Weight.Value - previous.Weight.Value) / previous.Weight.Value
                        : 0;
                    var bodyFatChange = current.BodyFat.HasValue && previous.BodyFat.HasValue
                        ? Math.Abs(current.BodyFat.Value - previous.BodyFat.Value)
                        : 0;

                    if (weightChange >= 0.05 || bodyFatChange >= 3)
                    {
                        var detail = weightChange >= 0.05
                            ? $"El peso ha variado aproximadamente un {weightChange:P0} entre las dos últimas mediciones."
                            : $"El porcentaje de grasa ha variado {bodyFatChange:0.0} puntos entre las dos últimas mediciones.";

                        await ScheduleConfiguredActionAsync(
                            patient.TenantId,
                            "create_professional_task",
                            new CreateTaskAction(
                                patient.ClientId,
                                patient.UserId,
                                "Revisar evolución antropométrica",
                                detail,
                                DateTime.UtcNow,
                                "high",
                                "automation:biometrics.evolution"),
                            DateTime.UtcNow,
                            null,
                            $"biometrics:evolution:{patient.ClientId}:{current.Date:yyyyMMdd}",
                            3,
                            cancellationToken);
                    }
                }
            }

            if (await IsRuleEnabledAsync(patient.TenantId, "diet.renewal", cancellationToken))
            {
                await using var dietCommand = new NpgsqlCommand("""
                    SELECT cd.end_date
                    FROM client_diets cd
                    WHERE cd.client_id=@client AND cd.is_active=TRUE
                    ORDER BY cd.end_date NULLS LAST, cd.id DESC
                    LIMIT 1;
                    """, connection);
                dietCommand.Parameters.AddWithValue("client", patient.ClientId);
                var end = await dietCommand.ExecuteScalarAsync(cancellationToken);
                var endDate = end switch
                {
                    DateOnly date => date,
                    DateTime dateTime => DateOnly.FromDateTime(dateTime),
                    _ => (DateOnly?)null
                };

                if (endDate.HasValue && endDate.Value <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14))
                {
                    var remaining = (endDate.Value.ToDateTime(TimeOnly.MinValue).Date - DateTime.UtcNow.Date).Days;
                    await ScheduleConfiguredActionAsync(
                        patient.TenantId,
                        "notify_patient",
                        new NotifyPatientAction(
                            patient.ClientId,
                            "diet_renewal",
                            "Tu dieta está próxima a finalizar",
                            $"Tu dieta actual termina en {Math.Max(0, remaining)} días. Puedes contactar con tu nutricionista para revisar la siguiente.",
                            "/patient?tab=diets",
                            false),
                        DateTime.UtcNow,
                        null,
                        $"diet:renewal:{patient.ClientId}:{endDate.Value:yyyyMMdd}",
                        3,
                        cancellationToken);

                    await ScheduleConfiguredActionAsync(
                        patient.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(
                            patient.ClientId,
                            patient.UserId,
                            "Planificar renovación de dieta",
                            $"La dieta activa del paciente finaliza el {endDate.Value:dd/MM/yyyy}. Revisar continuidad o actualización.",
                            DateTime.UtcNow,
                            "normal",
                            "automation:diet.renewal"),
                        DateTime.UtcNow,
                        null,
                        $"diet:renewal-task:{patient.ClientId}:{endDate.Value:yyyyMMdd}",
                        3,
                        cancellationToken);
                }
            }
        }
    }

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
                              COALESCE((SELECT MAX(pc.submitted_at) FROM patient_checkins pc WHERE pc.client_id=c.id), TIMESTAMPTZ '1970-01-01')
                          ) < NOW() - INTERVAL '30 days'
                       THEN 'no_recent_followup'
                     WHEN EXISTS (SELECT 1 FROM patient_appointments a
                                   WHERE a.client_id=c.id AND a.tenant_id=c.tenant_id AND a.status='completed'
                                     AND a.starts_at >= NOW() - INTERVAL '30 days')
                          OR EXISTS (SELECT 1 FROM patient_checkins pc
                                     WHERE pc.client_id=c.id
                                       AND pc.submitted_at >= NOW() - INTERVAL '14 days')
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

            if (c.Status == "no_recent_followup")
            {
                await ScheduleActionAsync(
                    c.TenantId,
                    "notify_patient",
                    new NotifyPatientAction(
                        c.ClientId,
                        "followup_recovery",
                        "Te echamos de menos",
                        "Hace tiempo que no registras actividad de seguimiento. Si quieres continuar, entra en tu portal y retoma el contacto con tu nutricionista.",
                        "/patient?tab=checkins"),
                    DateTime.UtcNow,
                    null,
                    $"lifecycle:recovery:{c.ClientId}:{DateTime.UtcNow:yyyyMMdd}",
                    cancellationToken: cancellationToken);
            }

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

    private async Task<bool> HasFutureAppointmentAsync(int tenantId, int clientId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(
                SELECT 1 FROM patient_appointments
                WHERE tenant_id=@tenant AND client_id=@client
                  AND status IN ('requested','confirmed')
                  AND starts_at > NOW()
            );
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", clientId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
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
            WHERE id=@client AND tenant_id=@tenant AND archived_at IS NULL
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
                    if (!await IsRuleEnabledAsync(evt.TenantId, "client.created", cancellationToken)) break;
                    var payload = AutomationJson.Deserialize<ClientCreatedPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para client.created.");
                    var due = await ApplyConfiguredDelayAsync(evt.TenantId, "client.created", DateTime.UtcNow.AddDays(1), cancellationToken);
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
                    if (!await IsRuleEnabledAsync(evt.TenantId, "patient.checkin.submitted", cancellationToken)) break;
                    var payload = AutomationJson.Deserialize<CheckinSubmittedPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para patient.checkin.submitted.");
                    await CancelPendingJobsByIdempotencyPrefixAsync(evt.TenantId, $"followup:checkin-reminder:{payload.ClientId}:", cancellationToken);
                    await CancelPendingJobsByIdempotencyPrefixAsync(evt.TenantId, $"followup:checkin-task:{payload.ClientId}:", cancellationToken);
                    await CancelPendingJobsByIdempotencyPrefixAsync(evt.TenantId, $"postappointment:checkin:{payload.ClientId}:", cancellationToken);
                    await ScheduleActionAsync(
                        evt.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(payload.ClientId, payload.NutritionistId,
                            "Revisar check-in semanal",
                            "El paciente ha enviado un nuevo check-in semanal.",
                            await ApplyConfiguredDelayAsync(evt.TenantId, "patient.checkin.submitted", DateTime.UtcNow.AddHours(24), cancellationToken), "high", "automation:patient.checkin.submitted"),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:create-professional-task",
                        cancellationToken: cancellationToken);
                    break;
                }
            case "appointment.completed":
                {
                    if (!await IsRuleEnabledAsync(evt.TenantId, "appointment.completed", cancellationToken)) break;
                    var payload = AutomationJson.Deserialize<AppointmentCompletedPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para appointment.completed.");

                    await ScheduleActionAsync(
                        evt.TenantId,
                        "notify_patient",
                        new NotifyPatientAction(
                            payload.ClientId,
                            "post_appointment_checkin",
                            "Tu cita ha terminado",
                            "En unos días podrás completar tu check-in para contarle a tu nutricionista cómo ha ido esta semana.",
                            "/patient?tab=checkins"),
                        DateTime.UtcNow.AddDays(3),
                        evt.Id,
                        $"postappointment:checkin:{payload.ClientId}:{evt.Id}",
                        cancellationToken: cancellationToken);

                    if (!await HasFutureAppointmentAsync(evt.TenantId, payload.ClientId, cancellationToken))
                    {
                        await ScheduleActionAsync(
                            evt.TenantId,
                            "create_professional_task",
                            new CreateTaskAction(
                                payload.ClientId,
                                payload.NutritionistId,
                                "Planificar próxima cita",
                                "La última cita se ha completado y no existe una cita futura. Valorar la siguiente revisión y, si procede, la actualización de la dieta.",
                                DateTime.UtcNow.AddDays(14),
                                "normal",
                                "automation:appointment.completed"),
                            DateTime.UtcNow.AddDays(14),
                            evt.Id,
                            $"postappointment:next:{payload.ClientId}:{payload.AppointmentId}",
                            cancellationToken: cancellationToken);
                    }

                    await ScheduleActionAsync(
                        evt.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(
                            payload.ClientId,
                            payload.NutritionistId,
                            "Revisar resultado de la cita",
                            "Revisar la cita completada y confirmar si el plan de seguimiento, check-in o dieta requiere alguna acción.",
                            DateTime.UtcNow.AddDays(1),
                            "normal",
                            "automation:appointment.completed"),
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
                    await CancelPendingJobsByIdempotencyPrefixAsync(evt.TenantId, $"postappointment:next:{payload.ClientId}:", cancellationToken);

                    // Si la confirmación llega tarde, el recordatorio de 24 h se ejecuta cuanto antes; no se descarta
                    // por haber pasado su hora teórica, mientras que el de 2 h sí se omite si ya quedó atrás.
                    if (!await IsRuleEnabledAsync(evt.TenantId, "appointment.reminder.24h", cancellationToken) &&
                        !await IsRuleEnabledAsync(evt.TenantId, "appointment.reminder.2h", cancellationToken))
                        break;

                    var firstReminder = await ApplyConfiguredLeadTimeAsync(
                        evt.TenantId, "appointment.reminder.24h", payload.StartsAtUtc, 24 * 60, cancellationToken);

                    if (await IsRuleEnabledAsync(evt.TenantId, "appointment.reminder.24h", cancellationToken))
                    {
                        await ScheduleActionAsync(
                            evt.TenantId,
                            "notify_patient",
                            new NotifyPatientAction(
                                payload.ClientId,
                                "appointment_reminder",
                                "Recordatorio de cita",
                                "Recuerda que tienes una cita con tu nutricionista.",
                                "/patient?tab=appointments"),
                            firstReminder,
                            evt.Id,
                            $"event:{evt.Id}:reminder-24h",
                            cancellationToken: cancellationToken);
                    }

                    var secondReminder = await ApplyConfiguredLeadTimeAsync(
                        evt.TenantId, "appointment.reminder.2h", payload.StartsAtUtc, 2 * 60, cancellationToken);
                    if (secondReminder > DateTime.UtcNow &&
                        await IsRuleEnabledAsync(evt.TenantId, "appointment.reminder.2h", cancellationToken))
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
                    var payload = AutomationJson.Deserialize<AppointmentStatusPayload>(evt.Payload);
                    if (payload is not null)
                        await CancelPendingJobsByIdempotencyPrefixAsync(evt.TenantId, $"postappointment:next:{payload.ClientId}:", cancellationToken);

                    // Al cancelar una cita se invalidan los recordatorios pendientes asociados a la confirmación original,
                    // pero nunca jobs que ya estén en processing/completed o pertenezcan a otro agregado.
                    await CancelJobsForEventAggregateAsync(evt, cancellationToken);
                    break;
                }
            case "appointment.no_show":
                {
                    if (!await IsRuleEnabledAsync(evt.TenantId, "appointment.no_show", cancellationToken)) break;
                    var payload = AutomationJson.Deserialize<AppointmentStatusPayload>(evt.Payload)
                        ?? throw new InvalidOperationException("Payload inválido para appointment.no_show.");
                    // Si la cita se marca como no presentada antes de su hora, los recordatorios pendientes
                    // dejan de tener sentido. Se reutiliza la cancelación por agregado para invalidar únicamente
                    // los jobs de esta cita, igual que en una cancelación explícita.
                    await CancelJobsForEventAggregateAsync(evt, cancellationToken);

                    await ScheduleActionAsync(
                        evt.TenantId,
                        "create_professional_task",
                        new CreateTaskAction(payload.ClientId, payload.NutritionistId,
                            "Contactar paciente por ausencia a la cita",
                            "La cita ha quedado marcada como no presentada. Valorar contacto y reprogramación.",
                            await ApplyConfiguredDelayAsync(evt.TenantId, "appointment.no_show", DateTime.UtcNow.AddHours(24), cancellationToken), "high", "automation:appointment.no_show"),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:create-professional-task",
                        cancellationToken: cancellationToken);

                    await ScheduleActionAsync(
                        evt.TenantId,
                        "notify_patient",
                        new NotifyPatientAction(
                            payload.ClientId,
                            "appointment_no_show",
                            "No hemos podido realizar la cita",
                            "La cita ha quedado marcada como no presentada. Si necesitas continuar con tu seguimiento, puedes contactar con tu nutricionista para reprogramarla.",
                            "/patient?tab=appointments"),
                        DateTime.UtcNow,
                        evt.Id,
                        $"event:{evt.Id}:patient-notification",
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
               AND cd.end_date IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM client_diets newer
                   WHERE newer.client_id=cd.client_id
                     AND newer.is_active=true
                     AND newer.id <> cd.id
                     AND newer.start_date IS NOT NULL
                     AND newer.start_date > cd.end_date
               );
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
            if (daysRemaining is 7 or 3 or 1 or 0 &&
                await IsRuleEnabledAsync(diet.TenantId, "diet.expiry.reminder", cancellationToken))
            {
                await ScheduleActionAsync(
                    diet.TenantId,
                    "notify_patient",
                    new NotifyPatientAction(
                        diet.ClientId,
                        "diet_expiring",
                        daysRemaining == 0 ? "Tu dieta finaliza hoy" : "Tu dieta está próxima a finalizar",
                        daysRemaining == 0
                            ? $"Tu dieta \"{diet.DietName}\" finaliza hoy. Contacta con tu nutricionista si necesitas continuar."
                            : $"Tu dieta \"{diet.DietName}\" finaliza en {daysRemaining} día(s). Consulta con tu nutricionista si necesitas continuar o hacer cambios.",
                        "/patient?tab=diet"),
                    DateTime.UtcNow,
                    null,
                    $"diet:expiring:{diet.AssignmentId}:{diet.EndDate:yyyyMMdd}:{daysRemaining}",
                    cancellationToken: cancellationToken);
            }

            // Una dieta vencida genera tarea profesional inmediata en lugar de modificar automáticamente la dieta:
            // la decisión clínica de renovar, sustituir o finalizar queda deliberadamente en manos del profesional.
            if (diet.EndDate.Value < today &&
                await IsRuleEnabledAsync(diet.TenantId, "diet.expired", cancellationToken))
            {
                await ScheduleActionAsync(
                    diet.TenantId,
                    "notify_patient",
                    new NotifyPatientAction(
                        diet.ClientId,
                        "diet_expired",
                        "Tu dieta ha finalizado",
                        $"Tu dieta \"{diet.DietName}\" ha finalizado. Contacta con tu nutricionista para revisar el siguiente paso.",
                        "/patient?tab=diet"),
                    DateTime.UtcNow,
                    null,
                    $"diet:expired-notification:{diet.AssignmentId}:{diet.EndDate:yyyyMMdd}",
                    cancellationToken: cancellationToken);

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


    /// <summary>
    /// Cancela recordatorios de documentación que ya no son necesarios.
    /// Se limita al tenant y al prefijo de idempotencia de un paciente.
    /// </summary>
    public Task CancelPendingDocumentReminderJobsAsync(int tenantId, int clientId, CancellationToken cancellationToken = default)
        => CancelPendingJobsByIdempotencyPrefixAsync(
            tenantId,
            $"documents:pending-reminder:{clientId}:",
            cancellationToken);

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

        int? nutritionistId = null;
        await using (var assignmentConnection = new NpgsqlConnection(_connectionString))
        {
            await assignmentConnection.OpenAsync(cancellationToken);
            await using var assignmentCommand = new NpgsqlCommand("""
                SELECT nutritionist_id
                FROM client_nutritionist_assignments
                WHERE client_id=@client AND is_active
                ORDER BY assigned_at DESC
                LIMIT 1;
                """, assignmentConnection);
            assignmentCommand.Parameters.AddWithValue("client", clientId);
            var value = await assignmentCommand.ExecuteScalarAsync(cancellationToken);
            if (value is not null && value != DBNull.Value) nutritionistId = Convert.ToInt32(value);
        }

        try
        {
            await PublishEventAsync(
                tenantId,
                "patient.checkin.reviewed",
                "patient_checkin",
                checkinId.ToString(),
                new CheckinReviewedPayload(clientId, nutritionistId),
                $"checkin:{checkinId}:reviewed:{reviewerUserId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo registrar la automatización posterior a la revisión del check-in {CheckinId}.", checkinId);
        }

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
    public sealed record CheckinReviewedPayload(int ClientId, int? NutritionistId);
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
