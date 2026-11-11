using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Plantillas de planes y de sesiones.
/// </summary>
public sealed partial class TrainingPeriodizationController
{
    [HttpGet("session-templates")]
    public async Task<IActionResult> GetSessionTemplates(CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        var superAdmin = User.IsInRole("superadmin");
        var clinicAdmin = User.IsInRole("clinic_admin");
        await using var command = Command("""
            SELECT id, name, session_type, duration_minutes, intensity_target, instructions,
                   exercise_prescription, exercise_prescription_data::text, created_at
            FROM training_session_templates
            WHERE (@superAdmin OR tenant_id IS NOT DISTINCT FROM (SELECT tenant_id FROM users WHERE id = @userId))
              AND (@superAdmin OR @clinicAdmin OR created_by_user_id = @userId)
            ORDER BY updated_at DESC, id DESC
            """, ("userId", userId.Value), ("superAdmin", superAdmin), ("clinicAdmin", clinicAdmin));
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new {
                id = reader.GetInt64(0), name = reader.GetString(1), sessionType = reader.GetString(2),
                durationMinutes = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                intensityTarget = reader.IsDBNull(4) ? null : reader.GetString(4),
                instructions = reader.IsDBNull(5) ? null : reader.GetString(5),
                exercisePrescription = reader.IsDBNull(6) ? null : reader.GetString(6),
                exercisePrescriptionData = reader.IsDBNull(7) ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(7)),
                createdAt = reader.GetDateTime(8)
            });
        return Ok(items);
    }

    [HttpPost("session-templates")]
    public async Task<IActionResult> SaveSessionTemplate([FromBody] SaveTrainingSessionTemplateRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            return BadRequest(new { message = "El nombre de la plantilla debe tener entre 1 y 160 caracteres." });
        if (request.SessionType is not ("endurance" or "strength" or "mobility" or "recovery" or "health" or "general" or "other"))
            return BadRequest(new { message = "El tipo de sesión no es válido." });
        if (request.DurationMinutes is < 1 or > 1440)
            return BadRequest(new { message = "La duración debe estar entre 1 y 1440 minutos." });
        if (request.IntensityTarget?.Length > 120 || request.Instructions?.Length > 4000 || request.ExercisePrescription?.Length > 6000)
            return BadRequest(new { message = "La intensidad admite 120 caracteres, las instrucciones 4000 y la prescripción 6000." });
        if (request.ExercisePrescriptionData.HasValue &&
            (request.ExercisePrescriptionData.Value.ValueKind != JsonValueKind.Object || request.ExercisePrescriptionData.Value.GetRawText().Length > 20000))
            return BadRequest(new { message = "La prescripción estructurada debe ser un objeto JSON de hasta 20000 caracteres." });

        var superAdmin = User.IsInRole("superadmin");
        var clinicAdmin = User.IsInRole("clinic_admin");
        await using var command = Command("""
            INSERT INTO training_session_templates(tenant_id, created_by_user_id, name, session_type, duration_minutes,
                intensity_target, instructions, exercise_prescription, exercise_prescription_data)
            SELECT tenant_id, @userId, @name, @type, @duration, @intensity, @instructions, @prescription, @prescriptionData::jsonb
            FROM users WHERE id = @userId
            RETURNING id
            """, ("userId", userId.Value), ("name", request.Name.Trim()), ("type", request.SessionType),
            ("duration", request.DurationMinutes), ("intensity", request.IntensityTarget?.Trim()),
            ("instructions", request.Instructions?.Trim()), ("prescription", request.ExercisePrescription?.Trim()),
            ("prescriptionData", request.ExercisePrescriptionData.HasValue ? request.ExercisePrescriptionData.Value.GetRawText() : null));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        if (id is null) return NotFound();
        return Ok(new {
            id = Convert.ToInt64(id), name = request.Name.Trim(), sessionType = request.SessionType,
            durationMinutes = request.DurationMinutes, intensityTarget = request.IntensityTarget,
            instructions = request.Instructions, exercisePrescription = request.ExercisePrescription,
            exercisePrescriptionData = request.ExercisePrescriptionData
        });
    }

    [HttpDelete("session-templates/{templateId:long}")]
    public async Task<IActionResult> DeleteSessionTemplate(long templateId, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        await using var command = Command("""
            DELETE FROM training_session_templates
            WHERE id = @id
              AND (@superAdmin OR tenant_id IS NOT DISTINCT FROM (SELECT tenant_id FROM users WHERE id = @userId))
              AND (@superAdmin OR @clinicAdmin OR created_by_user_id = @userId)
            RETURNING id
            """, ("id", templateId), ("userId", userId.Value),
            ("superAdmin", User.IsInRole("superadmin")), ("clinicAdmin", User.IsInRole("clinic_admin")));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is null ? NotFound() : NoContent();
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates(CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        var superAdmin = User.IsInRole("superadmin");
        var clinicAdmin = User.IsInRole("clinic_admin");
        await using var command = Command("""
            SELECT t.id, t.name, t.periodization_model, t.notes, t.created_at,
                   COALESCE(jsonb_array_length(t.template_data->'sessions'), 0) AS session_count
            FROM training_plan_templates t
            WHERE (@superAdmin OR t.tenant_id = (SELECT tenant_id FROM users WHERE id = @userId))
              AND (@superAdmin OR @clinicAdmin OR t.created_by_user_id = @userId)
            ORDER BY t.updated_at DESC, t.id DESC
            """, ("userId", userId.Value), ("superAdmin", superAdmin), ("clinicAdmin", clinicAdmin));
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new { id = reader.GetInt64(0), name = reader.GetString(1), periodizationModel = reader.GetString(2),
                notes = reader.IsDBNull(3) ? null : reader.GetString(3), createdAt = reader.GetDateTime(4),
                sessionCount = reader.GetInt32(5) });
        return Ok(items);
    }

    [HttpPost("plans/{planId:int}/templates")]
    public async Task<IActionResult> SavePlanAsTemplate(int planId, [FromBody] SaveTrainingTemplateRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            return BadRequest(new { message = "El nombre de la plantilla debe tener entre 1 y 160 caracteres." });

        await using var planCommand = Command("""
            SELECT p.name, p.periodization_model, p.start_date, p.end_date, p.notes, c.tenant_id
            FROM training_plans p
            JOIN training_profiles f ON f.id = p.profile_id
            JOIN clients c ON c.id = f.client_id
            WHERE p.id = @planId
            """, ("planId", planId));
        string planName, model; DateOnly startDate; DateOnly? endDate; string? notes; int tenantId;
        await using (var reader = await planCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return NotFound();
            planName = reader.GetString(0); model = reader.GetString(1); startDate = reader.GetFieldValue<DateOnly>(2);
            endDate = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3);
            notes = reader.IsDBNull(4) ? null : reader.GetString(4); tenantId = reader.GetInt32(5);
        }

        var sessions = new List<TrainingTemplateSession>();
        await using (var sessionsCommand = Command("""
            SELECT scheduled_date, title, session_type, duration_minutes, intensity_target, instructions, exercise_prescription,
                   exercise_prescription_data::text
            FROM training_sessions WHERE plan_id = @planId ORDER BY scheduled_date, id
            """, ("planId", planId)))
        await using (var reader = await sessionsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                sessions.Add(new TrainingTemplateSession(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(7))));
        }

        var mesocycles = new List<TrainingTemplateMesocycle>();
        await using (var mesoCommand = Command("""
            SELECT id, name, sequence_number, start_date, end_date, objective, notes
            FROM training_mesocycles WHERE plan_id = @planId ORDER BY sequence_number, id
            """, ("planId", planId)))
        await using (var reader = await mesoCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                mesocycles.Add(new TrainingTemplateMesocycle(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2),
                    reader.GetFieldValue<DateOnly>(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), new List<TrainingTemplateMicrocycle>()));
        }
        foreach (var meso in mesocycles)
        {
            await using var microCommand = Command("""
                SELECT name, sequence_number, start_date, end_date, focus, notes
                FROM training_microcycles WHERE mesocycle_id = @mesocycleId ORDER BY sequence_number, id
                """, ("mesocycleId", meso.Id));
            await using var reader = await microCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                meso.Microcycles.Add(new TrainingTemplateMicrocycle(reader.GetString(0), reader.GetInt32(1),
                    reader.GetFieldValue<DateOnly>(2), reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        var payload = JsonSerializer.Serialize(new TrainingTemplatePayload(planName, model, startDate, endDate, notes, sessions, mesocycles),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await using var save = Command("""
            INSERT INTO training_plan_templates(tenant_id, created_by_user_id, name, periodization_model, notes, template_data)
            VALUES (@tenantId, @userId, @name, @model, @notes, CAST(@payload AS jsonb))
            RETURNING id
            """, ("tenantId", tenantId), ("userId", userId.Value), ("name", request.Name.Trim()),
            ("model", model), ("notes", notes), ("payload", payload));
        var id = Convert.ToInt64(await save.ExecuteScalarAsync(cancellationToken));
        return Ok(new { id, name = request.Name.Trim(), periodizationModel = model, sessionCount = sessions.Count });
    }

    [HttpPost("clients/{clientId:int}/plans/from-template/{templateId:long}")]
    public async Task<IActionResult> CreatePlanFromTemplate(int clientId, long templateId, [FromBody] ApplyTrainingTemplateRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await CanManageClient(clientId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            return BadRequest(new { message = "El nombre del plan debe tener entre 1 y 160 caracteres." });

        var tenantId = await _context.Database.SqlQueryRaw<int?>(
            """SELECT tenant_id AS "Value" FROM clients WHERE id = {0} AND archived_at IS NULL""", clientId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!tenantId.HasValue) return NotFound();

        var superAdmin = User.IsInRole("superadmin");
        var clinicAdmin = User.IsInRole("clinic_admin");
        await using var templateCommand = Command("""
            SELECT template_data::text FROM training_plan_templates
            WHERE id = @templateId AND (@superAdmin OR tenant_id = @tenantId)
              AND (@superAdmin OR @clinicAdmin OR created_by_user_id = @userId)
            """, ("templateId", templateId), ("tenantId", tenantId.Value), ("userId", userId.Value),
            ("superAdmin", superAdmin), ("clinicAdmin", clinicAdmin));
        var raw = await templateCommand.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(raw)) return NotFound();
        var template = JsonSerializer.Deserialize<TrainingTemplatePayload>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (template is null) return BadRequest(new { message = "La plantilla no tiene un formato válido." });

        if (template.Sessions.Any(x => x.SessionType is not ("endurance" or "strength" or "mobility" or "recovery" or "health" or "general" or "other")))
            return BadRequest(new { message = "La plantilla contiene un tipo de sesión no válido." });

        await using var createPlan = Command("""
            INSERT INTO training_plans(profile_id, name, periodization_model, start_date, end_date, status, notes, created_by_user_id)
            SELECT f.id, @name, @model, @startDate,
                   CASE WHEN @endDate IS NULL THEN NULL ELSE @startDate + (@endDate - @templateStartDate) END,
                   'draft', @notes, @userId
            FROM training_profiles f
            WHERE f.client_id = @clientId AND f.enabled = TRUE
            RETURNING id
            """, ("clientId", clientId), ("name", request.Name.Trim()), ("model", template.PeriodizationModel),
            ("startDate", request.StartDate), ("endDate", template.EndDate), ("templateStartDate", template.StartDate),
            ("notes", template.Notes), ("userId", userId.Value));
        var planIdValue = await createPlan.ExecuteScalarAsync(cancellationToken);
        if (planIdValue is null) return Conflict(new { message = "Activa primero la planificación deportiva para este cliente." });
        var newPlanId = Convert.ToInt32(planIdValue);

        foreach (var session in template.Sessions)
        {
            var scheduledDate = request.StartDate.AddDays(session.ScheduledDate.DayNumber - template.StartDate.DayNumber);
            await using var insertSession = Command("""
                INSERT INTO training_sessions(plan_id, scheduled_date, title, session_type, duration_minutes, intensity_target, instructions, exercise_prescription, exercise_prescription_data)
                VALUES (@planId, @date, @title, @type, @duration, @intensity, @instructions, @prescription, @prescriptionData::jsonb)
                """, ("planId", newPlanId), ("date", scheduledDate), ("title", session.Title), ("type", session.SessionType),
                ("duration", session.DurationMinutes), ("intensity", session.IntensityTarget), ("instructions", session.Instructions),
                ("prescription", session.ExercisePrescription),
                ("prescriptionData", session.ExercisePrescriptionData.HasValue ? session.ExercisePrescriptionData.Value.GetRawText() : null));
            await insertSession.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var meso in template.Mesocycles)
        {
            var mesoStart = request.StartDate.AddDays(meso.StartDate.DayNumber - template.StartDate.DayNumber);
            var mesoEnd = meso.EndDate.HasValue ? request.StartDate.AddDays(meso.EndDate.Value.DayNumber - template.StartDate.DayNumber) : (DateOnly?)null;
            await using var insertMeso = Command("""
                INSERT INTO training_mesocycles(plan_id, name, sequence_number, start_date, end_date, objective, notes)
                VALUES (@planId, @name, @sequence, @startDate, @endDate, @objective, @notes)
                RETURNING id
                """, ("planId", newPlanId), ("name", meso.Name), ("sequence", meso.SequenceNumber),
                ("startDate", mesoStart), ("endDate", mesoEnd), ("objective", meso.Objective), ("notes", meso.Notes));
            var newMesoId = Convert.ToInt32(await insertMeso.ExecuteScalarAsync(cancellationToken));
            foreach (var micro in meso.Microcycles)
            {
                var microStart = request.StartDate.AddDays(micro.StartDate.DayNumber - template.StartDate.DayNumber);
                var microEnd = micro.EndDate.HasValue ? request.StartDate.AddDays(micro.EndDate.Value.DayNumber - template.StartDate.DayNumber) : (DateOnly?)null;
                await using var insertMicro = Command("""
                    INSERT INTO training_microcycles(mesocycle_id, name, sequence_number, start_date, end_date, focus, notes)
                    VALUES (@mesocycleId, @name, @sequence, @startDate, @endDate, @focus, @notes)
                    """, ("mesocycleId", newMesoId), ("name", micro.Name), ("sequence", micro.SequenceNumber),
                    ("startDate", microStart), ("endDate", microEnd), ("focus", micro.Focus), ("notes", micro.Notes));
                await insertMicro.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        return Ok(new { id = newPlanId, name = request.Name.Trim(), copiedSessions = template.Sessions.Count, copiedMesocycles = template.Mesocycles.Count });
    }
}
