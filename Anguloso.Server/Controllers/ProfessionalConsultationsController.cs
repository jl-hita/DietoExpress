using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/professional/consultations")]
[Authorize(Policy = "Professional")]
public sealed class ProfessionalConsultationsController : ControllerBase
{
    private static readonly HashSet<string> AllowedSteps = new(StringComparer.OrdinalIgnoreCase)
    {
        "summary", "evolution", "checkin", "goals", "diet", "education", "tasks", "next_appointment", "close"
    };

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "first", "follow_up", "quick"
    };

    private readonly angulosodbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ITenantContextService _tenantContext;

    public ProfessionalConsultationsController(angulosodbContext db, IConfiguration configuration, ITenantContextService tenantContext)
    {
        _db = db;
        _configuration = configuration;
        _tenantContext = tenantContext;
    }

    public sealed record StartConsultationRequest(string? ConsultationType);
    public sealed record UpdateProgressRequest(string? CurrentStep, string[]? CompletedSteps, JsonElement? Progress);

    [HttpGet("appointment/{appointmentId:int}")]
    public async Task<IActionResult> GetByAppointment(int appointmentId)
    {
        var appointment = await GetAuthorizedAppointmentAsync(appointmentId);
        if (appointment == null) return NotFound();

        var consultation = await ReadConsultationAsync(appointmentId, _tenantContext.TenantId!.Value);
        var latestCheckin = await _db.patient_checkins.AsNoTracking()
            .Where(c => c.tenant_id == appointment.TenantId && c.client_id == appointment.ClientId)
            .OrderByDescending(c => c.submitted_at)
            .Select(c => new
            {
                c.id, c.submitted_at, c.weight, c.adherence, c.hunger, c.energy,
                c.sleep_quality, c.sleep_hours, c.training, c.difficulties, c.notes, c.reviewed_at
            })
            .FirstOrDefaultAsync();

        var previousConsultationExists = await _db.patient_appointments.AsNoTracking()
            .AnyAsync(a => a.tenant_id == appointment.TenantId && a.client_id == appointment.ClientId &&
                           a.id != appointmentId && a.status == "completed");

        var previousAppointment = await _db.patient_appointments.AsNoTracking()
            .Where(a => a.tenant_id == appointment.TenantId && a.client_id == appointment.ClientId &&
                        a.id != appointmentId && a.status == "completed" && a.starts_at < appointment.StartsAt)
            .OrderByDescending(a => a.starts_at)
            .Select(a => new { a.id, a.starts_at, a.ends_at, a.professional_notes })
            .FirstOrDefaultAsync();

        var activeDiet = await _db.client_diets.AsNoTracking()
            .Where(cd => cd.client_id == appointment.ClientId && cd.is_active == true &&
                         cd.diet != null && cd.diet!.tenant_id == appointment.TenantId)
            .OrderByDescending(cd => cd.id)
            .Select(cd => new
            {
                cd.diet!.id, cd.diet.name, cd.diet.target_kcal, cd.diet.target_protein,
                cd.diet.target_carbs, cd.diet.target_fat, cd.diet.created_at
            })
            .FirstOrDefaultAsync();

        var openTasks = new List<object>();
        await using (var taskConnection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            await taskConnection.OpenAsync();
            await using var taskCommand = new NpgsqlCommand("""
                SELECT id, title, description, priority, due_at, created_at
                FROM professional_tasks
                WHERE tenant_id=@tenant AND client_id=@client AND status='open'
                ORDER BY due_at NULLS LAST, created_at DESC
                LIMIT 10;
                """, taskConnection);
            taskCommand.Parameters.AddWithValue("tenant", appointment.TenantId);
            taskCommand.Parameters.AddWithValue("client", appointment.ClientId);
            await using var taskReader = await taskCommand.ExecuteReaderAsync();
            while (await taskReader.ReadAsync())
            {
                openTasks.Add(new
                {
                    id = taskReader.GetInt64(0),
                    title = taskReader.GetString(1),
                    description = taskReader.IsDBNull(2) ? null : taskReader.GetString(2),
                    priority = taskReader.GetString(3),
                    dueAt = taskReader.IsDBNull(4) ? (DateTime?)null : taskReader.GetDateTime(4),
                    createdAt = taskReader.GetDateTime(5)
                });
            }
        }

        var followupSignals = new List<object>();
        if (latestCheckin != null)
        {
            if (latestCheckin.adherence.HasValue && latestCheckin.adherence.Value <= 5) followupSignals.Add(new { code = "adherence_low", label = "Adherencia baja", value = latestCheckin.adherence });
            if (latestCheckin.hunger.HasValue && latestCheckin.hunger.Value >= 8) followupSignals.Add(new { code = "hunger_high", label = "Hambre elevada", value = latestCheckin.hunger });
            if (latestCheckin.energy.HasValue && latestCheckin.energy.Value <= 4) followupSignals.Add(new { code = "energy_low", label = "Energía baja", value = latestCheckin.energy });
            if (latestCheckin.sleep_quality.HasValue && latestCheckin.sleep_quality.Value <= 4) followupSignals.Add(new { code = "sleep_quality_low", label = "Sueño mejorable", value = latestCheckin.sleep_quality });
            if (latestCheckin.sleep_hours.HasValue && latestCheckin.sleep_hours.Value <= 6) followupSignals.Add(new { code = "sleep_hours_low", label = "Pocas horas de sueño", value = latestCheckin.sleep_hours });
            if (latestCheckin.training.HasValue && latestCheckin.training.Value <= 2) followupSignals.Add(new { code = "training_low", label = "Entrenamiento bajo", value = latestCheckin.training });
        }

        return Ok(new
        {
            appointment = new
            {
                id = appointment.Id, startsAt = appointment.StartsAt, endsAt = appointment.EndsAt,
                status = appointment.Status, clientId = appointment.ClientId, clientName = appointment.ClientName,
                nutritionistId = appointment.NutritionistId
            },
            suggestedConsultationType = previousConsultationExists ? "follow_up" : "first",
            consultation, latestCheckin, previousAppointment, activeDiet, openTasks, followupSignals
        });
    }

    [HttpPost("appointment/{appointmentId:int}/start")]
    public async Task<IActionResult> Start(int appointmentId, [FromBody] StartConsultationRequest? request)
    {
        var appointment = await GetAuthorizedAppointmentAsync(appointmentId);
        if (appointment == null) return NotFound();
        if (appointment.Status != "confirmed")
            return Conflict(new { message = "La consulta solo puede iniciarse desde una cita confirmada." });

        var existing = await ReadConsultationAsync(appointmentId, appointment.TenantId);
        if (existing != null) return Ok(existing);

        var previousConsultationExists = await _db.patient_appointments.AsNoTracking()
            .AnyAsync(a => a.tenant_id == appointment.TenantId && a.client_id == appointment.ClientId &&
                           a.id != appointmentId && a.status == "completed");

        var consultationType = string.IsNullOrWhiteSpace(request?.ConsultationType)
            ? (previousConsultationExists ? "follow_up" : "first")
            : request!.ConsultationType!.Trim().ToLowerInvariant();

        if (!AllowedTypes.Contains(consultationType))
            return BadRequest(new { message = "Tipo de consulta no válido." });

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO professional_consultations
                (tenant_id, appointment_id, client_id, professional_id, consultation_type, status, current_step, progress, completed_steps)
            VALUES (@tenant,@appointment,@client,@professional,@type,'in_progress','summary','{}'::jsonb,'[]'::jsonb)
            ON CONFLICT (tenant_id, appointment_id) DO NOTHING;
            """, connection);
        command.Parameters.AddWithValue("tenant", appointment.TenantId);
        command.Parameters.AddWithValue("appointment", appointment.Id);
        command.Parameters.AddWithValue("client", appointment.ClientId);
        command.Parameters.AddWithValue("professional", appointment.NutritionistId);
        command.Parameters.AddWithValue("type", consultationType);
        await command.ExecuteNonQueryAsync();

        return Ok(await ReadConsultationAsync(appointmentId, appointment.TenantId));
    }

    [HttpPut("appointment/{appointmentId:int}/progress")]
    public async Task<IActionResult> UpdateProgress(int appointmentId, [FromBody] UpdateProgressRequest request)
    {
        var appointment = await GetAuthorizedAppointmentAsync(appointmentId);
        if (appointment == null) return NotFound();

        var step = string.IsNullOrWhiteSpace(request.CurrentStep) ? "summary" : request.CurrentStep.Trim().ToLowerInvariant();
        if (!AllowedSteps.Contains(step)) return BadRequest(new { message = "Paso de consulta no válido." });

        var completedSteps = (request.CompletedSteps ?? Array.Empty<string>())
            .Select(x => x.Trim().ToLowerInvariant()).Where(AllowedSteps.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var progressJson = request.Progress.HasValue && request.Progress.Value.ValueKind == JsonValueKind.Object
            ? request.Progress.Value.GetRawText() : "{}";

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE professional_consultations
            SET current_step=@step, completed_steps=@completed::jsonb, progress=@progress::jsonb, updated_at=NOW()
            WHERE tenant_id=@tenant AND appointment_id=@appointment;
            """, connection);
        command.Parameters.AddWithValue("tenant", appointment.TenantId);
        command.Parameters.AddWithValue("appointment", appointmentId);
        command.Parameters.AddWithValue("step", step);
        command.Parameters.AddWithValue("completed", JsonSerializer.Serialize(completedSteps));
        command.Parameters.AddWithValue("progress", progressJson);

        var affected = await command.ExecuteNonQueryAsync();
        if (affected == 0) return NotFound();
        return Ok(await ReadConsultationAsync(appointmentId, appointment.TenantId));
    }

    [HttpPost("appointment/{appointmentId:int}/complete")]
    public async Task<IActionResult> Complete(int appointmentId)
    {
        var appointment = await GetAuthorizedAppointmentAsync(appointmentId);
        if (appointment == null) return NotFound();

        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using var command = new NpgsqlCommand("""
            UPDATE professional_consultations
            SET status='completed', current_step='close',
                completed_steps='["summary","evolution","checkin","goals","diet","education","tasks","next_appointment","close"]'::jsonb,
                completed_at=NOW(), updated_at=NOW()
            WHERE tenant_id=@tenant AND appointment_id=@appointment AND status='in_progress';
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", appointment.TenantId);
        command.Parameters.AddWithValue("appointment", appointmentId);

        var affected = await command.ExecuteNonQueryAsync();
        if (affected == 0)
        {
            await transaction.RollbackAsync();
            return Conflict(new { message = "La consulta no está en curso o ya ha sido cerrada." });
        }

        await using var appointmentCommand = new NpgsqlCommand("""
            UPDATE patient_appointments
            SET status='completed', updated_at=NOW()
            WHERE id=@appointment AND tenant_id=@tenant AND status='confirmed';
            """, connection, transaction);
        appointmentCommand.Parameters.AddWithValue("appointment", appointmentId);
        appointmentCommand.Parameters.AddWithValue("tenant", appointment.TenantId);
        await appointmentCommand.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
        return Ok(await ReadConsultationAsync(appointmentId, appointment.TenantId));
    }

    private async Task<AuthorizedAppointment?> GetAuthorizedAppointmentAsync(int appointmentId)
    {
        if (!_tenantContext.TenantId.HasValue || !_tenantContext.UserId.HasValue) return null;

        var appointment = await _db.patient_appointments.AsNoTracking()
            .Where(a => a.id == appointmentId && a.tenant_id == _tenantContext.TenantId.Value && a.client.archived_at == null)
            .Select(a => new AuthorizedAppointment
            {
                Id = a.id, TenantId = a.tenant_id, ClientId = a.client_id, ClientName = a.client.full_name,
                NutritionistId = a.nutritionist_id, StartsAt = a.starts_at, EndsAt = a.ends_at, Status = a.status
            })
            .SingleOrDefaultAsync();

        if (appointment == null) return null;
        if (!User.IsInRole("clinic_admin") && appointment.NutritionistId != _tenantContext.UserId.Value) return null;
        return appointment;
    }

    private async Task<object?> ReadConsultationAsync(int appointmentId, int tenantId)
    {
        await using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT id, client_id, professional_id, consultation_type, status, current_step,
                   progress, completed_steps, started_at, completed_at, created_at, updated_at
            FROM professional_consultations
            WHERE tenant_id=@tenant AND appointment_id=@appointment LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("appointment", appointmentId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new
        {
            id = reader.GetInt64(0), clientId = reader.GetInt32(1), professionalId = reader.GetInt32(2),
            consultationType = reader.GetString(3), status = reader.GetString(4), currentStep = reader.GetString(5),
            progress = JsonSerializer.Deserialize<object>(reader.GetFieldValue<string>(6)) ?? new { },
            completedSteps = JsonSerializer.Deserialize<string[]>(reader.GetFieldValue<string>(7)) ?? Array.Empty<string>(),
            startedAt = reader.GetDateTime(8),
            completedAt = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9),
            createdAt = reader.GetDateTime(10),
            updatedAt = reader.IsDBNull(11) ? (DateTime?)null : reader.GetDateTime(11)
        };
    }

    private sealed class AuthorizedAppointment
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int ClientId { get; set; }
        public string? ClientName { get; set; }
        public int NutritionistId { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
