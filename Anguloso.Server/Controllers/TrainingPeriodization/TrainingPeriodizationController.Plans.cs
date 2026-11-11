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
/// Perfil de entrenamiento del cliente y ciclo de vida de los planes (crear, publicar, editar, borrar, rutina rápida).
/// </summary>
public sealed partial class TrainingPeriodizationController
{
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
        return CreatedAtAction(nameof(GetPlans), new { clientId }, new { id = planId, request.Name, request.PeriodizationModel, request.StartDate, request.EndDate, status = "draft", version = 1 });
    }

    [HttpPost("plans/{planId:int}/publish")]
    public async Task<IActionResult> PublishPlan(int planId, CancellationToken cancellationToken)
    {
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();

        int clientId;
        string planName;
        await using (var publish = Command("""
            UPDATE training_plans p
            SET status = 'active', version = version + 1, updated_at = NOW()
            FROM training_profiles f
            WHERE p.id = @planId AND p.profile_id = f.id AND f.enabled = TRUE
              AND p.status = 'draft'
              AND EXISTS (SELECT 1 FROM training_sessions s WHERE s.plan_id = p.id)
              AND NOT EXISTS (
                  SELECT 1 FROM training_mesocycles m
                  WHERE m.plan_id = p.id AND m.review_status <> 'reviewed'
              )
              AND NOT EXISTS (
                  SELECT 1 FROM training_review_requests r
                  WHERE r.plan_id = p.id AND r.status = 'pending'
              )
            RETURNING f.client_id, p.name
            """, ("planId", planId)))
        await using (var reader = await publish.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                clientId = reader.GetInt32(0);
                planName = reader.GetString(1);
            }
            else
            {
                clientId = 0;
                planName = string.Empty;
            }
        }

        if (clientId > 0)
        {
            await NotifyTrainingPlanAssignedAsync(clientId, planId, planName, cancellationToken);
            return Ok(new { id = planId, status = "active", alreadyPublished = false });
        }

        await using var state = Command("""
            SELECT p.status, f.client_id, p.name,
                   EXISTS (SELECT 1 FROM training_sessions s WHERE s.plan_id = p.id),
                   EXISTS (SELECT 1 FROM training_mesocycles m WHERE m.plan_id = p.id AND m.review_status <> 'reviewed'),
                   EXISTS (SELECT 1 FROM training_review_requests r WHERE r.plan_id = p.id AND r.status = 'pending')
            FROM training_plans p
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE p.id = @planId AND f.enabled = TRUE
            """, ("planId", planId));
        await using var stateReader = await state.ExecuteReaderAsync(cancellationToken);
        if (!await stateReader.ReadAsync(cancellationToken)) return NotFound();
        var status = stateReader.GetString(0);
        if (status == "active") return Ok(new { id = planId, status, alreadyPublished = true });
        if (status != "draft")
            return Conflict(new { message = "Solo se pueden publicar planes que estén en borrador." });
        if (!stateReader.GetBoolean(3))
            return Conflict(new { message = "Añade al menos una sesión antes de publicar el plan." });
        if (stateReader.GetBoolean(4))
            return Conflict(new { message = "Todos los mesociclos deben estar revisados profesionalmente antes de publicar." });
        if (stateReader.GetBoolean(5))
            return Conflict(new { message = "Resuelve las solicitudes de revisión pendientes antes de publicar." });
        return Conflict(new { message = "No se ha podido publicar el plan. Recarga la planificación e inténtalo de nuevo." });
    }

    [HttpPut("plans/{planId:int}")]
    public async Task<IActionResult> UpdatePlan(int planId, [FromBody] UpdateTrainingPlanRequest request, CancellationToken cancellationToken)
    {
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            return BadRequest(new { message = "El nombre debe tener entre 1 y 160 caracteres." });
        if (request.EndDate.HasValue && request.EndDate.Value < request.StartDate)
            return BadRequest(new { message = "La fecha final no puede ser anterior a la inicial." });
        if (request.PeriodizationModel is not ("linear" or "undulating" or "block" or "reverse" or "continuous_competition" or "autoregulatory" or "custom"))
            return BadRequest(new { message = "El modelo de periodización no es válido." });

        await using (var children = Command("""
            SELECT EXISTS (
                SELECT 1 FROM training_plans current
                WHERE current.id = @planId
                  AND (current.start_date <> @startDate OR current.end_date IS DISTINCT FROM @endDate)
                  AND (
                    EXISTS (SELECT 1 FROM training_sessions
                            WHERE plan_id = @planId AND (scheduled_date < @startDate OR (@endDate IS NOT NULL AND scheduled_date > @endDate)))
                    OR EXISTS (SELECT 1 FROM training_mesocycles
                               WHERE plan_id = @planId AND (start_date < @startDate OR (@endDate IS NOT NULL AND COALESCE(end_date, start_date) > @endDate)))
                    OR EXISTS (SELECT 1 FROM training_macrocycles
                               WHERE plan_id = @planId AND (start_date < @startDate OR (@endDate IS NOT NULL AND COALESCE(end_date, start_date) > @endDate)))
                    OR EXISTS (SELECT 1 FROM training_macrocycle_phases phase
                               JOIN training_macrocycles macro ON macro.id = phase.macrocycle_id
                               WHERE macro.plan_id = @planId AND (phase.start_date < @startDate OR (@endDate IS NOT NULL AND phase.end_date > @endDate)))
                  )
            )
            """, ("planId", planId), ("startDate", request.StartDate), ("endDate", request.EndDate)))
        {
            if (Convert.ToBoolean(await children.ExecuteScalarAsync(cancellationToken)))
                return Conflict(new { message = "Las fechas no pueden excluir sesiones o bloques que ya forman parte de esta planificación." });
        }

        await using var command = Command("""
            UPDATE training_plans p
            SET name = @name, periodization_model = @model, start_date = @startDate, end_date = @endDate,
                notes = @notes, version = version + 1, updated_at = NOW()
            FROM training_profiles f
            WHERE p.id = @planId AND p.profile_id = f.id AND f.enabled = TRUE AND p.version = @version
            RETURNING p.id, p.name, p.periodization_model, p.start_date, p.end_date, p.status, p.version, p.notes
            """, ("planId", planId), ("name", request.Name.Trim()), ("model", request.PeriodizationModel),
            ("startDate", request.StartDate), ("endDate", request.EndDate), ("notes", request.Notes),
            ("version", request.Version));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Conflict(new { message = "La planificación ha cambiado desde que la abriste. Recarga la página antes de volver a editarla." });
        return Ok(new TrainingPlanDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateOnly>(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4), reader.GetString(5), reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetString(7)));
    }

    [HttpDelete("plans/{planId:int}")]
    public async Task<IActionResult> DeletePlan(int planId, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("superadmin") && !await CanManagePlan(planId, cancellationToken)) return NotFound();
        await using var command = Command("""
            DELETE FROM training_plans p
            USING training_profiles f
            WHERE p.id = @planId AND p.profile_id = f.id
            RETURNING p.id
            """, ("planId", planId));
        var deleted = await command.ExecuteScalarAsync(cancellationToken);
        return deleted is null ? NotFound() : NoContent();
    }

    [HttpPost("clients/{clientId:int}/quick-routine")]
    public async Task<IActionResult> CreateQuickRoutine(int clientId, [FromBody] CreateQuickTrainingRoutineRequest request, CancellationToken cancellationToken)
    {
        if (!await CanManageClient(clientId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            return BadRequest(new { message = "El nombre de la rutina es obligatorio (máximo 160 caracteres)." });
        if (request.DurationMinutes is < 1 or > 1440)
            return BadRequest(new { message = "La duración debe estar entre 1 y 1440 minutos." });
        if (request.SessionType is not ("endurance" or "strength" or "mobility" or "recovery" or "health" or "general" or "other"))
            return BadRequest(new { message = "El tipo de sesión no es válido." });
        if (request.IntensityTarget?.Length > 120 || request.Instructions?.Length > 4000 || request.ExercisePrescription?.Length > 6000)
            return BadRequest(new { message = "La intensidad admite 120 caracteres, las instrucciones 4000 y los ejercicios 6000." });

        await using (var profile = Command("""
            SELECT management_mode FROM training_profiles
            WHERE client_id = @clientId AND enabled = TRUE
            """, ("clientId", clientId)))
        {
            var mode = await profile.ExecuteScalarAsync(cancellationToken) as string;
            if (mode != "self")
                return Conflict(new { message = "Activa la gestión propia del entrenamiento antes de crear una rutina desde la consulta." });
        }

        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var planCommand = CreateCommand("""
                INSERT INTO training_plans(profile_id, name, periodization_model, start_date, end_date, status, notes, created_by_user_id)
                SELECT f.id, @name, 'custom', @date, NULL, 'active', 'Rutina creada desde la consulta guiada', @userId
                FROM training_profiles f
                WHERE f.client_id = @clientId AND f.enabled = TRUE AND f.management_mode = 'self'
                RETURNING id
                """, transaction, ("clientId", clientId), ("name", request.Name.Trim()),
                ("date", request.ScheduledDate), ("userId", userId.Value));
            var planValue = await planCommand.ExecuteScalarAsync(cancellationToken);
            if (planValue is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new { message = "No se pudo crear la rutina. Comprueba que la gestión del entrenamiento está activada." });
            }

            var planId = Convert.ToInt32(planValue);
            await using var sessionCommand = CreateCommand("""
                INSERT INTO training_sessions(plan_id, scheduled_date, title, session_type, duration_minutes, intensity_target, instructions, exercise_prescription)
                VALUES (@planId, @date, @title, @type, @duration, @intensity, @instructions, @prescription)
                RETURNING id
                """, transaction, ("planId", planId), ("date", request.ScheduledDate), ("title", request.Name.Trim()),
                ("type", request.SessionType), ("duration", request.DurationMinutes), ("intensity", request.IntensityTarget?.Trim()),
                ("instructions", request.Instructions?.Trim()), ("prescription", request.ExercisePrescription?.Trim()));
            var sessionId = Convert.ToInt32(await sessionCommand.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            await NotifyTrainingPlanAssignedAsync(clientId, planId, request.Name.Trim(), cancellationToken);
            return CreatedAtAction(nameof(GetPlans), new { clientId },
                new { planId, sessionId, name = request.Name.Trim(), scheduledDate = request.ScheduledDate, message = "Rutina creada y asignada al cliente." });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
