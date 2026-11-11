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
/// Generación y vista previa de bloques, reorganización del split y borrador de macrociclo.
/// </summary>
public sealed partial class TrainingPeriodizationController
{
    [HttpPost("generate-preview")]
    public async Task<IActionResult> GeneratePreview([FromBody] TrainingProgramGenerationRequest request, [FromQuery] int? planId, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        // With a plan, the preview uses the same history as the saved result (previous blocks, performance).
        (TrainingProgramGenerationRequest Request, PreviousBlock? Previous) historyForPreview = (request, null);
        if (planId.HasValue)
        {
            if (!await CanManagePlan(planId.Value, cancellationToken)) return NotFound();
            historyForPreview = await WithHistoryAsync(planId.Value, request, null, cancellationToken);
            request = historyForPreview.Request;
        }
        try
        {
            var candidates = await _exerciseCatalog.LoadGeneratorCandidatesAsync(userId.Value, cancellationToken);
            var result = TrainingProgramGenerator.Generate(request, candidates);
            if (!result.Succeeded) return UnprocessableEntity(new { errors = result.Errors });
            return Ok(historyForPreview.Previous is null ? result.Prescription : result.Prescription! with
            {
                History = BuildHistory(result.Prescription!, historyForPreview.Request, historyForPreview.Previous)
            });
        }
        catch (Exception)
        {
            return StatusCode(503, new { errors = new[] { "No se pudo preparar el catálogo de ejercicios para generar la rutina." } });
        }
    }

