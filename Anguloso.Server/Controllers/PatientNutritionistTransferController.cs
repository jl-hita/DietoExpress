using System.Security.Claims;
using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/portal/nutritionist-transfer")]
[Authorize(Roles = "patient")]
public sealed class PatientNutritionistTransferController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly EmailServ _email;
    private readonly GoogleCalendarService _googleCalendar;
    private readonly ILogger<PatientNutritionistTransferController> _logger;

    public PatientNutritionistTransferController(
        angulosodbContext context,
        EmailServ email,
        GoogleCalendarService googleCalendar,
        ILogger<PatientNutritionistTransferController> logger)
    {
        _context = context;
        _email = email;
        _googleCalendar = googleCalendar;
        _logger = logger;
    }

    private int? ClientId
    {
        get
        {
            var raw = User.FindFirstValue("clientId");
            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    public sealed class TransferStatusRow
    {
        public long Id { get; set; }
        public string Status { get; set; } = "";
        public int AppointmentId { get; set; }
        public DateTime AppointmentStartsAt { get; set; }
        public string NutritionistName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public sealed class TransferRow
    {
        public long Id { get; set; }
        public int SourceTenantId { get; set; }
        public int TargetTenantId { get; set; }
        public int ClientId { get; set; }
        public int SourceNutritionistId { get; set; }
        public int TargetNutritionistId { get; set; }
        public int AppointmentId { get; set; }
        public string Status { get; set; } = "";
    }

    [HttpGet]
    public async Task<IActionResult> GetCurrent()
    {
        var clientId = ClientId;
        if (clientId == null) return Unauthorized();
        var workflow = await _context.Database.SqlQueryRaw<TransferStatusRow>(
            @"SELECT w.id AS ""Id"", w.status AS ""Status"", w.appointment_id AS ""AppointmentId"",
                     a.starts_at AS ""AppointmentStartsAt"", COALESCE(u.full_name, 'Nutricionista') AS ""NutritionistName"",
                     w.created_at AS ""CreatedAt""
              FROM patient_nutritionist_transfer_workflows w
              JOIN patient_appointments a ON a.id = w.appointment_id
              JOIN users u ON u.id = w.target_nutritionist_id
              WHERE w.client_id = {0}
              ORDER BY w.created_at DESC LIMIT 1", clientId.Value)
            .SingleOrDefaultAsync();
        if (workflow == null) return Ok(new { pending = false });
        return Ok(new
        {
            pending = workflow.Status is "pending_professional" or "awaiting_patient_confirmation",
            workflow.Id,
            status = workflow.Status,
            workflow.AppointmentId,
            workflow.AppointmentStartsAt,
            workflow.NutritionistName,
            workflow.CreatedAt,
            requiresPatientConfirmation = workflow.Status == "awaiting_patient_confirmation"
        });
    }

    public sealed class ConfirmTransferRequest
    {
        public bool ConsentToHistoryTransfer { get; set; }
    }

    [HttpPost("{id:long}/confirm")]
    public async Task<IActionResult> Confirm(long id, [FromBody] ConfirmTransferRequest request)
    {
        var clientId = ClientId;
        if (clientId == null) return Unauthorized();
        if (request?.ConsentToHistoryTransfer != true)
            return BadRequest(new { message = "Debes consentir expresamente la transferencia de tu historial." });

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var workflow = await _context.Database.SqlQueryRaw<TransferRow>(
            @"SELECT id AS ""Id"", source_tenant_id AS ""SourceTenantId"", target_tenant_id AS ""TargetTenantId"",
                     client_id AS ""ClientId"", source_nutritionist_id AS ""SourceNutritionistId"",
                     target_nutritionist_id AS ""TargetNutritionistId"", appointment_id AS ""AppointmentId"",
                     status AS ""Status""
              FROM patient_nutritionist_transfer_workflows
              WHERE id = {0} AND client_id = {1}
              FOR UPDATE", id, clientId.Value).SingleOrDefaultAsync();

        if (workflow == null) return NotFound();
        if (workflow.Status == "confirmed")
            return Ok(new { completed = true, message = "El cambio y la transferencia ya se habían completado." });
        if (workflow.Status != "awaiting_patient_confirmation")
            return Conflict(new { message = "Solo puedes confirmar el cambio después de que el nuevo profesional acepte la cita." });

        var appointmentConfirmed = await _context.patient_appointments.AsNoTracking().AnyAsync(a =>
            a.id == workflow.AppointmentId && a.client_id == clientId.Value &&
            a.nutritionist_id == workflow.TargetNutritionistId &&
            a.tenant_id == workflow.TargetTenantId && a.status == "confirmed");
        if (!appointmentConfirmed)
            return Conflict(new { message = "La cita ya no está confirmada. Tu nutricionista actual no ha cambiado." });

        var oldProfessional = await _context.users.AsNoTracking()
            .Where(u => u.id == workflow.SourceNutritionistId && u.tenant_id == workflow.SourceTenantId)
            .Select(u => new { u.full_name, u.email }).SingleOrDefaultAsync();

        var cancelledAppointmentIds = new List<int>();
        try
        {
            // Trasladar los registros que se relacionan por client_id y tenant_id dentro
            // de la misma transacción. Los nombres de tabla/columna proceden del catálogo
            // PostgreSQL y se citan allí; los valores siempre son parámetros.
            var tables = await _context.Database.SqlQueryRaw<string>(
                @"SELECT format('%I.%I', c.table_schema, c.table_name) AS ""Value""
                  FROM information_schema.columns c
                  JOIN information_schema.columns t
                    ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                   AND t.column_name = 'tenant_id'
                  WHERE c.table_schema = 'public' AND c.column_name = 'client_id'
                  GROUP BY c.table_schema, c.table_name
                  ORDER BY c.table_name").ToListAsync();

            foreach (var table in tables)
            {
                // EF1003 is suppressed only for this statement: PostgreSQL format('%I.%I')
                // quotes the identifier from information_schema; data values remain parameters.
#pragma warning disable EF1003
                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE " + table + " SET tenant_id = {0} WHERE client_id = {1} AND tenant_id = {2}",
                    workflow.TargetTenantId, workflow.ClientId, workflow.SourceTenantId);
#pragma warning restore EF1003
            }

            // Capturar los identificadores antes de cancelar para poder reflejar el cambio
            // también en el calendario externo del profesional anterior después del commit.
            cancelledAppointmentIds = await _context.patient_appointments.AsNoTracking()
                .Where(a => a.client_id == workflow.ClientId
                    && a.tenant_id == workflow.TargetTenantId
                    && a.nutritionist_id == workflow.SourceNutritionistId
                    && a.starts_at > DateTime.UtcNow
                    && (a.status == "requested" || a.status == "confirmed"))
                .Select(a => a.id)
                .ToListAsync();

            // Cancelar todas las citas futuras del profesional anterior, tanto solicitadas
            // como confirmadas. Las citas pasadas y las del nuevo profesional se conservan.
            // Se hace después de trasladar tenant_id y dentro de la transacción de transferencia.
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE patient_appointments
                SET status = 'cancelled',
                    updated_at = NOW(),
                    video_room_name = NULL,
                    video_room_url = NULL,
                    video_provider = NULL,
                    video_expires_at = NULL
                WHERE client_id = {workflow.ClientId}
                  AND tenant_id = {workflow.TargetTenantId}
                  AND nutritionist_id = {workflow.SourceNutritionistId}
                  AND starts_at > NOW()
                  AND status IN ('requested', 'confirmed')");

            var clientUpdated = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE clients
                SET tenant_id = {workflow.TargetTenantId}, user_id = {workflow.TargetNutritionistId}
                WHERE id = {workflow.ClientId} AND tenant_id = {workflow.SourceTenantId} AND archived_at IS NULL");
            if (clientUpdated != 1)
                throw new InvalidOperationException("La ficha del paciente cambió durante la transferencia.");

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE client_nutritionist_assignments
                SET is_active = FALSE, unassigned_at = NOW()
                WHERE client_id = {workflow.ClientId} AND is_active = TRUE");

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO client_nutritionist_assignments
                    (client_id, nutritionist_id, assigned_by_user_id, assigned_at, is_active)
                VALUES
                    ({workflow.ClientId}, {workflow.TargetNutritionistId}, {workflow.TargetNutritionistId}, NOW(), TRUE)");

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE patient_nutritionist_transfer_workflows
                SET status = 'confirmed', consent_at = NOW(), resolved_at = NOW()
                WHERE id = {workflow.Id} AND status = 'awaiting_patient_confirmation'");

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO audit_logs (tenant_id, user_id, user_role, action, entity_name, entity_id, client_id, details, created_at)
                VALUES ({workflow.TargetTenantId}, NULL, 'patient', 'PATIENT_HISTORY_TRANSFER_CONSENT',
                        'client', {workflow.ClientId.ToString()}, {workflow.ClientId}, 'Consentimiento explícito para cambio de nutricionista y transferencia de historial', NOW())");
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "No se pudo transferir el historial del paciente {ClientId} al nutricionista {NutritionistId}. Se mantiene la relación original.",
                workflow.ClientId, workflow.TargetNutritionistId);
            return Conflict(new { message = "No se ha podido transferir todo el historial de forma segura. Tu relación y tus datos originales se mantienen intactos; contacta con soporte." });
        }

        // Google Calendar es un sistema externo: sus eventos se cancelan tras confirmar
        // la transacción local. Un fallo externo no revierte el cambio de nutricionista.
        foreach (var appointmentId in cancelledAppointmentIds)
        {
            try
            {
                await _googleCalendar.CancelAppointmentEventAsync(
                    workflow.SourceNutritionistId,
                    workflow.SourceTenantId,
                    appointmentId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "La cita {AppointmentId} se canceló en DietoExpress, pero no se pudo cancelar su evento en Google Calendar.",
                    appointmentId);
            }
        }

        if (!string.IsNullOrWhiteSpace(oldProfessional?.email))
        {
            try
            {
                var emailResult = await _email.SendEmailAsync(
                    oldProfessional.email,
                    "Cambio de nutricionista confirmado",
                    $"<p>El paciente ha confirmado el cambio a otro profesional y ha consentido la transferencia de su historial.</p><p>Las citas futuras que tenía contigo han quedado canceladas en DietoExpress. Las citas anteriores se conservan en el historial.</p><p>El historial se ha transferido a la nueva clínica. No es necesaria ninguna acción por tu parte.</p>");
                if (!emailResult.Exito)
                    _logger.LogWarning("La transferencia del paciente {ClientId} se completó, pero el proveedor rechazó el aviso al nutricionista anterior.", workflow.ClientId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Transferencia confirmada pero no se pudo notificar al nutricionista anterior del paciente {ClientId}.", workflow.ClientId);
            }
        }

        return Ok(new { completed = true, message = "Cambio confirmado y transferencia del historial completada." });
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id)
    {
        var clientId = ClientId;
        if (clientId == null) return Unauthorized();
        var workflow = await _context.Database.SqlQueryRaw<TransferRow>(
            @"SELECT id AS ""Id"", source_tenant_id AS ""SourceTenantId"", target_tenant_id AS ""TargetTenantId"",
                     client_id AS ""ClientId"", source_nutritionist_id AS ""SourceNutritionistId"",
                     target_nutritionist_id AS ""TargetNutritionistId"", appointment_id AS ""AppointmentId"",
                     status AS ""Status""
              FROM patient_nutritionist_transfer_workflows WHERE id = {0} AND client_id = {1}",
            id, clientId.Value).SingleOrDefaultAsync();
        if (workflow == null) return NotFound();
        if (workflow.Status == "cancelled")
            return Ok(new { cancelled = true, message = "El cambio ya estaba cancelado. Mantienes tu nutricionista actual y tu historial." });
        if (workflow.Status is not ("pending_professional" or "awaiting_patient_confirmation"))
            return Conflict(new { message = "Este cambio ya se ha resuelto y no se puede cancelar." });

        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE patient_nutritionist_transfer_workflows
            SET status = 'cancelled', resolved_at = NOW()
            WHERE id = {id} AND client_id = {clientId.Value}
              AND status IN ('pending_professional','awaiting_patient_confirmation')");
        await _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE patient_appointments
            SET status = 'cancelled', updated_at = NOW(),
                video_room_name = NULL, video_room_url = NULL, video_provider = NULL, video_expires_at = NULL
            WHERE id = {workflow.AppointmentId} AND client_id = {clientId.Value}
              AND status IN ('requested','confirmed')");
        await transaction.CommitAsync();
        return Ok(new { cancelled = true, message = "Cambio cancelado. Mantienes tu nutricionista actual y tu historial." });
    }
}
