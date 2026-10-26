using Npgsql;

namespace Anguloso.Server.Logica;

/// <summary>Recordatorio push persistente al paciente 24 horas antes de una cita confirmada.</summary>
public sealed class AppointmentDayReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _connectionString;
    private readonly ILogger<AppointmentDayReminderWorker> _logger;

    public AppointmentDayReminderWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AppointmentDayReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueRemindersAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando recordatorios de cita de 24 horas.");
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    private async Task ProcessDueRemindersAsync(CancellationToken ct)
    {
        var due = new List<Reminder>();
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT a.id,a.tenant_id,a.client_id,a.starts_at
                FROM patient_appointments a
                JOIN clients c ON c.id=a.client_id AND c.tenant_id=a.tenant_id
                WHERE a.status='confirmed' AND c.archived_at IS NULL
                  AND a.starts_at > NOW() + INTERVAL '23 hours 45 minutes'
                  AND a.starts_at <= NOW() + INTERVAL '24 hours 1 minute'
                  AND NOT EXISTS (
                    SELECT 1 FROM appointment_day_reminder_deliveries d
                    WHERE d.appointment_id=a.id
                      AND (d.status='sent' OR (d.status IN ('processing','partial') AND d.updated_at >= NOW() - INTERVAL '2 minutes')))
                ORDER BY a.starts_at
                LIMIT 100
                FOR UPDATE OF a SKIP LOCKED;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                due.Add(new Reminder(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                    DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)));
        }

        foreach (var item in due)
        {
            ct.ThrowIfCancellationRequested();
            await using (var connection = new NpgsqlConnection(_connectionString))
            {
                await connection.OpenAsync(ct);
                await using var claim = new NpgsqlCommand("""
                    INSERT INTO appointment_day_reminder_deliveries(appointment_id,tenant_id,client_id,scheduled_for,status,created_at,updated_at)
                    VALUES(@id,@tenant,@client,@starts,'processing',NOW(),NOW())
                    ON CONFLICT(appointment_id) DO UPDATE SET status='processing',updated_at=NOW(),last_error=NULL
                    WHERE appointment_day_reminder_deliveries.status IN ('partial','processing')
                      AND appointment_day_reminder_deliveries.updated_at < NOW() - INTERVAL '2 minutes'
                    RETURNING appointment_id;
                    """, connection);
                claim.Parameters.AddWithValue("id", item.Id);
                claim.Parameters.AddWithValue("tenant", item.TenantId);
                claim.Parameters.AddWithValue("client", item.ClientId);
                claim.Parameters.AddWithValue("starts", item.StartsAtUtc);
                if (await claim.ExecuteScalarAsync(ct) is null) continue;
            }

            var sent = false;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                await notifications.CreateForPatientAsync(item.TenantId, item.ClientId, "appointment_reminder_24h",
                    "Mañana tienes una cita", "Te recordamos que tienes una cita mañana. Consulta la hora y los detalles en DietoExpress.",
                    "/patient?tab=appointments", true, $"appointment-reminder-24h:{item.Id}:patient");
                var push = scope.ServiceProvider.GetRequiredService<PatientPushService>();
                await push.SendToClientAsync(item.ClientId, "Mañana tienes una cita",
                    "Te recordamos que tienes una cita mañana. Consulta la hora y los detalles en DietoExpress.",
                    "appointments", $"appointment-reminder-24h:{item.Id}:patient", ct);
                var professionalPush = scope.ServiceProvider.GetRequiredService<ProfessionalPushService>();
                await professionalPush.SendNotificationAsync(item.TenantId,
                    await GetNutritionistIdAsync(item.Id, ct),
                    "Cita mañana", "Tienes una cita mañana. Consulta tu agenda en DietoExpress.",
                    "/appointments", ct);
                sent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo completar el recordatorio de 24 horas de la cita {AppointmentId}.", item.Id);
            }
            await using var updateConnection = new NpgsqlConnection(_connectionString);
            await updateConnection.OpenAsync(ct);
            await using var update = new NpgsqlCommand("""
                UPDATE appointment_day_reminder_deliveries
                SET status=@status,last_error=@error,updated_at=NOW(),completed_at=CASE WHEN @done THEN NOW() ELSE completed_at END
                WHERE appointment_id=@id;
                """, updateConnection);
            update.Parameters.AddWithValue("status", sent ? "sent" : "partial");
            update.Parameters.AddWithValue("error", sent ? DBNull.Value : "No se pudo completar el envío del recordatorio.");
            update.Parameters.AddWithValue("done", sent);
            update.Parameters.AddWithValue("id", item.Id);
            await update.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<int> GetNutritionistIdAsync(int appointmentId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT nutritionist_id FROM patient_appointments WHERE id=@id;", connection);
        command.Parameters.AddWithValue("id", appointmentId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null ? throw new InvalidOperationException("No se encontró el profesional de la cita.") : Convert.ToInt32(value);
    }

    private sealed record Reminder(int Id, int TenantId, int ClientId, DateTime StartsAtUtc);
}