    [HttpGet("plans/{planId:int}/split")]
    public async Task<IActionResult> GetPlanSplit(int planId, CancellationToken cancellationToken)
    {
        if (!await CanAccessPlan(planId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT split_type, training_days FROM training_plan_split_preferences WHERE plan_id = @planId
            """, ("planId", planId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Ok(new { splitType = 0, trainingDays = new[] { 1, 3, 5 } });
        return Ok(new
        {
            splitType = reader.GetInt16(0),
            trainingDays = reader.GetFieldValue<short[]>(1).Select(day => (int)day).ToArray()
        });
    }

    [HttpPost("plans/{planId:int}/reorganize-split")]
    public async Task<IActionResult> ReorganizePlanSplit(
        int planId,
        [FromBody] ReorganizeTrainingSplitRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        var validation = ValidateTrainingSplit(request.SplitType, request.TrainingDays);
        if (validation is not null) return BadRequest(new { message = validation });
        if (request.Moves is null || request.Moves.Count > 312 ||
            request.Moves.Select(move => move.SessionId).Distinct().Count() != request.Moves.Count ||
            request.Moves.Select(move => move.ScheduledDate).Distinct().Count() != request.Moves.Count)
            return BadRequest(new { message = "La propuesta contiene sesiones o fechas duplicadas, o supera 312 sesiones." });
        if (request.Moves.Any(move => !request.TrainingDays.Contains((int)move.ScheduledDate.DayOfWeek)))
            return BadRequest(new { message = "Todas las fechas propuestas deben coincidir con los días seleccionados." });

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            DateOnly planStart;
            DateOnly? planEnd;
            await using (var planCommand = connection.CreateCommand())
            {
                planCommand.Transaction = transaction;
                planCommand.CommandText = "SELECT start_date, end_date FROM training_plans WHERE id = @planId FOR UPDATE";
                planCommand.Parameters.AddWithValue("planId", planId);
                await using var reader = await planCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return NotFound();
                }
                planStart = reader.GetFieldValue<DateOnly>(0);
                planEnd = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1);
            }

            if (request.Moves.Any(move => move.ScheduledDate < planStart || (planEnd.HasValue && move.ScheduledDate > planEnd.Value)))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new { message = "La reorganización dejaría sesiones fuera del periodo del plan." });
            }

            if (request.Moves.Count > 0)
            {
                await using var currentSplit = connection.CreateCommand();
                currentSplit.Transaction = transaction;
                currentSplit.CommandText = "SELECT split_type, cardinality(training_days) FROM training_plan_split_preferences WHERE plan_id = @planId";
                currentSplit.Parameters.AddWithValue("planId", planId);
                var currentSplitType = 0;
                var currentDayCount = 3;
                await using (var splitReader = await currentSplit.ExecuteReaderAsync(cancellationToken))
                {
                    if (await splitReader.ReadAsync(cancellationToken))
                    {
                        currentSplitType = splitReader.GetInt16(0);
                        currentDayCount = splitReader.GetInt32(1);
                    }
                }
                if (request.SplitType != currentSplitType || request.TrainingDays.Length != currentDayCount)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Conflict(new { message = "Esta operación solo recoloca días y conserva el tipo y la frecuencia del split actual. Para cambiar a otro split o cambiar el número de días, genera una nueva propuesta y revísala antes de sustituir las sesiones futuras." });
                }
            }

            if (request.Moves.Count > 0)
            {
                var ids = request.Moves.Select(move => move.SessionId).ToArray();
                var scheduledDates = request.Moves.Select(move => move.ScheduledDate).ToArray();
                var currentDates = new Dictionary<int, DateOnly>();
                await using (var verify = connection.CreateCommand())
                {
                    verify.Transaction = transaction;
                    verify.CommandText = """
                        SELECT s.id, s.scheduled_date FROM training_sessions s
                        WHERE s.plan_id = @planId AND s.id = ANY(@sessionIds)
                          AND s.scheduled_date >= CURRENT_DATE
                          AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id)
                        FOR UPDATE OF s
                        """;
                    verify.Parameters.AddWithValue("planId", planId);
                    verify.Parameters.AddWithValue("sessionIds", ids);
                    await using var reader = await verify.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                        currentDates[reader.GetInt32(0)] = reader.GetFieldValue<DateOnly>(1);
                }
                if (currentDates.Count != ids.Length)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Conflict(new { message = "Una o más sesiones ya no son futuras, no pertenecen al plan o tienen una realización registrada. No se ha cambiado ninguna sesión." });
                }

                foreach (var move in request.Moves)
                {
                    var currentDate = currentDates[move.SessionId];
                    var currentWeek = currentDate.AddDays(-(((int)currentDate.DayOfWeek + 6) % 7));
                    var targetWeek = move.ScheduledDate.AddDays(-(((int)move.ScheduledDate.DayOfWeek + 6) % 7));
                    if (currentWeek != targetWeek)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Conflict(new { message = "La reorganización solo puede cambiar el día dentro de la misma semana; no desplazará sesiones entre semanas." });
                    }
                }

                await using (var collisionCheck = connection.CreateCommand())
                {
                    collisionCheck.Transaction = transaction;
                    collisionCheck.CommandText = """
                        SELECT COUNT(*) FROM training_sessions s
                        WHERE s.plan_id = @planId AND s.scheduled_date = ANY(@scheduledDates)
                          AND NOT (s.id = ANY(@sessionIds))
                        """;
                    collisionCheck.Parameters.AddWithValue("planId", planId);
                    collisionCheck.Parameters.AddWithValue("scheduledDates", scheduledDates);
                    collisionCheck.Parameters.AddWithValue("sessionIds", ids);
                    if (Convert.ToInt32(await collisionCheck.ExecuteScalarAsync(cancellationToken)) > 0)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return Conflict(new { message = "Una de las fechas propuestas ya contiene otra sesión que no forma parte de la reorganización." });
                    }
                }

                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE training_sessions s
                    SET scheduled_date = changes.scheduled_date, updated_at = NOW()
                    FROM unnest(@sessionIds::integer[], @scheduledDates::date[]) AS changes(session_id, scheduled_date)
                    WHERE s.id = changes.session_id AND s.plan_id = @planId
                    """;
                update.Parameters.AddWithValue("sessionIds", ids);
                update.Parameters.AddWithValue("scheduledDates", scheduledDates);
                update.Parameters.AddWithValue("planId", planId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var savePreference = connection.CreateCommand())
            {
                savePreference.Transaction = transaction;
                savePreference.CommandText = """
                    INSERT INTO training_plan_split_preferences(plan_id, split_type, training_days, updated_at)
                    VALUES (@planId, @splitType, @trainingDays, NOW())
                    ON CONFLICT (plan_id) DO UPDATE SET split_type = EXCLUDED.split_type,
                        training_days = EXCLUDED.training_days, updated_at = NOW()
                    """;
                savePreference.Parameters.AddWithValue("planId", planId);
                savePreference.Parameters.AddWithValue("splitType", (short)request.SplitType);
                savePreference.Parameters.AddWithValue("trainingDays", request.TrainingDays.Select(day => (short)day).ToArray());
                await savePreference.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Ok(new { updatedSessions = request.Moves.Count, splitType = request.SplitType, trainingDays = request.TrainingDays });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPost("plans/{planId:int}/replace-future-split")]
    public async Task<IActionResult> ReplaceFutureSplit(
        int planId,
        [FromBody] ReplaceFutureTrainingSplitRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanManagePlan(planId, cancellationToken)) return NotFound();
        var validation = ValidateTrainingSplit(request.SplitType, request.TrainingDays);
        if (validation is not null) return BadRequest(new { message = validation });
        if (request.ReplaceFromDate < DateOnly.FromDateTime(DateTime.UtcNow) ||
            request.ReplaceThroughDate < request.ReplaceFromDate ||
            request.Sessions is null || request.Sessions.Count is < 1 or > 312)
            return BadRequest(new { message = "El periodo de sustitución debe ser futuro y contener entre una y 312 sesiones." });
        if (request.ReplaceFromDate.DayOfWeek != DayOfWeek.Monday ||
            request.ReplaceThroughDate.DayOfWeek != DayOfWeek.Sunday ||
            (request.ReplaceThroughDate.DayNumber - request.ReplaceFromDate.DayNumber + 1) % 7 != 0)
            return BadRequest(new { message = "El periodo de sustitución debe abarcar semanas completas, de lunes a domingo." });
        var replacementWeeks = (request.ReplaceThroughDate.DayNumber - request.ReplaceFromDate.DayNumber + 1) / 7;
        if (replacementWeeks is < 1 or > 52 || request.Sessions.Count != replacementWeeks * request.TrainingDays.Length)
            return BadRequest(new { message = "La propuesta debe contener exactamente una sesión por cada día de entrenamiento y semana del periodo." });
        if (request.Sessions.Select(session => session.ScheduledDate).Distinct().Count() != request.Sessions.Count ||
            request.Sessions.Any(session => session.ScheduledDate < request.ReplaceFromDate ||
                session.ScheduledDate > request.ReplaceThroughDate ||
                !request.TrainingDays.Contains((int)session.ScheduledDate.DayOfWeek)))
            return BadRequest(new { message = "Las sesiones propuestas deben tener fechas únicas, dentro del periodo y en los días seleccionados." });
        if (request.Sessions.GroupBy(session => session.ScheduledDate.AddDays(-(((int)session.ScheduledDate.DayOfWeek + 6) % 7)))
            .Any(week => week.Count() != request.TrainingDays.Length ||
                week.Select(session => (int)session.ScheduledDate.DayOfWeek).Distinct().Count() != request.TrainingDays.Length))
            return BadRequest(new { message = "Cada semana debe incluir exactamente una sesión en cada día seleccionado." });
        if (request.Sessions.Any(session => string.IsNullOrWhiteSpace(session.Title) || session.Title.Trim().Length > 160 ||
            session.DurationMinutes is < 1 or > 1440 || session.IntensityTarget?.Length > 120 ||
            session.Instructions?.Length > 4000 || session.ExercisePrescription?.Length > 6000 ||
            session.SessionType is not ("endurance" or "strength" or "mobility" or "recovery" or "health" or "general" or "other") ||
            (session.ExercisePrescriptionData.HasValue &&
             (session.ExercisePrescriptionData.Value.ValueKind != JsonValueKind.Object ||
              session.ExercisePrescriptionData.Value.GetRawText().Length > 20000))))
            return BadRequest(new { message = "Una o más sesiones contienen campos no válidos o superan los límites permitidos." });

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            DateOnly planStart;
            DateOnly? planEnd;
            await using (var planCommand = connection.CreateCommand())
            {
                planCommand.Transaction = transaction;
                planCommand.CommandText = "SELECT start_date, end_date FROM training_plans WHERE id = @planId FOR UPDATE";
                planCommand.Parameters.AddWithValue("planId", planId);
                await using var reader = await planCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return NotFound();
                }
                planStart = reader.GetFieldValue<DateOnly>(0);
                planEnd = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1);
            }

