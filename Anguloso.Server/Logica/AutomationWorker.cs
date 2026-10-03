using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Logica;

/// <summary>
/// Scheduler persistente. Reclama trabajos con SKIP LOCKED para permitir varias
/// instancias sin ejecutar el mismo trabajo simultáneamente.
/// </summary>
public sealed class AutomationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _connectionString;
    private readonly ILogger<AutomationWorker> _logger;

    public AutomationWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AutomationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
        _logger = logger;
    }

    // El worker ejecuta lotes pequeños de trabajos pendientes y espera entre ciclos para
    // evitar una consulta/ejecución continua contra PostgreSQL y los servicios externos.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutomationWorker iniciado.");
        var nextOnboardingSweep = DateTime.UtcNow;
        var nextLifecycleSweep = DateTime.UtcNow;
        var nextFollowUpSweep = DateTime.UtcNow;
        // Los barridos temporales se mantienen separados de la cola de jobs: si la cola está vacía,
        // siguen ejecutándose las comprobaciones periódicas de lifecycle, seguimiento y dietas.
        // Los tres barridos se ejecutan como reconciliaciones de baja frecuencia; una hora limita carga y, al usar claves idempotentes,
        // tolera que el proceso se reinicie entre dos ciclos sin perder ni duplicar las acciones derivadas.
        var nextDietSweep = DateTime.UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextOnboardingSweep)
                {
                    using var onboardingScope = _scopeFactory.CreateScope();
                    var automation = onboardingScope.ServiceProvider.GetRequiredService<AutomationService>();
                    await automation.RunPatientOnboardingAutomationSweepAsync(stoppingToken);
                    nextOnboardingSweep = DateTime.UtcNow.AddHours(1);
                }

                if (DateTime.UtcNow >= nextLifecycleSweep)
                {
                    using var lifecycleScope = _scopeFactory.CreateScope();
                    var automation = lifecycleScope.ServiceProvider.GetRequiredService<AutomationService>();
                    await automation.RunPatientLifecycleSweepAsync(stoppingToken);
                    nextLifecycleSweep = DateTime.UtcNow.AddHours(1);
                }

                if (DateTime.UtcNow >= nextFollowUpSweep)
                {
                    using var followUpScope = _scopeFactory.CreateScope();
                    var automation = followUpScope.ServiceProvider.GetRequiredService<AutomationService>();
                    await automation.RunFollowUpAutomationSweepAsync(stoppingToken);
                    nextFollowUpSweep = DateTime.UtcNow.AddHours(1);
                }


                if (DateTime.UtcNow >= nextDietSweep)
                {
                    using var dietScope = _scopeFactory.CreateScope();
                    var automation = dietScope.ServiceProvider.GetRequiredService<AutomationService>();
                    await automation.RunDietAutomationSweepAsync(stoppingToken);
                    nextDietSweep = DateTime.UtcNow.AddHours(1);
                }

                // La cola se procesa en lotes pequeños; el límite también evita que una ráfaga de trabajos monopolice una instancia.
                var processed = await ProcessBatchAsync(stoppingToken);
                if (processed == 0)
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el ciclo del AutomationWorker.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        _logger.LogInformation("AutomationWorker detenido.");
    }

    // Reclama un lote de trabajos de forma atómica. La combinación de transacción + SKIP LOCKED
    // permite varias instancias del servidor sin ejecutar simultáneamente el mismo trabajo.
    // Reserva trabajos de forma compatible con concurrencia para que varias instancias del
    // worker no procesen simultáneamente la misma automatización.
    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var jobs = new List<AutomationJob>();
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var tx = await connection.BeginTransactionAsync(cancellationToken);

            // Diez minutos es el umbral para distinguir un worker que sigue ejecutando una acción externa de un proceso que
            // probablemente murió. Es deliberadamente conservador: una recuperación prematura podría duplicar una acción externa,
            // por lo que las acciones que puedan repetirse deben apoyarse además en su propia idempotencia.
            await using (var recover = new NpgsqlCommand("""
                UPDATE automation_jobs
                SET status='pending', locked_at=NULL, updated_at=NOW()
                WHERE status='processing'
                  AND locked_at < NOW() - INTERVAL '10 minutes';
                """, connection, tx))
            {
                await recover.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new NpgsqlCommand("""
                SELECT id, tenant_id, event_id, action_type, payload, scheduled_at, attempts, max_attempts
                FROM automation_jobs
                WHERE status='pending' AND scheduled_at <= NOW()
                ORDER BY scheduled_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 20;
                """, connection, tx);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                jobs.Add(new AutomationJob(
                    reader.GetInt64(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt64(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetDateTime(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7)));
            }
            await reader.DisposeAsync();

            foreach (var job in jobs)
            {
                // El intento se incrementa al reclamar el job, no al terminarlo: así un proceso que muera durante una
                // llamada externa consume igualmente un intento y no puede reintentarse indefinidamente tras cada reinicio.
                await using var update = new NpgsqlCommand("""
                    UPDATE automation_jobs
                    SET status='processing', locked_at=NOW(), attempts=attempts+1, updated_at=NOW()
                    WHERE id=@id AND status='pending';
                    """, connection, tx);
                update.Parameters.AddWithValue("id", job.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await tx.CommitAsync(cancellationToken);
        }

        // Una vez liberada la transacción de reclamación, las llamadas externas se ejecutan fuera de la
        // transacción SQL para no mantener bloqueos mientras esperamos a email, push u otros servicios.
        foreach (var job in jobs)
            await ExecuteJobAsync(job, cancellationToken);

        return jobs.Count;
    }

    // Ejecuta una acción ya reclamada. Los errores se registran y el trabajo se reprograma o marca
    // como fallido según attempts/max_attempts, por lo que un fallo temporal no pierde el trabajo.
    private async Task ExecuteJobAsync(AutomationJob job, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        try
        {
            switch (job.ActionType)
            {
                case "create_professional_task":
                    await ExecuteCreateTaskAsync(job, cancellationToken);
                    break;
                case "notify_patient":
                    await ExecuteNotifyPatientAsync(job, cancellationToken);
                    break;
                case "email_patient":
                    await ExecuteEmailPatientAsync(job, cancellationToken);
                    break;
                case "email_billing_contact":
                    await ExecuteBillingEmailAsync(job, cancellationToken);
                    break;
                case "email_professional":
                    await ExecuteProfessionalEmailAsync(job, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Acción de automatización no soportada: {job.ActionType}");
            }

            await CompleteJobAsync(job.Id, started, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el trabajo de automatización {JobId}.", job.Id);
            await FailJobAsync(job, ex, started, cancellationToken);
        }
    }

    // La creación usa como idempotency key el propio job: si el worker ejecuta la acción y cae antes de marcarla
    // completada, el reintento no crea una segunda tarea profesional.
    private async Task ExecuteCreateTaskAsync(AutomationJob job, CancellationToken cancellationToken)
    {
        var action = AutomationJson.Deserialize<CreateTaskAction>(job.Payload)
            ?? throw new InvalidOperationException("Payload inválido para create_professional_task.");

        using var scope = _scopeFactory.CreateScope();
        var automation = scope.ServiceProvider.GetRequiredService<AutomationService>();

        await automation.CreateProfessionalTaskAsync(
            job.TenantId,
            new ProfessionalTaskCreateRequest
            {
                ClientId = action.ClientId,
                AssignedUserId = action.AssignedUserId,
                Title = action.Title,
                Description = action.Description,
                DueAt = action.DueAt,
                Priority = action.Priority
            },
            action.Source,
            $"job:{job.Id}",
            cancellationToken);
    }


    // La notificación se crea a través de NotificationService en un scope propio; el worker no conserva estado
    // de petición HTTP y cada ejecución obtiene sus dependencias desde un ámbito independiente.
    private async Task ExecuteNotifyPatientAsync(AutomationJob job, CancellationToken cancellationToken)
    {
        var action = AutomationJson.Deserialize<NotifyPatientAction>(job.Payload)
            ?? throw new InvalidOperationException("Payload inválido para notify_patient.");

        using var scope = _scopeFactory.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        await notifications.CreateForPatientAsync(
            job.TenantId,
            action.ClientId,
            action.Type,
            action.Title,
            action.Message,
            action.ActionUrl,
            action.SendPush);
    }

    private async Task ExecuteEmailPatientAsync(AutomationJob job, CancellationToken cancellationToken)
    {
        var action = AutomationJson.Deserialize<EmailPatientAction>(job.Payload)
            ?? throw new InvalidOperationException("Payload inválido para email_patient.");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT email FROM clients WHERE id=@client AND tenant_id=@tenant AND archived_at IS NULL;",
            connection);
        command.Parameters.AddWithValue("client", action.ClientId);
        command.Parameters.AddWithValue("tenant", job.TenantId);
        var email = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (!string.IsNullOrWhiteSpace(email))
        {
            await using var preferenceCommand = new NpgsqlCommand("SELECT email_enabled FROM patient_communication_preferences WHERE tenant_id=@tenant AND client_id=@client LIMIT 1;", connection);
            preferenceCommand.Parameters.AddWithValue("tenant", job.TenantId);
            preferenceCommand.Parameters.AddWithValue("client", action.ClientId);
            var preference = await preferenceCommand.ExecuteScalarAsync(cancellationToken);
            if (preference is bool enabled && !enabled)
                return;
        }
        // El destinatario se resuelve al ejecutar el job, no al programarlo, para que una corrección posterior del
        // email del paciente pueda hacer recuperable un job que falló por datos incompletos.
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("El paciente no tiene un email válido.");

        using var scope = _scopeFactory.CreateScope();
        var emailServ = scope.ServiceProvider.GetRequiredService<EmailServ>();
        var result = await emailServ.SendEmailAsync(email, action.Subject, action.HtmlBody);
        if (!result.Exito)
            throw new InvalidOperationException(result.Mensaje);
    }

    private async Task ExecuteProfessionalEmailAsync(AutomationJob job, CancellationToken cancellationToken)
    {
        var action = AutomationJson.Deserialize<ProfessionalEmailAction>(job.Payload)
            ?? throw new InvalidOperationException("Payload inválido para email_professional.");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT email
            FROM users
            WHERE id=@user
              AND tenant_id=@tenant
              AND archived_at IS NULL
              AND role <> 'superadmin'
              AND email IS NOT NULL
              AND TRIM(email) <> ''
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("user", action.UserId);
        command.Parameters.AddWithValue("tenant", job.TenantId);
        var email = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(email))
            return;

        using var scope = _scopeFactory.CreateScope();
        var emailServ = scope.ServiceProvider.GetRequiredService<EmailServ>();
        var result = await emailServ.SendEmailAsync(email, action.Subject, action.HtmlBody);
        if (!result.Exito)
            throw new InvalidOperationException(result.Mensaje);
    }

    private async Task ExecuteBillingEmailAsync(AutomationJob job, CancellationToken cancellationToken)
    {
        var action = AutomationJson.Deserialize<AutomationService.BillingEmailAction>(job.Payload)
            ?? throw new InvalidOperationException("Payload inválido para email_billing_contact.");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT email
            FROM users
            WHERE tenant_id=@tenant
              AND archived_at IS NULL
              AND role <> 'superadmin'
              AND (@user_id IS NULL OR id=@user_id)
              AND email IS NOT NULL
              AND TRIM(email) <> ''
            ORDER BY CASE WHEN role='clinic_admin' THEN 0 ELSE 1 END, created_at, id
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("tenant", job.TenantId);
        command.Parameters.AddWithValue("user_id", (object?)action.UserId ?? DBNull.Value);
        var email = await command.ExecuteScalarAsync(cancellationToken) as string;
        // El destinatario de facturación se resuelve dinámicamente y tiene fallback dentro del tenant. Si no existe
        // ningún destinatario válido, no hay una acción de envío que reintentar y el evento no debe quedar bloqueado.
        if (string.IsNullOrWhiteSpace(email))
            return;

        using var scope = _scopeFactory.CreateScope();
        var emailServ = scope.ServiceProvider.GetRequiredService<EmailServ>();
        var result = await emailServ.SendEmailAsync(email, action.Subject, action.HtmlBody);
        if (!result.Exito)
            throw new InvalidOperationException(result.Mensaje);
    }

    // El historial de ejecución se escribe junto con el cambio de estado en la misma conexión, de modo que una
    // ejecución marcada como completada siempre deja también su traza de duración/resultados; el historial refleja
    // intentos individuales y no sustituye al estado durable del job.
    private async Task CompleteJobAsync(long jobId, DateTime started, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE automation_jobs
            SET status='completed', completed_at=NOW(), locked_at=NULL, last_error=NULL, updated_at=NOW()
            WHERE id=@id;
            """, connection);
        command.Parameters.AddWithValue("id", jobId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await WriteExecutionAsync(connection, jobId, "completed", null, DateTime.UtcNow - started, cancellationToken);
    }

    // Un fallo queda registrado como ejecución y decide el siguiente intento según la política
    // de reintentos, manteniendo trazabilidad sin perder el trabajo original.
    private async Task FailJobAsync(AutomationJob job, Exception ex, DateTime started, CancellationToken cancellationToken)
    {
        // El backoff crece por intento hasta 5 minutos para no castigar continuamente servicios externos que estén
        // temporalmente degradados. La comparación usa el intento ya consumido al reclamar el job.
        var retry = job.Attempts < job.MaxAttempts;
        var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Max(0, job.Attempts - 1)) * 5));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(retry
            ? """
              UPDATE automation_jobs
              SET status='pending',
                  scheduled_at=NOW() + (@delay * INTERVAL '1 second'),
                  locked_at=NULL,
                  last_error=@error,
                  updated_at=NOW()
              WHERE id=@id;
              """
            : """
              UPDATE automation_jobs
              SET status='failed', locked_at=NULL, last_error=@error, updated_at=NOW()
              WHERE id=@id;
              """, connection);
        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("error", ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message);
        if (retry) command.Parameters.AddWithValue("delay", delay.TotalSeconds);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await WriteExecutionAsync(connection, job.Id, retry ? "retrying" : "failed", ex.Message, DateTime.UtcNow - started, cancellationToken);
    }

    private static async Task WriteExecutionAsync(
        NpgsqlConnection connection,
        long jobId,
        string result,
        string? error,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO automation_executions(job_id, result, error, duration_ms, executed_at)
            VALUES (@job, @result, @error, @duration, NOW());
            """, connection);
        command.Parameters.AddWithValue("job", jobId);
        command.Parameters.AddWithValue("result", result);
        command.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("duration", (long)Math.Max(0, duration.TotalMilliseconds));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
