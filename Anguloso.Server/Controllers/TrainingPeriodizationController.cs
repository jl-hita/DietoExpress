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
/// Planificación deportiva opcional por cliente. La existencia de un perfil es la activación explícita del módulo.
/// </summary>
[ApiController]
[Route("api/training")]
[Authorize(Policy = "Professional")]
public sealed class TrainingPeriodizationController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly EmailServ _emailServ;
    private readonly ExerciseCatalogService _exerciseCatalog;

    public TrainingPeriodizationController(angulosodbContext context, EmailServ emailServ, ExerciseCatalogService exerciseCatalog)
    {
        _context = context;
        _emailServ = emailServ;
        _exerciseCatalog = exerciseCatalog;
    }



    [HttpPost("generate-preview")]
    public async Task<IActionResult> GeneratePreview([FromBody] TrainingProgramGenerationRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        try
        {
            var candidates = await _exerciseCatalog.LoadGeneratorCandidatesAsync(userId.Value, cancellationToken);
            var result = TrainingProgramGenerator.Generate(request, candidates);
            return result.Succeeded ? Ok(result.Prescription) : UnprocessableEntity(new { errors = result.Errors });
        }
        catch (Exception)
        {
            return StatusCode(503, new { errors = new[] { "No se pudo preparar el catálogo de ejercicios para generar la rutina." } });
        }
    }

    [HttpPost("plans/{planId:int}/generate-mesocycle")]
    public async Task<IActionResult> GenerateAndSaveMesocycle(
        int planId,
        [FromBody] GenerateTrainingMesocycleRequest request,
        CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            return BadRequest(new { message = "El nombre del mesociclo es obligatorio (máximo 160 caracteres)." });
        if (request.SequenceNumber < 1)
            return BadRequest(new { message = "El número de mesociclo debe ser positivo." });
        if (request.Notes?.Length > 4000)
            return BadRequest(new { message = "Las notas admiten hasta 4000 caracteres." });

        var candidates = await _exerciseCatalog.LoadGeneratorCandidatesAsync(userId.Value, cancellationToken);
        var generated = TrainingProgramGenerator.Generate(request.Generation, candidates);
        if (!generated.Succeeded)
            return UnprocessableEntity(new { errors = generated.Errors });
        var prescription = generated.Prescription!;
        var offsets = prescription.Sessions.Select(session =>
            ((int)session.Day - (int)prescription.StartDate.DayOfWeek + 7) % 7
                + (session.WeekNumber - 1) * 7).ToArray();
        var mesocycleStart = prescription.StartDate;
        var mesocycleEnd = prescription.StartDate.AddDays(offsets.Max());
        if (mesocycleEnd < mesocycleStart)
            return UnprocessableEntity(new { message = "El intervalo calculado para el mesociclo no es válido." });

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var planCommand = connection.CreateCommand())
            {
                planCommand.Transaction = transaction;
                planCommand.CommandText = """
                    SELECT start_date, end_date
                    FROM training_plans
                    WHERE id = @planId
                    FOR UPDATE
                    """;
                planCommand.Parameters.AddWithValue("planId", planId);
                await using var reader = await planCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return NotFound();
                var planStart = reader.GetFieldValue<DateOnly>(0);
                DateOnly? planEnd = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1);
                if (mesocycleStart < planStart || (planEnd.HasValue && mesocycleEnd > planEnd.Value))
                    return Conflict(new { message = "El mesociclo generado debe quedar dentro de las fechas del plan. Amplía el plan o ajusta la fecha y duración." });
            }

            await using (var overlapCommand = connection.CreateCommand())
            {
                overlapCommand.Transaction = transaction;
                overlapCommand.CommandText = """
                    SELECT EXISTS (
                        SELECT 1 FROM training_mesocycles
                        WHERE plan_id = @planId
                          AND (sequence_number = @sequence OR
                               (start_date <= @endDate AND COALESCE(end_date, 'infinity'::date) >= @startDate))
                    ) OR EXISTS (
                        SELECT 1 FROM training_sessions
                        WHERE plan_id = @planId AND scheduled_date BETWEEN @startDate AND @endDate
                    )
                    """;
                overlapCommand.Parameters.AddWithValue("planId", planId);
                overlapCommand.Parameters.AddWithValue("sequence", request.SequenceNumber);
                overlapCommand.Parameters.AddWithValue("startDate", mesocycleStart);
                overlapCommand.Parameters.AddWithValue("endDate", mesocycleEnd);
                if ((bool)(await overlapCommand.ExecuteScalarAsync(cancellationToken) ?? true))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Conflict(new { message = "Ya existe un mesociclo con ese número o con fechas solapadas." });
                }
            }

            int mesocycleId;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO training_mesocycles(plan_id, name, sequence_number, start_date, end_date, objective, notes)
                    VALUES (@planId, @name, @sequence, @startDate, @endDate, @objective, @notes)
                    RETURNING id
                    """;
                command.Parameters.AddWithValue("planId", planId);
                command.Parameters.AddWithValue("name", request.Name.Trim());
                command.Parameters.AddWithValue("sequence", request.SequenceNumber);
                command.Parameters.AddWithValue("startDate", mesocycleStart);
                command.Parameters.AddWithValue("endDate", mesocycleEnd);
                command.Parameters.AddWithValue("objective", prescription.Goal);
                command.Parameters.AddWithValue("notes", (object?)request.Notes?.Trim() ?? DBNull.Value);
                mesocycleId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            }

            for (var week = 1; week <= prescription.DurationWeeks; week++)
            {
                var weekStart = prescription.StartDate.AddDays((week - 1) * 7);
                var weekEnd = prescription.StartDate.AddDays(week * 7 - 1);
                var microcycleEnd = weekEnd > mesocycleEnd ? mesocycleEnd : weekEnd;
                int microcycleId;
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO training_microcycles(mesocycle_id, name, sequence_number, start_date, end_date, focus, notes)
                        VALUES (@mesocycleId, @name, @sequence, @startDate, @endDate, @focus, @notes)
                        """;
                    command.Parameters.AddWithValue("mesocycleId", mesocycleId);
                    command.Parameters.AddWithValue("name", $"Semana {week}");
                    command.Parameters.AddWithValue("sequence", week);
                    command.Parameters.AddWithValue("startDate", weekStart);
                    command.Parameters.AddWithValue("endDate", microcycleEnd < weekStart ? weekStart : microcycleEnd);
                    command.Parameters.AddWithValue("focus", $"Progresión {prescription.Goal}; motor {prescription.EngineVersion}");
                    command.Parameters.AddWithValue("notes", (object?)$"Borrador generado automáticamente. Revisión profesional requerida." ?? DBNull.Value);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (var session in prescription.Sessions.Where(x => x.WeekNumber == week))
                {
                    var offset = ((int)session.Day - (int)prescription.StartDate.DayOfWeek + 7) % 7;
                    var sessionDate = prescription.StartDate.AddDays((week - 1) * 7 + offset);
                    var prescriptionText = string.Join(Environment.NewLine, session.Exercises.Select(exercise =>
                        $"{exercise.Order}. {exercise.ExerciseNameSnapshot} — {exercise.Sets} × {exercise.MinRepetitions}-{exercise.MaxRepetitions} reps; RIR {(exercise.TargetRepetitionsInReserve?.ToString() ?? "—")}; descanso {exercise.RestSeconds}s; progresión {exercise.ProgressionRule}. [ID: {exercise.ExerciseId}; catálogo: {exercise.CatalogVersion}]"));
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO training_sessions(plan_id, scheduled_date, title, session_type, duration_minutes,
                            intensity_target, instructions, exercise_prescription)
                        VALUES (@planId, @date, @title, 'strength', @duration, @intensity, @instructions, @prescription)
                        """;
                    command.Parameters.AddWithValue("planId", planId);
                    command.Parameters.AddWithValue("date", sessionDate);
                    command.Parameters.AddWithValue("title", session.Title);
                    command.Parameters.AddWithValue("duration", session.EstimatedDurationMinutes);
                    command.Parameters.AddWithValue("intensity", $"{prescription.Goal}; RIR según prescripción");
                    command.Parameters.AddWithValue("instructions", $"Borrador generado por el motor {prescription.EngineVersion}. Revisar antes de asignar al paciente.");
                    command.Parameters.AddWithValue("prescription", prescriptionText);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return Ok(new
            {
                mesocycleId,
                name = request.Name.Trim(),
                sequenceNumber = request.SequenceNumber,
                startDate = mesocycleStart,
                endDate = mesocycleEnd,
                durationWeeks = prescription.DurationWeeks,
                sessionsCreated = prescription.Sessions.Count,
                engineVersion = prescription.EngineVersion,
                requiresProfessionalReview = true
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return StatusCode(500, new { message = "No se pudo guardar el mesociclo generado. No se han conservado cambios parciales." });
        }
    }

    [HttpGet("session-templates")]
    public async Task<IActionResult> GetSessionTemplates(CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        var superAdmin = User.IsInRole("superadmin");
        var clinicAdmin = User.IsInRole("clinic_admin");
        await using var command = Command("""
            SELECT id, name, session_type, duration_minutes, intensity_target, instructions,
                   exercise_prescription, created_at
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
                createdAt = reader.GetDateTime(7)
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

        var superAdmin = User.IsInRole("superadmin");
        var clinicAdmin = User.IsInRole("clinic_admin");
        await using var command = Command("""
            INSERT INTO training_session_templates(tenant_id, created_by_user_id, name, session_type, duration_minutes,
                intensity_target, instructions, exercise_prescription)
            SELECT tenant_id, @userId, @name, @type, @duration, @intensity, @instructions, @prescription
            FROM users WHERE id = @userId
            RETURNING id
            """, ("userId", userId.Value), ("name", request.Name.Trim()), ("type", request.SessionType),
            ("duration", request.DurationMinutes), ("intensity", request.IntensityTarget?.Trim()),
            ("instructions", request.Instructions?.Trim()), ("prescription", request.ExercisePrescription?.Trim()));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        if (id is null) return NotFound();
        return Ok(new {
            id = Convert.ToInt64(id), name = request.Name.Trim(), sessionType = request.SessionType,
            durationMinutes = request.DurationMinutes, intensityTarget = request.IntensityTarget,
            instructions = request.Instructions, exercisePrescription = request.ExercisePrescription
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
            SELECT scheduled_date, title, session_type, duration_minutes, intensity_target, instructions, exercise_prescription
            FROM training_sessions WHERE plan_id = @planId ORDER BY scheduled_date, id
            """, ("planId", planId)))
        await using (var reader = await sessionsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                sessions.Add(new TrainingTemplateSession(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
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
                INSERT INTO training_sessions(plan_id, scheduled_date, title, session_type, duration_minutes, intensity_target, instructions, exercise_prescription)
                VALUES (@planId, @date, @title, @type, @duration, @intensity, @instructions, @prescription)
                """, ("planId", newPlanId), ("date", scheduledDate), ("title", session.Title), ("type", session.SessionType),
                ("duration", session.DurationMinutes), ("intensity", session.IntensityTarget), ("instructions", session.Instructions),
                ("prescription", session.ExercisePrescription));
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
        await NotifyTrainingPlanAssignedAsync(clientId, newPlanId, request.Name.Trim(), cancellationToken);
        return Ok(new { id = newPlanId, name = request.Name.Trim(), copiedSessions = template.Sessions.Count, copiedMesocycles = template.Mesocycles.Count });
    }

    [HttpGet("clients/{clientId:int}/profile")]
    public async Task<IActionResult> GetProfile(int clientId, CancellationToken cancellationToken)
    {
        if (!await CanAccessClient(clientId, cancellationToken)) return NotFound();
        var canManage = await CanManageClient(clientId, cancellationToken);
        await using var command = Command("""
            SELECT id, client_id, enabled, primary_goal, experience_level, management_mode, notes, created_at, updated_at
            FROM training_profiles WHERE client_id = @clientId
            """, ("clientId", clientId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            // No tener perfil todavía es un estado normal: el profesional puede activar
            // la planificación desde esta misma pantalla. No debe tratarse como un 404.
            return Ok(new
            {
                id = 0,
                clientId,
                enabled = false,
                primaryGoal = "general_fitness",
                experienceLevel = "beginner",
                managementMode = "none",
                notes = (string?)null,
                canManage
            });
        }
        return Ok(new TrainingProfileDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetBoolean(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetDateTime(7), reader.GetDateTime(8), canManage));
    }

    [HttpPut("clients/{clientId:int}/profile")]
    public async Task<IActionResult> SetProfile(int clientId, [FromBody] UpdateTrainingProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await CanManageClient(clientId, cancellationToken)) return NotFound();
        if (request.PrimaryGoal is not ("endurance" or "strength" or "hypertrophy" or "weight_loss" or "health" or "general_fitness" or "other"))
            return BadRequest(new { message = "El objetivo deportivo no es válido." });
        if (request.ExperienceLevel is not ("beginner" or "intermediate" or "advanced" or "unknown"))
            return BadRequest(new { message = "El nivel de experiencia no es válido." });
        var managementMode = request.ManagementMode ?? "self";
        if (managementMode is not ("none" or "self" or "collaboration"))
            return BadRequest(new { message = "La modalidad de gestión del entrenamiento no es válida." });

        await using var command = Command("""
            INSERT INTO training_profiles(client_id, tenant_id, enabled, primary_goal, experience_level, management_mode, notes, created_by_user_id)
            SELECT c.id, c.tenant_id, @enabled, @goal, @experience, @managementMode, @notes, @userId
            FROM clients c WHERE c.id = @clientId AND c.archived_at IS NULL
            ON CONFLICT(client_id) DO UPDATE SET enabled = EXCLUDED.enabled,
                primary_goal = EXCLUDED.primary_goal, experience_level = EXCLUDED.experience_level,
                management_mode = EXCLUDED.management_mode, notes = EXCLUDED.notes, updated_at = NOW()
            RETURNING id, client_id, enabled, primary_goal, experience_level, management_mode, notes, created_at, updated_at
            """, ("clientId", clientId), ("enabled", request.Enabled), ("goal", request.PrimaryGoal),
            ("experience", request.ExperienceLevel), ("managementMode", managementMode), ("notes", request.Notes), ("userId", userId.Value));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();
        var profile = new TrainingProfileDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetBoolean(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetDateTime(7), reader.GetDateTime(8));
        return Ok(profile);
    }

    [HttpGet("clients/{clientId:int}/plans")]
    public async Task<IActionResult> GetPlans(int clientId, CancellationToken cancellationToken)
    {
        if (!await CanAccessClient(clientId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT p.id, p.name, p.periodization_model, p.start_date, p.end_date, p.status, p.version, p.notes
            FROM training_plans p JOIN training_profiles f ON f.id = p.profile_id
            WHERE f.client_id = @clientId AND f.enabled = TRUE
            ORDER BY p.start_date DESC, p.id DESC
            """, ("clientId", clientId));
        var items = new List<TrainingPlanDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new TrainingPlanDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4), reader.GetString(5), reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return Ok(items);
    }

    [HttpPost("clients/{clientId:int}/plans")]
    public async Task<IActionResult> CreatePlan(int clientId, [FromBody] CreateTrainingPlanRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await CanManageClient(clientId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160) return BadRequest(new { message = "El nombre debe tener entre 1 y 160 caracteres." });
        if (request.EndDate.HasValue && request.EndDate.Value < request.StartDate) return BadRequest(new { message = "La fecha final no puede ser anterior a la inicial." });
        if (request.PeriodizationModel is not ("linear" or "undulating" or "block" or "reverse" or "continuous_competition" or "autoregulatory" or "custom"))
            return BadRequest(new { message = "El modelo de periodización no es válido." });
        await using var command = Command("""
            INSERT INTO training_plans(profile_id, name, periodization_model, start_date, end_date, status, notes, created_by_user_id)
            SELECT f.id, @name, @model, @startDate, @endDate, 'draft', @notes, @userId
            FROM training_profiles f WHERE f.client_id = @clientId AND f.enabled = TRUE
            RETURNING id
            """, ("clientId", clientId), ("name", request.Name.Trim()), ("model", request.PeriodizationModel),
            ("startDate", request.StartDate), ("endDate", request.EndDate), ("notes", request.Notes), ("userId", userId.Value));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        if (id is null) return Conflict(new { message = "Activa primero la planificación deportiva para este cliente." });
        var planId = Convert.ToInt32(id);
        await NotifyTrainingPlanAssignedAsync(clientId, planId, request.Name.Trim(), cancellationToken);
        return CreatedAtAction(nameof(GetPlans), new { clientId }, new { id = planId, request.Name, request.PeriodizationModel, request.StartDate, request.EndDate, status = "draft", version = 1 });
    }

    [HttpGet("plans/{planId:int}/load-trend")]
    public async Task<IActionResult> GetLoadTrend(int planId, CancellationToken cancellationToken)
    {
        if (!await CanAccessPlan(planId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT date_trunc('week', s.scheduled_date)::date AS week_start,
                   COUNT(r.id) AS completed_sessions,
                   COALESCE(SUM(s.duration_minutes), 0) AS planned_minutes,
                   COALESCE(SUM(r.actual_duration_minutes), 0) AS actual_minutes,
                   COALESCE(SUM(r.actual_duration_minutes * r.session_rpe), 0) AS load_score
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            LEFT JOIN training_session_records r ON r.session_id = s.id AND r.client_id = f.client_id
            WHERE p.id = @planId AND f.enabled = TRUE
              AND s.scheduled_date >= CURRENT_DATE - INTERVAL '11 weeks'
              AND s.scheduled_date <= CURRENT_DATE
            GROUP BY date_trunc('week', s.scheduled_date)::date
            ORDER BY week_start
            """, ("planId", planId));
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new {
                weekStart = reader.GetFieldValue<DateOnly>(0),
                completedSessions = reader.GetInt64(1),
                plannedMinutes = reader.GetInt64(2),
                actualMinutes = reader.GetInt64(3),
                loadScore = reader.GetDecimal(4)
            });
        return Ok(items);
    }

    [HttpGet("plans/{planId:int}/sessions")]
    public async Task<IActionResult> GetSessions(int planId, CancellationToken cancellationToken)
    {
        if (!await CanAccessPlan(planId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT s.id, s.plan_id, s.scheduled_date, s.title, s.session_type, s.duration_minutes, s.intensity_target, s.instructions, s.status,
                   r.completed_at, r.actual_duration_minutes, r.session_rpe, r.notes, s.exercise_prescription
            FROM training_sessions s JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            LEFT JOIN training_session_records r ON r.session_id = s.id AND r.client_id = f.client_id
            WHERE p.id = @planId AND f.enabled = TRUE
            ORDER BY s.scheduled_date, s.id
            """, ("planId", planId));
        var items = new List<TrainingSessionDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new TrainingSessionDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetFieldValue<DateOnly>(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetDateTime(9), reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11), reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13)));
        return Ok(items);
    }

    [HttpPost("plans/{planId:int}/sessions")]
    public async Task<IActionResult> CreateSession(int planId, [FromBody] CreateTrainingSessionRequest request, CancellationToken cancellationToken)
    {
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 160) return BadRequest(new { message = "El nombre de la sesión es obligatorio (máximo 160 caracteres)." });
        if (request.DurationMinutes is < 1 or > 1440) return BadRequest(new { message = "La duración debe estar entre 1 y 1440 minutos." });
        if (request.ExercisePrescription?.Length > 6000) return BadRequest(new { message = "La prescripción de ejercicios admite hasta 6000 caracteres." });
        if (request.SessionType is not ("endurance" or "strength" or "mobility" or "recovery" or "health" or "general" or "other"))
            return BadRequest(new { message = "El tipo de sesión no es válido." });
        await using var command = Command("""
            INSERT INTO training_sessions(plan_id, scheduled_date, title, session_type, duration_minutes, intensity_target, instructions, exercise_prescription)
            SELECT id, @date, @title, @type, @duration, @intensity, @instructions, @exercisePrescription
            FROM training_plans
            WHERE id = @planId AND @date >= start_date
              AND (end_date IS NULL OR @date <= end_date)
            RETURNING id
            """, ("planId", planId), ("date", request.ScheduledDate), ("title", request.Title.Trim()), ("type", request.SessionType),
            ("duration", request.DurationMinutes), ("intensity", request.IntensityTarget), ("instructions", request.Instructions), ("exercisePrescription", request.ExercisePrescription?.Trim()));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is null
            ? Conflict(new { message = "La fecha de la sesión debe estar dentro del periodo del plan." })
            : CreatedAtAction(nameof(GetSessions), new { planId }, new { id = Convert.ToInt32(id), request.ScheduledDate, request.Title, request.SessionType, request.DurationMinutes, status = "planned" });
    }

    [HttpPut("sessions/{sessionId:int}")]
    public async Task<IActionResult> UpdateSession(int sessionId, [FromBody] CreateTrainingSessionRequest request, CancellationToken cancellationToken)
    {
        var clientId = await GetSessionClientId(sessionId, cancellationToken);
        if (!clientId.HasValue || !await CanManageClient(clientId.Value, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 160)
            return BadRequest(new { message = "El nombre de la sesión es obligatorio (máximo 160 caracteres)." });
        if (request.DurationMinutes is < 1 or > 1440)
            return BadRequest(new { message = "La duración debe estar entre 1 y 1440 minutos." });
        if (request.SessionType is not ("endurance" or "strength" or "mobility" or "recovery" or "health" or "general" or "other"))
            return BadRequest(new { message = "El tipo de sesión no es válido." });
        if (request.IntensityTarget?.Length > 120 || request.Instructions?.Length > 4000 || request.ExercisePrescription?.Length > 6000)
            return BadRequest(new { message = "La intensidad admite 120 caracteres y las instrucciones 4000." });

        await using var command = Command("""
            UPDATE training_sessions s
            SET scheduled_date = @date, title = @title, session_type = @type, duration_minutes = @duration,
                intensity_target = @intensity, instructions = @instructions, exercise_prescription = @exercisePrescription, updated_at = NOW()
            FROM training_plans p JOIN training_profiles f ON f.id = p.profile_id
            WHERE s.plan_id = p.id AND s.id = @sessionId AND f.enabled = TRUE
              AND @date >= p.start_date AND (p.end_date IS NULL OR @date <= p.end_date)
            RETURNING s.id
            """, ("sessionId", sessionId), ("date", request.ScheduledDate), ("title", request.Title.Trim()),
            ("type", request.SessionType), ("duration", request.DurationMinutes), ("intensity", request.IntensityTarget?.Trim()),
            ("instructions", request.Instructions?.Trim()), ("exercisePrescription", request.ExercisePrescription?.Trim()));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is null
            ? Conflict(new { message = "La fecha de la sesión debe estar dentro del periodo del plan." })
            : Ok(new { id = Convert.ToInt32(id), clientId = clientId.Value, message = "Sesión actualizada." });
    }

    [HttpPost("sessions/{sessionId:int}/record")]
    public async Task<IActionResult> RecordSession(int sessionId, [FromBody] RecordTrainingSessionRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        var clientId = await GetSessionClientId(sessionId, cancellationToken);
        // Los colaboradores tienen acceso de consulta: solo el profesional responsable puede modificar registros.
        if (!clientId.HasValue || !await CanManageClient(clientId.Value, cancellationToken)) return NotFound();
        if (request.ActualDurationMinutes is < 1 or > 1440 || request.SessionRpe is < 0 or > 10)
            return BadRequest(new { message = "La duración debe ser positiva y el RPE debe estar entre 0 y 10." });
        await using var command = Command("""
            INSERT INTO training_session_records(session_id, client_id, completed_at, actual_duration_minutes, session_rpe, notes)
            SELECT s.id, f.client_id, COALESCE(@completedAt, NOW()), @duration, @rpe, @notes
            FROM training_sessions s JOIN training_plans p ON p.id = s.plan_id JOIN training_profiles f ON f.id = p.profile_id
            WHERE s.id = @sessionId AND f.enabled = TRUE
            ON CONFLICT(session_id, client_id) DO UPDATE SET completed_at = EXCLUDED.completed_at,
                actual_duration_minutes = EXCLUDED.actual_duration_minutes, session_rpe = EXCLUDED.session_rpe,
                notes = EXCLUDED.notes, updated_at = NOW()
            RETURNING id
            """, ("sessionId", sessionId), ("completedAt", request.CompletedAt), ("duration", request.ActualDurationMinutes),
            ("rpe", request.SessionRpe), ("notes", request.Notes));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is null ? NotFound() : Ok(new { id = Convert.ToInt64(id), clientId = clientId.Value, sessionId });
    }



    [HttpGet("plans/{planId:int}/mesocycles")]
    public async Task<IActionResult> GetMesocycles(int planId, CancellationToken cancellationToken)
    {
        if (!await CanAccessPlan(planId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT id, plan_id, name, sequence_number, start_date, end_date, objective, notes
            FROM training_mesocycles WHERE plan_id = @planId
            ORDER BY sequence_number, start_date, id
            """, ("planId", planId));
        var items = new List<TrainingMesocycleDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new TrainingMesocycleDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetFieldValue<DateOnly>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return Ok(items);
    }

    [HttpPost("plans/{planId:int}/mesocycles")]
    public async Task<IActionResult> CreateMesocycle(int planId, [FromBody] CreateTrainingMesocycleRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            return BadRequest(new { message = "El nombre del mesociclo es obligatorio (máximo 160 caracteres)." });
        if (request.SequenceNumber < 1) return BadRequest(new { message = "El número de mesociclo debe ser positivo." });
        if (request.EndDate.HasValue && request.EndDate.Value < request.StartDate)
            return BadRequest(new { message = "La fecha final no puede ser anterior a la inicial." });
        await using var command = Command("""
            INSERT INTO training_mesocycles(plan_id, name, sequence_number, start_date, end_date, objective, notes)
            SELECT id, @name, @sequence, @startDate, @endDate, @objective, @notes
            FROM training_plans
            WHERE id = @planId AND @startDate >= start_date
              AND (end_date IS NULL OR @startDate <= end_date)
              AND (end_date IS NULL OR @endDate IS NULL OR @endDate <= end_date)
              AND NOT EXISTS (
                  SELECT 1 FROM training_mesocycles existing
                  WHERE existing.plan_id = training_plans.id
                    AND existing.start_date <= COALESCE(@endDate, 'infinity'::date)
                    AND COALESCE(existing.end_date, 'infinity'::date) >= @startDate
              )
            RETURNING id
            """, ("planId", planId), ("name", request.Name.Trim()), ("sequence", request.SequenceNumber),
            ("startDate", request.StartDate), ("endDate", request.EndDate), ("objective", request.Objective), ("notes", request.Notes));
        try
        {
            var id = await command.ExecuteScalarAsync(cancellationToken);
            return id is null ? Conflict(new { message = "Las fechas del mesociclo deben estar dentro del plan y no solaparse con otro mesociclo." }) : CreatedAtAction(nameof(GetMesocycles), new { planId }, new { id = Convert.ToInt32(id), request.Name, request.SequenceNumber });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new { message = "Ya existe un mesociclo con ese número dentro del plan." });
        }
    }

    [HttpPut("mesocycles/{mesocycleId:int}")]
    public async Task<IActionResult> UpdateMesocycle(int mesocycleId, [FromBody] CreateTrainingMesocycleRequest request, CancellationToken cancellationToken)
    {
        var planId = await GetMesocyclePlanId(mesocycleId, cancellationToken);
        if (!planId.HasValue || !await CanManagePlan(planId.Value, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160 || request.SequenceNumber < 1)
            return BadRequest(new { message = "El nombre y el número de secuencia del mesociclo son obligatorios y válidos." });
        if (request.EndDate.HasValue && request.EndDate.Value < request.StartDate)
            return BadRequest(new { message = "La fecha final no puede ser anterior a la inicial." });
        await using var command = Command("""
            UPDATE training_mesocycles m
            SET name = @name, sequence_number = @sequence, start_date = @startDate, end_date = @endDate,
                objective = @objective, notes = @notes, updated_at = NOW()
            FROM training_plans p
            WHERE m.id = @id AND p.id = m.plan_id
              AND @startDate >= p.start_date
              AND (p.end_date IS NULL OR @startDate <= p.end_date)
              AND (p.end_date IS NULL OR @endDate IS NULL OR @endDate <= p.end_date)
              AND NOT EXISTS (
                  SELECT 1 FROM training_microcycles child
                  WHERE child.mesocycle_id = m.id
                    AND (child.start_date < @startDate OR
                         (@endDate IS NOT NULL AND (child.start_date > @endDate OR child.end_date IS NULL OR child.end_date > @endDate)))
              )
              AND NOT EXISTS (
                  SELECT 1 FROM training_mesocycles other
                  WHERE other.plan_id = m.plan_id AND other.id <> m.id
                    AND other.start_date <= COALESCE(@endDate, 'infinity'::date)
                    AND COALESCE(other.end_date, 'infinity'::date) >= @startDate
              )
            RETURNING m.id
            """, ("id", mesocycleId), ("name", request.Name.Trim()), ("sequence", request.SequenceNumber),
            ("startDate", request.StartDate), ("endDate", request.EndDate), ("objective", request.Objective), ("notes", request.Notes));
        try
        {
            var updated = await command.ExecuteScalarAsync(cancellationToken);
            return updated is null ? Conflict(new { message = "Las fechas deben encajar en el plan y no solaparse con otros mesociclos." }) : NoContent();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new { message = "Ese número de secuencia ya está asignado a otro mesociclo." });
        }
    }

    [HttpDelete("mesocycles/{mesocycleId:int}")]
    public async Task<IActionResult> DeleteMesocycle(int mesocycleId, CancellationToken cancellationToken)
    {
        var planId = await GetMesocyclePlanId(mesocycleId, cancellationToken);
        if (!planId.HasValue || !await CanManagePlan(planId.Value, cancellationToken)) return NotFound();
        await using var command = Command("""
            DELETE FROM training_mesocycles m
            WHERE m.id = @id
              AND NOT EXISTS (SELECT 1 FROM training_microcycles c WHERE c.mesocycle_id = m.id)
            RETURNING m.id
            """, ("id", mesocycleId));
        var deleted = await command.ExecuteScalarAsync(cancellationToken);
        if (deleted is not null) return NoContent();
        await using var exists = Command("SELECT EXISTS(SELECT 1 FROM training_mesocycles WHERE id = @id)", ("id", mesocycleId));
        return Convert.ToBoolean(await exists.ExecuteScalarAsync(cancellationToken))
            ? Conflict(new { message = "No se puede borrar un mesociclo que contiene microciclos. Elimínalos primero." })
            : NotFound();
    }

    [HttpPost("plans/{planId:int}/mesocycles/reorder")]
    public async Task<IActionResult> ReorderMesocycles(int planId, [FromBody] ReorderTrainingCyclesRequest request, CancellationToken cancellationToken)
    {
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        if (request.OrderedIds is null || request.OrderedIds.Length == 0 || request.OrderedIds.Distinct().Count() != request.OrderedIds.Length)
            return BadRequest(new { message = "La lista de mesociclos debe contener identificadores únicos." });
        await using var countCommand = Command("SELECT COUNT(*) FROM training_mesocycles WHERE plan_id = @planId AND id = ANY(@ids)", ("planId", planId), ("ids", request.OrderedIds));
        var matched = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        await using var totalCommand = Command("SELECT COUNT(*) FROM training_mesocycles WHERE plan_id = @planId", ("planId", planId));
        var total = Convert.ToInt32(await totalCommand.ExecuteScalarAsync(cancellationToken));
        if (matched != total || request.OrderedIds.Length != total)
            return BadRequest(new { message = "La lista debe incluir todos los mesociclos del plan exactamente una vez." });
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var shift = Command("""
                WITH sequence_offset AS (
                    SELECT COALESCE(MAX(sequence_number), 0) + COUNT(*) + 1 AS amount
                    FROM training_mesocycles WHERE plan_id = @planId
                )
                UPDATE training_mesocycles m SET sequence_number = m.sequence_number + sequence_offset.amount
                FROM sequence_offset WHERE m.plan_id = @planId
                """, ("planId", planId), transaction))
                await shift.ExecuteNonQueryAsync(cancellationToken);
            for (var i = 0; i < request.OrderedIds.Length; i++)
                await using (var update = Command("UPDATE training_mesocycles SET sequence_number = @sequence, updated_at = NOW() WHERE plan_id = @planId AND id = @id", ("sequence", i + 1), ("planId", planId), ("id", request.OrderedIds[i]), transaction))
                    await update.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpGet("mesocycles/{mesocycleId:int}/microcycles")]
    public async Task<IActionResult> GetMicrocycles(int mesocycleId, CancellationToken cancellationToken)
    {
        var planId = await GetMesocyclePlanId(mesocycleId, cancellationToken);
        if (!planId.HasValue || !await CanAccessPlan(planId.Value, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT id, mesocycle_id, name, sequence_number, start_date, end_date, focus, notes
            FROM training_microcycles WHERE mesocycle_id = @mesocycleId
            ORDER BY sequence_number, start_date, id
            """, ("mesocycleId", mesocycleId));
        var items = new List<TrainingMicrocycleDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new TrainingMicrocycleDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetFieldValue<DateOnly>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return Ok(items);
    }

    [HttpPost("mesocycles/{mesocycleId:int}/microcycles")]
    public async Task<IActionResult> CreateMicrocycle(int mesocycleId, [FromBody] CreateTrainingMicrocycleRequest request, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        var planId = await GetMesocyclePlanId(mesocycleId, cancellationToken);
        if (!planId.HasValue || !await CanManagePlan(planId.Value, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            return BadRequest(new { message = "El nombre del microciclo es obligatorio (máximo 160 caracteres)." });
        if (request.SequenceNumber < 1) return BadRequest(new { message = "El número de microciclo debe ser positivo." });
        if (request.EndDate.HasValue && request.EndDate.Value < request.StartDate)
            return BadRequest(new { message = "La fecha final no puede ser anterior a la inicial." });
        await using var command = Command("""
            INSERT INTO training_microcycles(mesocycle_id, name, sequence_number, start_date, end_date, focus, notes)
            SELECT id, @name, @sequence, @startDate, @endDate, @focus, @notes
            FROM training_mesocycles
            WHERE id = @mesocycleId AND @startDate >= start_date
              AND (end_date IS NULL OR @startDate <= end_date)
              AND (end_date IS NULL OR @endDate IS NULL OR @endDate <= end_date)
              AND NOT EXISTS (
                  SELECT 1 FROM training_microcycles existing
                  WHERE existing.mesocycle_id = training_mesocycles.id
                    AND existing.start_date <= COALESCE(@endDate, 'infinity'::date)
                    AND COALESCE(existing.end_date, 'infinity'::date) >= @startDate
              )
            RETURNING id
            """, ("mesocycleId", mesocycleId), ("name", request.Name.Trim()), ("sequence", request.SequenceNumber),
            ("startDate", request.StartDate), ("endDate", request.EndDate), ("focus", request.Focus), ("notes", request.Notes));
        try
        {
            var id = await command.ExecuteScalarAsync(cancellationToken);
            return id is null ? Conflict(new { message = "Las fechas del microciclo deben estar dentro del mesociclo y no solaparse con otro microciclo." }) : CreatedAtAction(nameof(GetMicrocycles), new { mesocycleId }, new { id = Convert.ToInt32(id), request.Name, request.SequenceNumber });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new { message = "Ya existe un microciclo con ese número dentro del mesociclo." });
        }
    }

    [HttpPut("microcycles/{microcycleId:int}")]
    public async Task<IActionResult> UpdateMicrocycle(int microcycleId, [FromBody] CreateTrainingMicrocycleRequest request, CancellationToken cancellationToken)
    {
        var mesocycleId = await _context.Database.SqlQueryRaw<int?>(
            """SELECT mesocycle_id AS "Value" FROM training_microcycles WHERE id = {0}""", microcycleId).SingleOrDefaultAsync(cancellationToken);
        if (!mesocycleId.HasValue) return NotFound();
        var planId = await GetMesocyclePlanId(mesocycleId.Value, cancellationToken);
        if (!planId.HasValue || !await CanManagePlan(planId.Value, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160 || request.SequenceNumber < 1)
            return BadRequest(new { message = "El nombre y el número de secuencia del microciclo son obligatorios y válidos." });
        if (request.EndDate.HasValue && request.EndDate.Value < request.StartDate)
            return BadRequest(new { message = "La fecha final no puede ser anterior a la inicial." });
        await using var command = Command("""
            UPDATE training_microcycles m
            SET name = @name, sequence_number = @sequence, start_date = @startDate, end_date = @endDate,
                focus = @focus, notes = @notes, updated_at = NOW()
            FROM training_mesocycles parent
            WHERE m.id = @id AND parent.id = m.mesocycle_id
              AND @startDate >= parent.start_date
              AND (parent.end_date IS NULL OR @startDate <= parent.end_date)
              AND (parent.end_date IS NULL OR @endDate IS NULL OR @endDate <= parent.end_date)
              AND NOT EXISTS (
                  SELECT 1 FROM training_microcycles other
                  WHERE other.mesocycle_id = m.mesocycle_id AND other.id <> m.id
                    AND other.start_date <= COALESCE(@endDate, 'infinity'::date)
                    AND COALESCE(other.end_date, 'infinity'::date) >= @startDate
              )
            RETURNING m.id
            """, ("id", microcycleId), ("name", request.Name.Trim()), ("sequence", request.SequenceNumber),
            ("startDate", request.StartDate), ("endDate", request.EndDate), ("focus", request.Focus), ("notes", request.Notes));
        try
        {
            var updated = await command.ExecuteScalarAsync(cancellationToken);
            return updated is null ? Conflict(new { message = "Las fechas deben encajar en el mesociclo y no solaparse con otros microciclos." }) : NoContent();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new { message = "Ese número de secuencia ya está asignado a otro microciclo." });
        }
    }

    [HttpDelete("microcycles/{microcycleId:int}")]
    public async Task<IActionResult> DeleteMicrocycle(int microcycleId, CancellationToken cancellationToken)
    {
        var mesocycleId = await _context.Database.SqlQueryRaw<int?>(
            """SELECT mesocycle_id AS "Value" FROM training_microcycles WHERE id = {0}""", microcycleId).SingleOrDefaultAsync(cancellationToken);
        if (!mesocycleId.HasValue) return NotFound();
        var planId = await GetMesocyclePlanId(mesocycleId.Value, cancellationToken);
        if (!planId.HasValue || !await CanManagePlan(planId.Value, cancellationToken)) return NotFound();
        await using var command = Command("DELETE FROM training_microcycles WHERE id = @id RETURNING id", ("id", microcycleId));
        return await command.ExecuteScalarAsync(cancellationToken) is null ? NotFound() : NoContent();
    }

    [HttpPost("mesocycles/{mesocycleId:int}/microcycles/reorder")]
    public async Task<IActionResult> ReorderMicrocycles(int mesocycleId, [FromBody] ReorderTrainingCyclesRequest request, CancellationToken cancellationToken)
    {
        var planId = await GetMesocyclePlanId(mesocycleId, cancellationToken);
        if (!planId.HasValue || !await CanManagePlan(planId.Value, cancellationToken)) return NotFound();
        if (request.OrderedIds is null || request.OrderedIds.Length == 0 || request.OrderedIds.Distinct().Count() != request.OrderedIds.Length)
            return BadRequest(new { message = "La lista de microciclos debe contener identificadores únicos." });
        await using var countCommand = Command("SELECT COUNT(*) FROM training_microcycles WHERE mesocycle_id = @parentId AND id = ANY(@ids)", ("parentId", mesocycleId), ("ids", request.OrderedIds));
        var matched = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        await using var totalCommand = Command("SELECT COUNT(*) FROM training_microcycles WHERE mesocycle_id = @parentId", ("parentId", mesocycleId));
        var total = Convert.ToInt32(await totalCommand.ExecuteScalarAsync(cancellationToken));
        if (matched != total || request.OrderedIds.Length != total)
            return BadRequest(new { message = "La lista debe incluir todos los microciclos del mesociclo exactamente una vez." });
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var shift = Command("""
                WITH sequence_offset AS (
                    SELECT COALESCE(MAX(sequence_number), 0) + COUNT(*) + 1 AS amount
                    FROM training_microcycles WHERE mesocycle_id = @parentId
                )
                UPDATE training_microcycles m SET sequence_number = m.sequence_number + sequence_offset.amount
                FROM sequence_offset WHERE m.mesocycle_id = @parentId
                """, ("parentId", mesocycleId), transaction))
                await shift.ExecuteNonQueryAsync(cancellationToken);
            for (var i = 0; i < request.OrderedIds.Length; i++)
                await using (var update = Command("UPDATE training_microcycles SET sequence_number = @sequence, updated_at = NOW() WHERE mesocycle_id = @parentId AND id = @id", ("sequence", i + 1), ("parentId", mesocycleId), ("id", request.OrderedIds[i]), transaction))
                    await update.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpGet("clients/{clientId:int}/collaborations")]
    public async Task<IActionResult> GetCollaborations(int clientId, CancellationToken cancellationToken)
    {
        if (!await CanManageClient(clientId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT tc.id, tc.professional_user_id, u.full_name, u.email, tc.access_scope, tc.status,
                   tc.created_at, tc.accepted_at, tc.revoked_at
            FROM training_collaborations tc
            JOIN users u ON u.id = tc.professional_user_id
            WHERE tc.client_id = @clientId
            ORDER BY tc.created_at DESC, tc.id DESC
            """, ("clientId", clientId));
        var items = new List<TrainingCollaborationDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new TrainingCollaborationDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetDateTime(7), reader.IsDBNull(8) ? null : reader.GetDateTime(8)));
        return Ok(items);
    }

    [HttpPost("clients/{clientId:int}/collaborations")]
    public async Task<IActionResult> InviteCollaborator(int clientId, [FromBody] InviteTrainingCollaboratorRequest request, CancellationToken cancellationToken)
    {
        var inviterId = AuthHelpers.GetUserId(User);
        if (inviterId is null) return Unauthorized();
        if (!await CanManageClient(clientId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Trim().Length > 254)
            return BadRequest(new { message = "Introduce el correo electrónico del profesional invitado." });
        if (request.AccessScope is not (null or "training"))
            return BadRequest(new { message = "Por ahora solo está disponible el acceso de consulta al entrenamiento." });

        await using var findProfessional = Command("""
            SELECT id FROM users
            WHERE LOWER(email) = LOWER(@email)
              AND role IN ('nutritionist','clinic_admin')
              AND archived_at IS NULL
            LIMIT 1
            """, ("email", request.Email.Trim()));
        var professionalIdValue = await findProfessional.ExecuteScalarAsync(cancellationToken);
        if (professionalIdValue is null) return NotFound(new { message = "No se ha encontrado una cuenta profesional activa con ese correo." });
        var professionalId = Convert.ToInt32(professionalIdValue);
        if (professionalId == inviterId.Value) return BadRequest(new { message = "No puedes invitarte a ti mismo." });

        await using var command = Command("""
            INSERT INTO training_collaborations(client_id, professional_user_id, tenant_id, access_scope, status, invited_by_user_id, created_at, accepted_at, revoked_at)
            SELECT c.id, @professionalId, c.tenant_id, @scope, 'pending', @inviterId, NOW(), NULL, NULL
            FROM clients c
            WHERE c.id = @clientId AND c.archived_at IS NULL
              AND c.tenant_id IS NOT NULL
              AND c.tenant_id = (SELECT u.tenant_id FROM users u WHERE u.id = @professionalId)
            ON CONFLICT(client_id, professional_user_id) DO UPDATE SET
                tenant_id = EXCLUDED.tenant_id, access_scope = EXCLUDED.access_scope,
                status = 'pending', invited_by_user_id = EXCLUDED.invited_by_user_id,
                created_at = NOW(), accepted_at = NULL, revoked_at = NULL
            RETURNING id, status
            """, ("clientId", clientId), ("professionalId", professionalId), ("scope", request.AccessScope ?? "training"),
            ("inviterId", inviterId.Value));
        int collaborationId;
        string status;
        await using (var result = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await result.ReadAsync(cancellationToken))
                return BadRequest(new { message = "El profesional debe pertenecer a la misma clínica para colaborar en este expediente." });
            collaborationId = result.GetInt32(0);
            status = result.GetString(1);
        }

        // El correo es una notificación complementaria: un fallo SMTP no invalida la invitación persistida.
        string? inviteeEmail;
        await using (var emailCommand = Command("""
            SELECT u.email
            FROM training_collaborations tc
            JOIN users u ON u.id = tc.professional_user_id
            WHERE tc.id = @id
            """, ("id", collaborationId)))
            inviteeEmail = Convert.ToString(await emailCommand.ExecuteScalarAsync(cancellationToken));

        var emailSent = false;
        if (!string.IsNullOrWhiteSpace(inviteeEmail))
        {
            var emailResult = await _emailServ.SendEmailAsync(
                inviteeEmail,
                "Invitación para colaborar en un plan deportivo · DietoExpress",
                "<p>Has recibido una invitación para consultar un plan de entrenamiento en DietoExpress.</p><p>Inicia sesión en tu cuenta y abre <strong>Invitaciones de entrenamiento</strong> para revisar y aceptar la solicitud.</p><p>El acceso es de solo lectura y queda limitado al ámbito autorizado de la clínica.</p>",
                $"training-collaboration-{collaborationId}");
            emailSent = emailResult.Exito;
        }

        return Ok(new {
            id = collaborationId,
            status,
            emailSent,
            message = emailSent
                ? "Invitación creada y solicitud de correo aceptada por el servidor SMTP. El profesional debe aceptarla para acceder."
                : "Invitación creada, pero no se pudo enviar el aviso por correo. El profesional puede encontrarla dentro de DietoExpress."
        });
    }

    [HttpPost("collaborations/{collaborationId:int}/accept")]
    public async Task<IActionResult> AcceptCollaboration(int collaborationId, CancellationToken cancellationToken)
    {
        var professionalId = AuthHelpers.GetUserId(User);
        if (professionalId is null) return Unauthorized();
        await using var command = Command("""
            UPDATE training_collaborations
            SET status = 'active', accepted_at = NOW(), revoked_at = NULL
            WHERE id = @id AND professional_user_id = @professionalId AND status = 'pending'
              AND EXISTS (
                  SELECT 1 FROM clients c
                  WHERE c.id = training_collaborations.client_id AND c.archived_at IS NULL
                    AND training_collaborations.tenant_id IS NOT DISTINCT FROM (SELECT u.tenant_id FROM users u WHERE u.id = @professionalId)
              )
            RETURNING id, client_id
            """, ("id", collaborationId), ("professionalId", professionalId.Value));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound(new { message = "La invitación no existe, ya fue respondida o no te corresponde." });
        return Ok(new { id = reader.GetInt32(0), clientId = reader.GetInt32(1), status = "active" });
    }

    [HttpPost("collaborations/{collaborationId:int}/revoke")]
    public async Task<IActionResult> RevokeCollaboration(int collaborationId, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        await using var command = Command("""
            UPDATE training_collaborations tc
            SET status = 'revoked', revoked_at = NOW()
            WHERE tc.id = @id AND tc.status IN ('pending','active')
              AND (tc.invited_by_user_id = @userId OR EXISTS (
                  SELECT 1 FROM clients c
                  WHERE c.id = tc.client_id AND c.archived_at IS NULL
                    AND (c.user_id = @userId OR c.tenant_id = (SELECT u.tenant_id FROM users u WHERE u.id = @userId AND u.role = 'clinic_admin'))
              ))
            RETURNING tc.id, tc.client_id
            """, ("id", collaborationId), ("userId", userId.Value));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound(new { message = "No se encontró una colaboración revocable para este usuario." });
        return Ok(new { id = reader.GetInt32(0), clientId = reader.GetInt32(1), status = "revoked" });
    }

    [HttpGet("collaborations/invitations")]
    public async Task<IActionResult> GetMyInvitations(CancellationToken cancellationToken)
    {
        var professionalId = AuthHelpers.GetUserId(User);
        if (professionalId is null) return Unauthorized();
        await using var command = Command("""
            SELECT tc.id, tc.client_id, c.full_name, tc.access_scope, tc.created_at, u.full_name
            FROM training_collaborations tc
            JOIN clients c ON c.id = tc.client_id
            JOIN users u ON u.id = tc.invited_by_user_id
            WHERE tc.professional_user_id = @professionalId AND tc.status = 'pending' AND c.archived_at IS NULL
              AND tc.tenant_id IS NOT DISTINCT FROM (SELECT u.tenant_id FROM users u WHERE u.id = @professionalId)
            ORDER BY tc.created_at DESC
            """, ("professionalId", professionalId.Value));
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new { id = reader.GetInt32(0), clientId = reader.GetInt32(1), clientName = reader.GetString(2),
                accessScope = reader.GetString(3), createdAt = reader.GetDateTime(4), invitedBy = reader.GetString(5) });
        return Ok(items);
    }

    private async Task<bool> CanAccessClient(int clientId, CancellationToken cancellationToken) =>
        await CanAccessClientInternal(clientId, manage: false, cancellationToken);

    private async Task<bool> CanManageClient(int clientId, CancellationToken cancellationToken) =>
        await CanAccessClientInternal(clientId, manage: true, cancellationToken);

    private async Task<bool> CanAccessClientInternal(int clientId, bool manage, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return false;
        var superAdmin = User.IsInRole("superadmin");
        var clinicAdmin = User.IsInRole("clinic_admin");
        await using var command = Command("""
            SELECT EXISTS (
                SELECT 1 FROM clients c
                WHERE c.id = @clientId AND c.archived_at IS NULL
                  AND (@superAdmin OR (
                      c.tenant_id = (SELECT u.tenant_id FROM users u WHERE u.id = @userId)
                      AND (@clinicAdmin OR c.user_id = @userId
                           OR EXISTS (SELECT 1 FROM client_nutritionist_assignments a WHERE a.client_id = c.id AND a.nutritionist_id = @userId AND a.is_active)
                           OR (NOT @manage AND EXISTS (
                               SELECT 1 FROM training_collaborations tc
                               WHERE tc.client_id = c.id AND tc.professional_user_id = @userId
                                 AND tc.status = 'active'
                           ))
                      )
                  ))
            )
            """, ("clientId", clientId), ("userId", userId.Value), ("superAdmin", superAdmin), ("clinicAdmin", clinicAdmin), ("manage", manage));
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<bool> CanAccessPlan(int planId, CancellationToken cancellationToken) =>
        await CanAccessPlanInternal(planId, manage: false, cancellationToken);

    private async Task<bool> CanManagePlan(int planId, CancellationToken cancellationToken) =>
        await CanAccessPlanInternal(planId, manage: true, cancellationToken);

    private async Task<bool> CanAccessPlanInternal(int planId, bool manage, CancellationToken cancellationToken)
    {
        var clientId = await _context.Database.SqlQueryRaw<int?>(
            """SELECT f.client_id AS "Value" FROM training_plans p JOIN training_profiles f ON f.id = p.profile_id WHERE p.id = {0}""", planId)
            .SingleOrDefaultAsync(cancellationToken);
        return clientId.HasValue && await CanAccessClientInternal(clientId.Value, manage, cancellationToken);
    }


    private async Task<int?> GetMesocyclePlanId(int mesocycleId, CancellationToken cancellationToken) =>
        await _context.Database.SqlQueryRaw<int?>(
            """SELECT plan_id AS "Value" FROM training_mesocycles WHERE id = {0}""", mesocycleId)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<int?> GetSessionClientId(int sessionId, CancellationToken cancellationToken) =>
        await _context.Database.SqlQueryRaw<int?>(
            """SELECT f.client_id AS "Value" FROM training_sessions s JOIN training_plans p ON p.id = s.plan_id JOIN training_profiles f ON f.id = p.profile_id WHERE s.id = {0}""", sessionId)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task NotifyTrainingPlanAssignedAsync(int clientId, int planId, string planName, CancellationToken cancellationToken)
    {
        var tenantId = await _context.clients.AsNoTracking()
            .Where(c => c.id == clientId && c.archived_at == null)
            .Select(c => c.tenant_id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!tenantId.HasValue) return;

        try
        {
            var notifications = HttpContext.RequestServices.GetService<Anguloso.Server.Logica.NotificationService>();
            if (notifications != null)
                await notifications.CreateForPatientAsync(tenantId.Value, clientId, "training_plan_assigned",
                    "Nuevo plan de entrenamiento", $"Tu profesional ha preparado el plan «{planName}». Ya puedes consultarlo en DietoExpress.",
                    "/patient?tab=training", true, $"training-plan:{planId}:assigned");
            var push = HttpContext.RequestServices.GetService<Anguloso.Server.Logica.PatientPushService>();
            if (push != null)
                await push.SendToClientAsync(clientId, "Nuevo plan de entrenamiento",
                    "Tu profesional ha preparado un plan. Abre DietoExpress para consultarlo.",
                    "training", $"training-plan:{planId}:assigned", cancellationToken);
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<TrainingPeriodizationController>>()
                .LogWarning(ex, "No se pudo notificar la asignación del plan de entrenamiento {PlanId}.", planId);
        }
    }

    private NpgsqlCommand Command(string sql, params (string Name, object? Value)[] parameters) => CreateCommand(sql, null, parameters);

    private NpgsqlCommand Command(string sql, (string Name, object? Value) first, (string Name, object? Value) second, NpgsqlTransaction transaction) =>
        CreateCommand(sql, transaction, first, second);

    private NpgsqlCommand Command(string sql, (string Name, object? Value) first, NpgsqlTransaction transaction) =>
        CreateCommand(sql, transaction, first);

    private NpgsqlCommand Command(string sql, (string Name, object? Value) first, (string Name, object? Value) second,
        (string Name, object? Value) third, NpgsqlTransaction transaction) =>
        CreateCommand(sql, transaction, first, second, third);

    private NpgsqlCommand CreateCommand(string sql, NpgsqlTransaction? transaction, params (string Name, object? Value)[] parameters)
    {
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (transaction is not null) command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            if (value is DateOnly) parameter.NpgsqlDbType = NpgsqlDbType.Date;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}

public sealed record TrainingProfileDto(int Id, int ClientId, bool Enabled, string PrimaryGoal, string ExperienceLevel, string ManagementMode, string? Notes, DateTime CreatedAt, DateTime UpdatedAt, bool CanManage = true);
public sealed record UpdateTrainingProfileRequest(bool Enabled, string PrimaryGoal, string ExperienceLevel, string? Notes, string? ManagementMode = null);
public sealed record CreateTrainingPlanRequest(string Name, string PeriodizationModel, DateOnly StartDate, DateOnly? EndDate, string? Notes);
public sealed record TrainingPlanDto(int Id, string Name, string PeriodizationModel, DateOnly StartDate, DateOnly? EndDate, string Status, int Version, string? Notes);
public sealed record CreateTrainingSessionRequest(DateOnly ScheduledDate, string Title, string SessionType, int? DurationMinutes, string? IntensityTarget, string? Instructions, string? ExercisePrescription = null);
public sealed record RecordTrainingSessionRequest(DateTime? CompletedAt, int? ActualDurationMinutes, decimal? SessionRpe, string? Notes);
public sealed record TrainingSessionDto(int Id, int PlanId, DateOnly ScheduledDate, string Title, string SessionType, int? DurationMinutes, string? IntensityTarget, string? Instructions, string Status, DateTime? CompletedAt, int? ActualDurationMinutes, decimal? SessionRpe, string? RecordNotes, string? ExercisePrescription = null);
public sealed record InviteTrainingCollaboratorRequest(string Email, string? AccessScope = "training");
public sealed record TrainingCollaborationDto(int Id, int ProfessionalUserId, string ProfessionalName, string Email, string AccessScope, string Status, DateTime CreatedAt, DateTime? AcceptedAt, DateTime? RevokedAt);

public sealed record CreateTrainingMesocycleRequest(string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate, string? Objective, string? Notes);
public sealed record GenerateTrainingMesocycleRequest(string Name, int SequenceNumber, TrainingProgramGenerationRequest Generation, string? Notes = null);
public sealed record TrainingMesocycleDto(int Id, int PlanId, string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate, string? Objective, string? Notes);
public sealed record CreateTrainingMicrocycleRequest(string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate, string? Focus, string? Notes);
public sealed record ReorderTrainingCyclesRequest(int[] OrderedIds);
public sealed record SaveTrainingTemplateRequest(string Name);
public sealed record SaveTrainingSessionTemplateRequest(string Name, string SessionType, int? DurationMinutes, string? IntensityTarget, string? Instructions, string? ExercisePrescription);
public sealed record ApplyTrainingTemplateRequest(string Name, DateOnly StartDate);
public sealed record TrainingTemplateSession(DateOnly ScheduledDate, string Title, string SessionType, int? DurationMinutes,
    string? IntensityTarget, string? Instructions, string? ExercisePrescription);
public sealed record TrainingTemplatePayload(string Name, string PeriodizationModel, DateOnly StartDate, DateOnly? EndDate,
    string? Notes, List<TrainingTemplateSession> Sessions, List<TrainingTemplateMesocycle> Mesocycles);
public sealed record TrainingTemplateMesocycle(int Id, string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate,
    string? Objective, string? Notes, List<TrainingTemplateMicrocycle> Microcycles);
public sealed record TrainingTemplateMicrocycle(string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate,
    string? Focus, string? Notes);

public sealed record TrainingMicrocycleDto(int Id, int MesocycleId, string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate, string? Focus, string? Notes);