            if (request.ReplaceFromDate < planStart ||
                (planEnd.HasValue && request.ReplaceThroughDate > planEnd.Value))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new { message = "El periodo de sustitución queda fuera de las fechas del plan." });
            }

            await using (var completedCheck = connection.CreateCommand())
            {
                completedCheck.Transaction = transaction;
                completedCheck.CommandText = """
                    SELECT COUNT(*) FROM training_sessions s
                    WHERE s.plan_id = @planId
                      AND s.scheduled_date BETWEEN @fromDate AND @throughDate
                      AND EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id)
                    """;
                completedCheck.Parameters.AddWithValue("planId", planId);
                completedCheck.Parameters.AddWithValue("fromDate", request.ReplaceFromDate);
                completedCheck.Parameters.AddWithValue("throughDate", request.ReplaceThroughDate);
                if (Convert.ToInt32(await completedCheck.ExecuteScalarAsync(cancellationToken)) > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Conflict(new { message = "El periodo contiene sesiones con realizaciones registradas. Amplía el inicio de la sustitución para dejarlas fuera; no se ha modificado nada." });
                }
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = """
                    DELETE FROM training_sessions
                    WHERE plan_id = @planId
                      AND scheduled_date BETWEEN @fromDate AND @throughDate
                      AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = training_sessions.id)
                    """;
                delete.Parameters.AddWithValue("planId", planId);
                delete.Parameters.AddWithValue("fromDate", request.ReplaceFromDate);
                delete.Parameters.AddWithValue("throughDate", request.ReplaceThroughDate);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var session in request.Sessions)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO training_sessions(
                        plan_id, scheduled_date, title, session_type, duration_minutes,
                        intensity_target, instructions, exercise_prescription, exercise_prescription_data)
                    VALUES (
                        @planId, @date, @title, @type, @duration,
                        @intensity, @instructions, @prescription, @prescriptionData::jsonb)
                    """;
                insert.Parameters.AddWithValue("planId", planId);
                insert.Parameters.AddWithValue("date", session.ScheduledDate);
                insert.Parameters.AddWithValue("title", session.Title.Trim());
                insert.Parameters.AddWithValue("type", session.SessionType);
                insert.Parameters.AddWithValue("duration", (object?)session.DurationMinutes ?? DBNull.Value);
                insert.Parameters.AddWithValue("intensity", (object?)session.IntensityTarget?.Trim() ?? DBNull.Value);
                insert.Parameters.AddWithValue("instructions", (object?)session.Instructions?.Trim() ?? DBNull.Value);
                insert.Parameters.AddWithValue("prescription", (object?)session.ExercisePrescription?.Trim() ?? DBNull.Value);
                insert.Parameters.AddWithValue("prescriptionData", session.ExercisePrescriptionData.HasValue
                    ? session.ExercisePrescriptionData.Value.GetRawText()
                    : DBNull.Value);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var savePreference = connection.CreateCommand())
            {
                savePreference.Transaction = transaction;
                savePreference.CommandText = """
                    INSERT INTO training_plan_split_preferences(plan_id, split_type, training_days, updated_at)
                    VALUES (@planId, @splitType, @trainingDays, NOW())
                    ON CONFLICT (plan_id) DO UPDATE SET split_type = EXCLUDED.split_type,
                        training_days = EXCLUDED.training_days, updated_at = NOW()
                    """;
                savePreference.Parameters.AddWithValue("planId", planId);
                savePreference.Parameters.AddWithValue("splitType", (short)request.SplitType);
                savePreference.Parameters.AddWithValue("trainingDays", request.TrainingDays.Select(day => (short)day).ToArray());
                await savePreference.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Ok(new { sessionsCreated = request.Sessions.Count, splitType = request.SplitType, trainingDays = request.TrainingDays });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string? ValidateTrainingSplit(int splitType, IReadOnlyList<int>? trainingDays)
    {
        if (splitType is < 0 or > 3 || trainingDays is null || trainingDays.Count is < 2 or > 6 ||
            trainingDays.Any(day => day is < 0 or > 6) || trainingDays.Distinct().Count() != trainingDays.Count)
            return "Selecciona una distribución válida y entre dos y seis días distintos.";
        var count = trainingDays.Count;
        var valid = splitType switch
        {
            0 => count is >= 2 and <= 4,
            1 => count is >= 2 and <= 4,
            2 => count is 2 or 4,
            3 => count is 3 or 5 or 6,
            _ => false
        };
        return valid ? null : splitType switch
        {
            1 => "Cuerpo completo admite de 2 a 4 días.",
            2 => "Torso/Pierna admite 2 o 4 días.",
            3 => "Push/Pull/Legs admite 3, 5 o 6 días.",
            _ => "La distribución automática admite entre 2 y 4 días."
        };
    }

    /// <summary>
    /// Devuelve un borrador editable de macrociclo con la forma de <see cref="SaveTrainingMacrocycleRequest"/>.
    /// No persiste nada: el profesional lo revisa y lo guarda con PUT plans/{planId}/macrocycle.
    /// </summary>
    [HttpPost("macrocycle-draft")]
    public IActionResult DraftMacrocycle([FromBody] TrainingMacrocycleDraftRequest request)
    {
        if (request is null) return BadRequest(new { message = "Indica fecha de inicio, duración, objetivo y nivel." });
        var result = TrainingMacrocycleDraftBuilder.Build(request);
        if (!result.Succeeded) return BadRequest(new { message = string.Join(" ", result.Errors) });
        var draft = result.Draft!;
        return Ok(new SaveTrainingMacrocycleRequest(
            draft.Name, draft.PeriodizationModel, draft.StartDate, draft.EndDate, draft.Notes,
            draft.Phases.Select(phase => new SaveTrainingMacrocyclePhaseRequest(
                phase.Name, phase.SequenceNumber, phase.StartDate, phase.EndDate, phase.Objective, phase.PhaseType)).ToList()));
    }

    [HttpPost("volume-preview")]
    public async Task<IActionResult> PreviewManualVolume(
        [FromBody] TrainingManualVolumePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (request?.Sessions is null || request.Sessions.Count > 312)
            return BadRequest(new { message = "Envía una semana con hasta 312 sesiones individuales." });

        try
        {
            var candidates = await _exerciseCatalog.LoadManualVolumeCandidatesAsync(userId.Value, cancellationToken);
            var result = TrainingProgramQualityAnalyzer.AnalyzeManual(candidates, request.Sessions);
            return Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception)
        {
            return StatusCode(503, new { message = "No se pudo calcular el volumen de la rutina manual con el catálogo revisado." });
        }
    }

    /// <summary>
    /// Makes a generation request history-aware without extra input from the professional:
    ///  - starting loads come from the client's last recorded performance;
    ///  - the previous block of the plan (by date) tells which exercises it used, so a new training block
    ///    rotates to equally suitable ones and a deload keeps them;
    ///  - a block that directly follows a non-deload block skips the full "introduction week".
    /// Explicit values sent by the professional (rotation, load references) are respected.
    /// </summary>
    private async Task<(TrainingProgramGenerationRequest Request, PreviousBlock? Previous)> WithHistoryAsync(
        int planId, TrainingProgramGenerationRequest generation, int? sequenceNumber, CancellationToken cancellationToken)
    {
        if (generation.RotationIndex is null && sequenceNumber.HasValue)
            generation = generation with { RotationIndex = Math.Max(0, sequenceNumber.Value - 1) };
        if (generation.LoadReferences is null)
        {
            var clientId = await _context.Database.SqlQueryRaw<int?>(
                """SELECT f.client_id AS "Value" FROM training_plans p JOIN training_profiles f ON f.id = p.profile_id WHERE p.id = {0}""", planId)
                .SingleOrDefaultAsync(cancellationToken);
            if (clientId.HasValue)
                generation = generation with
                {
                    LoadReferences = await new TrainingExerciseProgressionService(_context)
                        .GetLoadReferencesAsync(clientId.Value, cancellationToken)
                };
        }

        var previous = await GetPreviousBlockAsync(planId, generation.StartDate, cancellationToken);
        if (previous is null) return (generation, null);
        generation = generation with
        {
            PreviousBlockExerciseIds = generation.PreviousBlockExerciseIds is { Count: > 0 } ? generation.PreviousBlockExerciseIds : previous.ExerciseIds,
            ContinuesPreviousBlock = !generation.IsDeload && !previous.WasDeload && previous.GapDays <= 7
        };
        return (generation, previous);
    }

    private sealed record PreviousBlock(IReadOnlyList<string> ExerciseIds, bool WasDeload, int GapDays,
        IReadOnlyDictionary<string, string> Names);

    private static TrainingProgramHistory BuildHistory(TrainingProgramPrescription prescription,
        TrainingProgramGenerationRequest request, PreviousBlock previous)
    {
        var used = previous.ExerciseIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = prescription.Sessions.SelectMany(session => session.Exercises)
            .GroupBy(exercise => exercise.ExerciseId, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        var items = current.Select(exercise => new TrainingHistoryExercise(
            exercise.ExerciseId, exercise.ExerciseNameSnapshot, used.Contains(exercise.ExerciseId) ? "kept" : "new")).ToList();
        var now = current.Select(exercise => exercise.ExerciseId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        items.AddRange(previous.ExerciseIds.Where(id => !now.Contains(id)).Select(id =>
            new TrainingHistoryExercise(id, previous.Names.TryGetValue(id, out var name) ? name : id, "dropped")));
        return new TrainingProgramHistory(previous.ExerciseIds.Count, request.ContinuesPreviousBlock, items);
    }

    /// <summary>Exercises of the latest mesocycle that ends before <paramref name="start"/> (structured prescriptions only).</summary>
    private async Task<PreviousBlock?> GetPreviousBlockAsync(int planId, DateOnly start, CancellationToken cancellationToken)
    {
        DateOnly blockStart, blockEnd;
        await using (var meso = Command("""
            SELECT start_date, COALESCE(end_date, start_date) FROM training_mesocycles
            WHERE plan_id = @planId AND COALESCE(end_date, start_date) < @start
            ORDER BY COALESCE(end_date, start_date) DESC, sequence_number DESC LIMIT 1
            """, ("planId", planId), ("start", start)))
        await using (var reader = await meso.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            blockStart = reader.GetFieldValue<DateOnly>(0);
            blockEnd = reader.GetFieldValue<DateOnly>(1);
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var wasDeload = false;
        await using (var sessions = Command("""
            SELECT exercise_prescription_data::text FROM training_sessions
            WHERE plan_id = @planId AND scheduled_date BETWEEN @blockStart AND @blockEnd
              AND exercise_prescription_data IS NOT NULL
            """, ("planId", planId), ("blockStart", blockStart), ("blockEnd", blockEnd)))
        await using (var reader = await sessions.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                try
                {
                    using var document = JsonDocument.Parse(reader.GetString(0));
                    if (!document.RootElement.TryGetProperty("exercises", out var exercises) || exercises.ValueKind != JsonValueKind.Array) continue;
                    foreach (var exercise in exercises.EnumerateArray())
                    {
                        if (exercise.TryGetProperty("exerciseId", out var id) && id.ValueKind == JsonValueKind.String)
                        {
                            ids.Add(id.GetString()!);
                            if (exercise.TryGetProperty("exerciseNameSnapshot", out var nm) && nm.ValueKind == JsonValueKind.String) names[id.GetString()!] = nm.GetString()!;
                        }
                        if (exercise.TryGetProperty("selectionRationale", out var why) && why.ValueKind == JsonValueKind.String &&
                            why.GetString()!.Contains("Descarga activa", StringComparison.Ordinal)) wasDeload = true;
                    }
                }
                catch (JsonException) { /* a corrupt prescription simply gives no history */ }
            }
        }
        return ids.Count == 0 ? null : new PreviousBlock(ids.ToArray(), wasDeload, start.DayNumber - blockEnd.DayNumber - 1, names);
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
        var history = await WithHistoryAsync(planId, request.Generation, request.SequenceNumber, cancellationToken);
        var generation = history.Request;
        var generated = TrainingProgramGenerator.Generate(generation, candidates);
        if (!generated.Succeeded)
            return UnprocessableEntity(new { errors = generated.Errors });
        var prescription = generated.Prescription!;
        var mesocycleStart = prescription.StartDate;
        var mesocycleEnd = prescription.StartDate.AddDays(prescription.DurationWeeks * 7 - 1);
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

            int mesocycleId;
            if (request.ExistingMesocycleId.HasValue)
            {
                await using var targetCommand = connection.CreateCommand();
                targetCommand.Transaction = transaction;
                targetCommand.CommandText = """
                    SELECT m.id
                    FROM training_mesocycles m
                    WHERE m.id = @mesocycleId AND m.plan_id = @planId
                      AND m.start_date <= @startDate AND COALESCE(m.end_date, 'infinity'::date) >= @endDate
                      AND NOT EXISTS (SELECT 1 FROM training_microcycles mc WHERE mc.mesocycle_id = m.id)
                      AND NOT EXISTS (SELECT 1 FROM training_sessions s WHERE s.plan_id = m.plan_id AND s.scheduled_date BETWEEN @startDate AND @endDate)
                    FOR UPDATE OF m
                    """;
                targetCommand.Parameters.AddWithValue("mesocycleId", request.ExistingMesocycleId.Value);
                targetCommand.Parameters.AddWithValue("planId", planId);
                targetCommand.Parameters.AddWithValue("startDate", mesocycleStart);
                targetCommand.Parameters.AddWithValue("endDate", mesocycleEnd);
                var target = await targetCommand.ExecuteScalarAsync(cancellationToken);
                if (target is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Conflict(new { message = "El mesociclo seleccionado debe cubrir todas las semanas generadas y no contener microciclos ni sesiones. Elige un bloque vacío o crea uno nuevo." });
                }
                mesocycleId = Convert.ToInt32(target);
            }
            else
            {
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
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO training_mesocycles(plan_id, name, sequence_number, start_date, end_date, objective, notes, review_status)
                    VALUES (@planId, @name, @sequence, @startDate, @endDate, @objective, @notes, 'draft')
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

            // Todo bloque generado queda como borrador hasta que el profesional lo marque como revisado.
            await using (var draftCommand = connection.CreateCommand())
            {
                draftCommand.Transaction = transaction;
                draftCommand.CommandText = "UPDATE training_mesocycles SET review_status = 'draft', updated_at = NOW() WHERE id = @id";
                draftCommand.Parameters.AddWithValue("id", mesocycleId);
                await draftCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            for (var week = 1; week <= prescription.DurationWeeks; week++)
            {
                var weekStart = prescription.StartDate.AddDays((week - 1) * 7);
                var weekEnd = prescription.StartDate.AddDays(week * 7 - 1);
                var microcycleEnd = weekEnd > mesocycleEnd ? mesocycleEnd : weekEnd;
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
                        $"{exercise.Order}. {exercise.ExerciseNameSnapshot} — {exercise.Sets} × {exercise.MinRepetitions}-{exercise.MaxRepetitions} reps; RIR {(exercise.TargetRepetitionsInReserve?.ToString() ?? "—")}; descanso {exercise.RestSeconds}s;{(exercise.TargetLoadKg is null ? "" : $" carga {exercise.TargetLoadKg} kg;")}{(exercise.WarmupSets is null ? "" : $" aproximación {exercise.WarmupSets} serie(s) muy fácil(es);")} progresión {exercise.ProgressionRule}. [ID: {exercise.ExerciseId}; catálogo: {exercise.CatalogVersion}]"));
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO training_sessions(plan_id, scheduled_date, title, session_type, duration_minutes,
                            intensity_target, instructions, exercise_prescription, exercise_prescription_data)
                        VALUES (@planId, @date, @title, 'strength', @duration, @intensity, @instructions, @prescription, @prescriptionData::jsonb)
                        """;
                    command.Parameters.AddWithValue("planId", planId);
                    command.Parameters.AddWithValue("date", sessionDate);
                    command.Parameters.AddWithValue("title", session.Title);
                    command.Parameters.AddWithValue("duration", session.EstimatedDurationMinutes);
                    command.Parameters.AddWithValue("intensity", $"{prescription.Goal}; RIR según prescripción");
                    command.Parameters.AddWithValue("instructions", $"Borrador generado por el motor {prescription.EngineVersion}. Revisar antes de asignar al paciente.");
                    command.Parameters.AddWithValue("prescription", prescriptionText);
                    var structuredPrescription = JsonSerializer.Serialize(new
                    {
                        engineVersion = prescription.EngineVersion,
                        goal = prescription.Goal,
                        weekNumber = session.WeekNumber,
                        day = session.Day.ToString(),
                        estimatedDurationMinutes = session.EstimatedDurationMinutes,
                        exercises = session.Exercises
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    command.Parameters.AddWithValue("prescriptionData", structuredPrescription);
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
                requiresProfessionalReview = true,
                addedToExistingMesocycle = request.ExistingMesocycleId.HasValue,
                history = new
                {
                    previousBlockExercises = history.Previous?.ExerciseIds.Count ?? 0,
                    continuesPreviousBlock = generation.ContinuesPreviousBlock,
                    loadReferences = generation.LoadReferences?.Count ?? 0
                }
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
}
