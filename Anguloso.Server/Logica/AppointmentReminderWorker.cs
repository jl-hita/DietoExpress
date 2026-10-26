using Npgsql;

namespace Anguloso.Server.Logica;

/// <summary>Envía recordatorios push persistentes para citas confirmadas que comienzan dentro de 15 minutos.</summary>
public sealed class AppointmentReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _connectionString;
    private readonly ILogger<AppointmentReminderWorker> _logger;

    public AppointmentReminderWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AppointmentReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AppointmentReminderWorker iniciado.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
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
                _logger.LogError(ex, "Error procesando recordatorios de citas.");
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
        _logger.LogInformation("AppointmentReminderWorker detenido.");
    }

    private async Task ProcessDueRemindersAsync(CancellationToken ct)
    {
        // Ventana amplia frente al ciclo de 30 s para tolerar pequeños retrasos del worker; el ledger impide repetir avisos.
        var due = new List<Reminder>();
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT a.id,a.tenant_id,a.client_id,a.nutritionist_id,c.full_name,a.starts_at
                FROM patient_appointments a
                JOIN clients c ON c.id=a.client_id AND c.tenant_id=a.tenant_id
                WHERE a.status='confirmed'
                  AND c.archived_at IS NULL
                  AND (
                    (a.starts_at > NOW() + INTERVAL '13 minutes'
                     AND a.starts_at <= NOW() + INTERVAL '15 minutes 30 seconds'
                     AND NOT EXISTS (SELECT 1 FROM appointment_reminder_deliveries d WHERE d.appointment_id=a.id))
                    OR
                    (a.starts_at > NOW()
                     AND EXISTS (SELECT 1 FROM appointment_reminder_deliveries d WHERE d.appointment_id=a.id
                                 AND d.status IN ('partial','processing')
                                 AND d.updated_at < NOW() - INTERVAL '2 minutes'))
                  )
                  ORDER BY a.starts_at
                LIMIT 100
                FOR UPDATE OF a SKIP LOCKED;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                due.Add(new Reminder(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4), DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)));
        }

        foreach (var item in due)
        {
            ct.ThrowIfCancellationRequested();
            // Insertar el ledger primero da un punto idempotente tras reinicios. El envío al paciente también
            // usa la clave de idempotencia de NotificationService, de modo que no se crean avisos internos duplicados.
            await using (var connection = new NpgsqlConnection(_connectionString))
            {
                await connection.OpenAsync(ct);
                await using var command = new NpgsqlCommand("""
                    INSERT INTO appointment_reminder_deliveries(appointment_id,tenant_id,client_id,nutritionist_id,scheduled_for,status,created_at,updated_at)
                    VALUES(@id,@tenant,@client,@nutritionist,@starts,'processing',NOW(),NOW())
                    ON CONFLICT(appointment_id) DO UPDATE SET status='processing',updated_at=NOW(),last_error=NULL
                    WHERE appointment_reminder_deliveries.status IN ('partial','processing')
                      AND appointment_reminder_deliveries.updated_at < NOW() - INTERVAL '2 minutes'
                    RETURNING appointment_id;
                    """, connection);
                command.Parameters.AddWithValue("id", item.Id);
                command.Parameters.AddWithValue("tenant", item.TenantId);
                command.Parameters.AddWithValue("client", item.ClientId);
                command.Parameters.AddWithValue("nutritionist", item.NutritionistId);
                command.Parameters.AddWithValue("starts", item.StartsAtUtc);
                if (await command.ExecuteScalarAsync(ct) is null) continue;
            }

            var patientSent = false;
            var professionalSent = false;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                await notifications.CreateForPatientAsync(item.TenantId, item.ClientId, "appointment_reminder",
                    "Tu cita es en 15 minutos", "Recuerda que tu cita comienza dentro de unos 15 minutos.",
                    "/patient?tab=appointments", true, $"appointment-reminder:{item.Id}:patient");
                var patientPush = scope.ServiceProvider.GetRequiredService<PatientPushService>();
                await patientPush.SendToClientAsync(item.ClientId, "Tu cita es en 15 minutos",
                    "Recuerda que tu cita comienza dentro de unos 15 minutos.", "appointments", ct);
                patientSent = true;
                var professionalPush = scope.ServiceProvider.GetRequiredService<ProfessionalPushService>();
                // Avisar al nutricionista asignado y a los administradores de la clínica que hayan activado push.
                // El servicio no envía nada a usuarios sin suscripción registrada.
                await professionalPush.SendAppointmentReminderAsync(item.TenantId, item.NutritionistId, item.Id, item.ClientName, item.StartsAtUtc, ct);
                foreach (var clinicAdminId in await GetClinicAdminIdsAsync(item.TenantId, ct))
                    await professionalPush.SendAppointmentReminderAsync(item.TenantId, clinicAdminId, item.Id, item.ClientName, item.StartsAtUtc, ct);
                professionalSent = true;
            }
            catch (Exception ex) { _logger.LogError(ex, "No se pudo completar el recordatorio de la cita {AppointmentId}.", item.Id); }
            finally
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                await using var command = new NpgsqlCommand("""
                    UPDATE appointment_reminder_deliveries
                    SET status=@status,last_error=@error,updated_at=NOW(),completed_at=CASE WHEN @done THEN NOW() ELSE completed_at END
                    WHERE appointment_id=@id;
                    """, connection);
                command.Parameters.AddWithValue("status", patientSent && professionalSent ? "sent" : "partial");
                command.Parameters.AddWithValue("error", patientSent && professionalSent ? DBNull.Value : "Uno o ambos canales no pudieron completarse.");
                command.Parameters.AddWithValue("done", patientSent && professionalSent);
                command.Parameters.AddWithValue("id", item.Id);
                await command.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private async Task<List<int>> GetClinicAdminIdsAsync(int tenantId, CancellationToken ct)
    {
        var ids = new List<int>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT id FROM users WHERE tenant_id=@tenant AND role='clinic_admin';", connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetInt32(0));
        return ids;
    }

    private sealed record Reminder(int Id, int TenantId, int ClientId, int NutritionistId, string ClientName, DateTime StartsAtUtc);
}
