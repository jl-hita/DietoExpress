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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutomationWorker iniciado.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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

    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var jobs = new List<AutomationJob>();
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var tx = await connection.BeginTransactionAsync(cancellationToken);

            // Recupera trabajos atascados por una caída/reinicio anterior.
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

        foreach (var job in jobs)
            await ExecuteJobAsync(job, cancellationToken);

        return jobs.Count;
    }

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

    private async Task FailJobAsync(AutomationJob job, Exception ex, DateTime started, CancellationToken cancellationToken)
    {
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
