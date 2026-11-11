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
/// Macrociclo, mesociclos y microciclos, y revisiones pendientes.
/// </summary>
public sealed partial class TrainingPeriodizationController
{
    [HttpGet("plans/{planId:int}/macrocycle")]
    public async Task<IActionResult> GetMacrocycle(int planId, CancellationToken cancellationToken)
    {
        if (!await CanAccessPlan(planId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT id, plan_id, name, periodization_model, start_date, end_date, notes
            FROM training_macrocycles WHERE plan_id = @planId
            """, ("planId", planId));
        MacrocycleDto? macrocycle;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return Ok(null);
            macrocycle = new MacrocycleDto(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetString(3), reader.GetFieldValue<DateOnly>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), []);
        }

        await using var phasesCommand = Command("""
            SELECT id, name, sequence_number, start_date, end_date, objective, phase_type
            FROM training_macrocycle_phases WHERE macrocycle_id = @macrocycleId
            ORDER BY sequence_number, start_date, id
            """, ("macrocycleId", macrocycle.Id));
        var phases = new List<MacrocyclePhaseDto>();
        await using (var reader = await phasesCommand.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                phases.Add(new MacrocyclePhaseDto(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2),
                    reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<DateOnly>(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6)));
        return Ok(macrocycle with { Phases = phases });
    }

    [HttpPut("plans/{planId:int}/macrocycle")]
    public async Task<IActionResult> SaveMacrocycle(int planId, [FromBody] SaveTrainingMacrocycleRequest request, CancellationToken cancellationToken)
    {
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        if (request.Phases is null)
            return BadRequest(new { message = "Añade al menos una fase al macrociclo." });
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            return BadRequest(new { message = "El nombre del macrociclo es obligatorio (máximo 160 caracteres)." });
        if (string.IsNullOrWhiteSpace(request.PeriodizationModel) || request.PeriodizationModel.Length > 40)
            return BadRequest(new { message = "Selecciona un modelo de periodización válido." });
        if (request.EndDate.HasValue && request.EndDate.Value < request.StartDate)
            return BadRequest(new { message = "La fecha final del macrociclo no puede ser anterior a la inicial." });
        if (request.Notes?.Length > 2000 || request.Phases.Count > 24)
            return BadRequest(new { message = "Las notas admiten 2000 caracteres y un macrociclo puede contener hasta 24 fases." });
        if (request.Phases.Any(phase => string.IsNullOrWhiteSpace(phase.Name) || phase.Name.Trim().Length > 160 ||
            phase.Objective?.Length > 500 || phase.SequenceNumber < 1 || phase.EndDate < phase.StartDate ||
            phase.PhaseType is not ("accumulation" or "intensification" or "deload" or "realization" or "general")))
            return BadRequest(new { message = "Revisa el nombre, tipo, objetivo y fechas de las fases." });
        if (request.Phases.Select(phase => phase.SequenceNumber).Distinct().Count() != request.Phases.Count)
            return BadRequest(new { message = "Cada fase debe tener un número de orden distinto." });
        var orderedPhases = request.Phases.OrderBy(phase => phase.StartDate).ThenBy(phase => phase.SequenceNumber).ToArray();
        for (var index = 0; index < orderedPhases.Length; index++)
        {
            var phase = orderedPhases[index];
            if (phase.StartDate < request.StartDate || (request.EndDate.HasValue && phase.EndDate > request.EndDate.Value))
                return BadRequest(new { message = $"La fase «{phase.Name}» queda fuera de las fechas del macrociclo." });
            if (index > 0 && phase.StartDate <= orderedPhases[index - 1].EndDate)
                return Conflict(new { message = "Las fases del macrociclo no pueden solaparse." });
        }

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var planCommand = connection.CreateCommand())
            {
                planCommand.Transaction = transaction;
                planCommand.CommandText = """
                    SELECT start_date, end_date FROM training_plans WHERE id = @planId
                    """;
                planCommand.Parameters.AddWithValue("planId", planId);
                await using var reader = await planCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return NotFound();
                var planStart = reader.GetFieldValue<DateOnly>(0);
                var planEnd = reader.IsDBNull(1) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(1);
                if (request.StartDate < planStart || (planEnd.HasValue && (!request.EndDate.HasValue || request.EndDate.Value > planEnd.Value)))
                    return BadRequest(new { message = "Las fechas del macrociclo deben quedar dentro de las fechas del plan de entrenamiento." });
            }

            long macrocycleId;
            await using (var upsert = connection.CreateCommand())
            {
                upsert.Transaction = transaction;
                upsert.CommandText = """
                    INSERT INTO training_macrocycles(plan_id, name, periodization_model, start_date, end_date, notes, updated_at)
                    VALUES (@planId, @name, @model, @startDate, @endDate, @notes, NOW())
                    ON CONFLICT (plan_id) DO UPDATE SET name = EXCLUDED.name,
                        periodization_model = EXCLUDED.periodization_model, start_date = EXCLUDED.start_date,
                        end_date = EXCLUDED.end_date, notes = EXCLUDED.notes, updated_at = NOW()
                    RETURNING id
                    """;
                upsert.Parameters.AddWithValue("planId", planId);
                upsert.Parameters.AddWithValue("name", request.Name.Trim());
                upsert.Parameters.AddWithValue("model", request.PeriodizationModel.Trim());
                upsert.Parameters.AddWithValue("startDate", request.StartDate);
                upsert.Parameters.AddWithValue("endDate", (object?)request.EndDate ?? DBNull.Value);
                upsert.Parameters.AddWithValue("notes", (object?)request.Notes?.Trim() ?? DBNull.Value);
                macrocycleId = Convert.ToInt64(await upsert.ExecuteScalarAsync(cancellationToken));
            }
            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM training_macrocycle_phases WHERE macrocycle_id = @macrocycleId";
                delete.Parameters.AddWithValue("macrocycleId", macrocycleId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }
            foreach (var phase in request.Phases.OrderBy(phase => phase.SequenceNumber))
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO training_macrocycle_phases(macrocycle_id, name, sequence_number, start_date, end_date, objective, phase_type)
                    VALUES (@macrocycleId, @name, @sequence, @startDate, @endDate, @objective, @phaseType)
                    """;
                insert.Parameters.AddWithValue("macrocycleId", macrocycleId);
                insert.Parameters.AddWithValue("name", phase.Name.Trim());
                insert.Parameters.AddWithValue("sequence", phase.SequenceNumber);
                insert.Parameters.AddWithValue("startDate", phase.StartDate);
                insert.Parameters.AddWithValue("endDate", phase.EndDate);
                insert.Parameters.AddWithValue("objective", (object?)phase.Objective?.Trim() ?? DBNull.Value);
                insert.Parameters.AddWithValue("phaseType", phase.PhaseType);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        await transaction.DisposeAsync();
        return await GetMacrocycle(planId, cancellationToken);
    }

    [HttpPost("mesocycles/{mesocycleId:int}/review-status")]
    public async Task<IActionResult> SetMesocycleReviewStatus(
        int mesocycleId, [FromBody] SetMesocycleReviewStatusRequest request, CancellationToken cancellationToken)
    {
        if (request.Status is not ("draft" or "reviewed"))
            return BadRequest(new { message = "El estado debe ser draft o reviewed." });
        var planId = await GetMesocyclePlanId(mesocycleId, cancellationToken);
        if (!planId.HasValue || !await CanManagePlan(planId.Value, cancellationToken)) return NotFound();
        await using var command = Command(
            "UPDATE training_mesocycles SET review_status = @status, updated_at = NOW() WHERE id = @id",
            ("status", request.Status), ("id", mesocycleId));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Ok(new { id = mesocycleId, reviewStatus = request.Status });
    }

    /// <summary>
    /// Vacía los bloques FUTUROS que siguen siendo borradores intactos para poder regenerarlos con el generador.
    /// Nunca toca bloques revisados, empezados, con registros del paciente ni con sesiones editadas a mano
    /// (se detectan por updated_at/estado/instrucciones). Con dryRun solo informa de qué se vaciaría.
    /// </summary>
    [HttpPost("plans/{planId:int}/mesocycles/clear-future-drafts")]
    public async Task<IActionResult> ClearFutureDraftMesocycles(int planId, [FromQuery] bool dryRun, CancellationToken cancellationToken)
    {
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var clearable = new List<object>();
            var kept = new List<object>();
            var clearIds = new List<int>();
            await using (var list = CreateCommand("""
                SELECT m.id, m.name, m.start_date, COALESCE(m.end_date, m.start_date), m.review_status,
                       (SELECT COUNT(*) FROM training_sessions s WHERE s.plan_id = m.plan_id AND s.scheduled_date BETWEEN m.start_date AND COALESCE(m.end_date, m.start_date)) AS sessions,
                       (SELECT COUNT(*) FROM training_sessions s
                          WHERE s.plan_id = m.plan_id AND s.scheduled_date BETWEEN m.start_date AND COALESCE(m.end_date, m.start_date)
                            AND (s.status <> 'planned'
                                 OR s.updated_at > s.created_at + INTERVAL '2 seconds'
                                 OR COALESCE(s.instructions, '') NOT LIKE 'Borrador generado por el motor%'
                                 OR EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id))) AS protected_sessions
                FROM training_mesocycles m
                WHERE m.plan_id = @planId AND m.start_date > CURRENT_DATE
                ORDER BY m.sequence_number
                FOR UPDATE OF m
                """, transaction, ("planId", planId)))
            await using (var reader = await list.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken))
                {
                    var id = reader.GetInt32(0);
                    var isDraft = reader.GetString(4) == "draft";
                    var sessions = reader.GetInt64(5);
                    var protectedSessions = reader.GetInt64(6);
                    var item = new { id, name = reader.GetString(1), startDate = reader.GetFieldValue<DateOnly>(2), sessions };
                    if (isDraft && protectedSessions == 0) { clearable.Add(item); clearIds.Add(id); }
                    else kept.Add(new { item.id, item.name, item.startDate, reason = !isDraft ? "revisado" : "sesiones editadas, registradas o no planificadas" });
                }

            if (!dryRun && clearIds.Count > 0)
            {
                await using var clear = CreateCommand("""
                    DELETE FROM training_sessions s USING training_mesocycles m
                    WHERE m.id = ANY(@ids) AND s.plan_id = m.plan_id
                      AND s.scheduled_date BETWEEN m.start_date AND COALESCE(m.end_date, m.start_date);
                    DELETE FROM training_microcycles WHERE mesocycle_id = ANY(@ids);
                    """, transaction, ("ids", clearIds.ToArray()));
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return Ok(new { dryRun, cleared = clearable, kept });
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Todo lo que espera al profesional en entrenamientos, en todos sus clientes.</summary>
    [HttpGet("pending-reviews")]
    public async Task<IActionResult> GetPendingReviews(CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        var superAdmin = User.IsInRole("superadmin");
        var clinicAdmin = User.IsInRole("clinic_admin");
        const string access = """
            (@superAdmin OR (
                cl.tenant_id = (SELECT u.tenant_id FROM users u WHERE u.id = @userId)
                AND (@clinicAdmin OR cl.user_id = @userId
                     OR EXISTS (SELECT 1 FROM client_nutritionist_assignments a WHERE a.client_id = cl.id AND a.nutritionist_id = @userId AND a.is_active))))
            """;
        await using var command = Command($"""
            SELECT 'review_request' AS kind, r.id, cl.id, cl.full_name, p.id, p.name, r.created_at, r.note
            FROM training_review_requests r
            JOIN training_plans p ON p.id = r.plan_id
            JOIN clients cl ON cl.id = r.client_id AND cl.archived_at IS NULL
            WHERE r.status = 'pending' AND {access}
            UNION ALL
            SELECT 'adaptation', a.id, cl.id, cl.full_name, p.id, p.name, a.created_at, a.action
            FROM training_adaptation_proposals a
            JOIN training_plans p ON p.id = a.plan_id
            JOIN clients cl ON cl.id = a.client_id AND cl.archived_at IS NULL
            WHERE a.status = 'pending' AND {access}
            UNION ALL
            SELECT 'draft_block', m.id, cl.id, cl.full_name, p.id, p.name, m.created_at, m.name
            FROM training_mesocycles m
            JOIN training_plans p ON p.id = m.plan_id AND p.status IN ('active','draft','paused')
            JOIN training_profiles f ON f.id = p.profile_id
            JOIN clients cl ON cl.id = f.client_id AND cl.archived_at IS NULL
            WHERE m.review_status = 'draft' AND m.start_date <= CURRENT_DATE + 14 AND COALESCE(m.end_date, m.start_date) >= CURRENT_DATE AND {access}
            ORDER BY 7 DESC
            LIMIT 200
            """, ("userId", userId.Value), ("superAdmin", superAdmin), ("clinicAdmin", clinicAdmin));
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new
            {
                kind = reader.GetString(0), id = reader.GetInt64(1), clientId = reader.GetInt32(2),
                clientName = reader.IsDBNull(3) ? "Cliente" : reader.GetString(3), planId = reader.GetInt32(4),
                planName = reader.GetString(5), createdAt = reader.GetDateTime(6), detail = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        return Ok(items);
    }

    [HttpGet("plans/{planId:int}/mesocycles")]
    public async Task<IActionResult> GetMesocycles(int planId, CancellationToken cancellationToken)
    {
        if (!await CanAccessPlan(planId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT id, plan_id, name, sequence_number, start_date, end_date, objective, notes, review_status
            FROM training_mesocycles WHERE plan_id = @planId
            ORDER BY sequence_number, start_date, id
            """, ("planId", planId));
        var items = new List<TrainingMesocycleDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new TrainingMesocycleDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetFieldValue<DateOnly>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8)));
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
}
