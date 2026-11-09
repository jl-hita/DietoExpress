using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Anguloso.Server.Middleware;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Controllers;

/// <summary>Acceso del paciente exclusivamente a sus propias sesiones deportivas y registros.</summary>
[ApiController]
[Route("api/portal/training")]
[Authorize(Roles = "patient")]
public sealed class TrainingPortalController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly Anguloso.Server.Logica.ExerciseCatalogService _catalog;
    private readonly Anguloso.Server.Logica.ExerciseImageService _images;
    private readonly Anguloso.Server.Logica.TrainingAutoAdaptationApplier _autoAdaptation;

    public TrainingPortalController(
        angulosodbContext context,
        Anguloso.Server.Logica.ExerciseCatalogService catalog,
        Anguloso.Server.Logica.ExerciseImageService images,
        Anguloso.Server.Logica.TrainingAutoAdaptationApplier autoAdaptation)
    {
        _context = context;
        _catalog = catalog;
        _images = images;
        _autoAdaptation = autoAdaptation;
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("clientId"), out var clientId)) return Unauthorized();
        if (!int.TryParse(User.FindFirstValue("portalTokenVersion"), out var version)) return Unauthorized();
        var clientValid = await _context.clients.AsNoTracking().AnyAsync(c => c.id == clientId && c.archived_at == null && c.portal_token_version == version, cancellationToken);
        if (!clientValid) return Unauthorized();

        await using var profileCommand = Command("SELECT enabled FROM training_profiles WHERE client_id = @clientId", ("clientId", clientId));
        var profileValue = await profileCommand.ExecuteScalarAsync(cancellationToken);
        if (profileValue is null || profileValue is DBNull || !Convert.ToBoolean(profileValue))
            return Ok(new { enabled = false, sessions = Array.Empty<object>(), exerciseProgressionRecommendations = Array.Empty<object>() });

        await using var command = Command("""
            SELECT s.id, s.plan_id, s.scheduled_date, s.title, s.session_type, s.duration_minutes, s.intensity_target, s.instructions, s.status,
                   r.completed_at, r.actual_duration_minutes, r.session_rpe, r.notes, s.exercise_prescription,
                   r.outcome, r.non_completion_reason, r.recovery_rating, r.pain_signal, r.progression_target_achieved,
                   s.exercise_prescription_data::text, r.exercise_feedback::text
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            LEFT JOIN training_session_records r ON r.session_id = s.id AND r.client_id = f.client_id
            WHERE f.client_id = @clientId AND f.enabled = TRUE AND p.status IN ('active','draft','paused')
            ORDER BY s.scheduled_date, s.id
            """, ("clientId", clientId));
        var sessions = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(new
            {
                id = reader.GetInt32(0), planId = reader.GetInt32(1), scheduledDate = reader.GetFieldValue<DateOnly>(2),
                title = reader.GetString(3), sessionType = reader.GetString(4),
                durationMinutes = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                intensityTarget = reader.IsDBNull(6) ? null : reader.GetString(6),
                instructions = reader.IsDBNull(7) ? null : reader.GetString(7), status = reader.GetString(8),
                completedAt = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9),
                actualDurationMinutes = reader.IsDBNull(10) ? (int?)null : reader.GetInt32(10),
                sessionRpe = reader.IsDBNull(11) ? (decimal?)null : reader.GetDecimal(11),
                recordNotes = reader.IsDBNull(12) ? null : reader.GetString(12),
                exercisePrescription = reader.IsDBNull(13) ? null : reader.GetString(13),
                outcome = reader.IsDBNull(14) ? null : reader.GetString(14),
                nonCompletionReason = reader.IsDBNull(15) ? null : reader.GetString(15),
                recoveryRating = reader.IsDBNull(16) ? null : reader.GetString(16),
                painSignal = reader.IsDBNull(17) ? null : reader.GetString(17),
                progressionTargetAchieved = reader.IsDBNull(18) ? (bool?)null : reader.GetBoolean(18),
                exercisePrescriptionData = reader.IsDBNull(19) ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(19)),
                exerciseFeedback = reader.IsDBNull(20) ? Array.Empty<PatientTrainingExerciseFeedback>() :
                    JsonSerializer.Deserialize<PatientTrainingExerciseFeedback[]>(reader.GetString(20), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? Array.Empty<PatientTrainingExerciseFeedback>()
            });
        }
        await reader.DisposeAsync();

        var feedback = new List<Anguloso.Server.Logica.TrainingSessionFeedback>();
        await using (var feedbackCommand = Command("""
            SELECT r.outcome, r.non_completion_reason, r.recovery_rating, r.pain_signal, r.session_rpe, r.progression_target_achieved, r.exercise_feedback::text
            FROM training_session_records r
            JOIN training_sessions s ON s.id = r.session_id
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE f.client_id = @clientId AND f.enabled = TRUE AND p.status = 'active' AND r.outcome IS NOT NULL
            ORDER BY r.updated_at DESC
            LIMIT 3
            """, ("clientId", clientId)))
        await using (var feedbackReader = await feedbackCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await feedbackReader.ReadAsync(cancellationToken))
            {
                feedback.Add(new Anguloso.Server.Logica.TrainingSessionFeedback(
                    ParseOutcome(feedbackReader.GetString(0)),
                    feedbackReader.IsDBNull(1) ? Anguloso.Server.Logica.TrainingNonCompletionReason.None : ParseReason(feedbackReader.GetString(1)),
                    feedbackReader.IsDBNull(2) ? Anguloso.Server.Logica.TrainingRecoveryRating.Unknown : ParseRecovery(feedbackReader.GetString(2)),
                    HasPainSkippedSet(feedbackReader.IsDBNull(6) ? null : feedbackReader.GetString(6))
                        ? Anguloso.Server.Logica.TrainingPainSignal.NewOrWorsening
                        : feedbackReader.IsDBNull(3) ? Anguloso.Server.Logica.TrainingPainSignal.None : ParsePain(feedbackReader.GetString(3)),
                    feedbackReader.IsDBNull(4) ? null : feedbackReader.GetDecimal(4),
                    feedbackReader.IsDBNull(5) ? null : feedbackReader.GetBoolean(5),
                    CalculateSetCompletionRatio(feedbackReader.IsDBNull(6) ? null : feedbackReader.GetString(6))));
            }
        }

        feedback.Reverse(); // Policy contract is oldest-to-newest.
        var adaptation = feedback.Count == 0
            ? null
            : Anguloso.Server.Logica.TrainingAdaptationPolicy.Evaluate(feedback);

        // Query only after closing the session reader; the progression service reads the same EF connection.
        var exerciseProgressionRecommendations =
            await new Anguloso.Server.Logica.TrainingExerciseProgressionService(_context)
                .GetRecommendationsAsync(clientId, cancellationToken);
        // Session-level adaptation handles recovery/context; exercise progression is
        // evaluated independently from actual working-set data and the exercise prescription.
        return Ok(new
        {
            enabled = true,
            sessions,
            adaptationRecommendation = adaptation is null ? null : new
            {
                action = ToApiAction(adaptation.Action),
                explanation = adaptation.Explanation
            },
            exerciseProgressionRecommendations
        });
    }

    // ---- Exercise images (served from our own storage; the app never contacts third parties) ----

    /// <summary>Which picture slots exist for each exercise prescribed in one of the patient's sessions.</summary>
    [HttpGet("sessions/{sessionId:int}/exercise-images")]
    public async Task<IActionResult> GetSessionExerciseImages(int sessionId, CancellationToken cancellationToken)
    {
        var clientId = await GetValidatedClientIdAsync(cancellationToken);
        if (clientId is null) return Unauthorized();
        var session = await LoadOwnSessionAsync(clientId.Value, sessionId, cancellationToken);
        if (session is null) return NotFound();
        var result = new List<object>();
        foreach (var exercise in ReadPrescribedExercises(session.Value.Json))
            result.Add(new { exerciseId = exercise.Id, slots = await _images.GetAvailableSlotsAsync(exercise.Id, cancellationToken) });
        return Ok(new { attribution = "Exercise data by RepDB (repdb.co)", exercises = result });
    }

    /// <summary>One picture. Only exercises prescribed to this patient can be requested; there is no listing or bulk endpoint.</summary>
    [HttpGet("exercise-images/{exerciseId}/{slot:int}")]
    public async Task<IActionResult> GetExerciseImage(string exerciseId, int slot, CancellationToken cancellationToken)
    {
        var clientId = await GetValidatedClientIdAsync(cancellationToken);
        if (clientId is null) return Unauthorized();
        await using var check = Command("""
            SELECT EXISTS (
                SELECT 1 FROM training_sessions s
                JOIN training_plans p ON p.id = s.plan_id
                JOIN training_profiles f ON f.id = p.profile_id
                WHERE f.client_id = @clientId AND s.exercise_prescription_data IS NOT NULL
                  AND s.exercise_prescription_data @> jsonb_build_object('exercises',
                        jsonb_build_array(jsonb_build_object('exerciseId', @exerciseId::text))))
            """, ("clientId", clientId.Value), ("exerciseId", exerciseId));
        if (!(bool)(await check.ExecuteScalarAsync(cancellationToken) ?? false)) return NotFound();
        var image = await _images.GetImageAsync(exerciseId, slot, cancellationToken);
        if (image is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(image.Data, image.ContentType);
    }

    // ---- Exercise swap ----

    /// <summary>
    /// Alternatives for one prescribed exercise of an upcoming, still unrecorded session. Same movement
    /// pattern first; otherwise exercises that train the same primary muscles; otherwise an explicit
    /// message that nothing suitable exists (never an unreviewed suggestion).
    /// </summary>
    [HttpGet("sessions/{sessionId:int}/exercises/{exerciseId}/alternatives")]
    public async Task<IActionResult> GetExerciseAlternatives(
        int sessionId, string exerciseId, [FromQuery] string? equipment, CancellationToken cancellationToken)
    {
        var clientId = await GetValidatedClientIdAsync(cancellationToken);
        if (clientId is null) return Unauthorized();
        var context = await LoadSwapContextAsync(clientId.Value, sessionId, exerciseId, cancellationToken);
        if (context.Error is not null) return context.Error;
        var equipmentList = (equipment ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(20).ToArray();
        var result = Anguloso.Server.Logica.TrainingExerciseAlternativeFinder.Find(
            exerciseId, context.PrimaryMuscles, context.Catalog, context.SessionExerciseIds,
            context.Goal, context.Experience, equipmentList, 8);
        return Ok(new { exerciseId, alternatives = result.Alternatives, message = result.Message });
    }

    /// <summary>
    /// Replaces the exercise in THIS session only. The replacement must be one of the finder's valid
    /// alternatives; sets, reps, RIR and rest are kept and the origin is recorded in the prescription.
    /// </summary>
    [HttpPost("sessions/{sessionId:int}/exercises/{exerciseId}/swap")]
    public async Task<IActionResult> SwapExercise(
        int sessionId, string exerciseId, [FromBody] PatientTrainingSwapRequest request, CancellationToken cancellationToken)
    {
        var clientId = await GetValidatedClientIdAsync(cancellationToken);
        if (clientId is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request?.ReplacementExerciseId))
            return BadRequest(new { message = "Indica el ejercicio de sustitución." });
        var context = await LoadSwapContextAsync(clientId.Value, sessionId, exerciseId, cancellationToken);
        if (context.Error is not null) return context.Error;

        var alternatives = Anguloso.Server.Logica.TrainingExerciseAlternativeFinder.Find(
            exerciseId, context.PrimaryMuscles, context.Catalog, context.SessionExerciseIds,
            context.Goal, context.Experience, null, 10);
        var replacement = context.Catalog.FirstOrDefault(candidate =>
            string.Equals(candidate.ExerciseId, request.ReplacementExerciseId, StringComparison.OrdinalIgnoreCase));
        if (replacement is null || !alternatives.Alternatives.Any(item =>
                string.Equals(item.ExerciseId, replacement.ExerciseId, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { message = "Ese ejercicio no es una alternativa válida para esta sesión.", alternatives = alternatives.Alternatives });
        if (!Anguloso.Server.Logica.TrainingAdaptationPrescriptionAdjuster.TrySwapExercise(
                context.Json, exerciseId, replacement, out var updatedJson, out var legacyText))
            return Conflict(new { message = "No se pudo aplicar el intercambio sobre la prescripción de esta sesión." });

        var note = $"Intercambio solicitado por el paciente: {context.ExerciseName} → {replacement.Name}.";
        await using var update = Command("""
            UPDATE training_sessions
            SET exercise_prescription_data = @json::jsonb, exercise_prescription = @text,
                instructions = CASE WHEN COALESCE(instructions, '') = '' THEN @note ELSE instructions || E'\n' || @note END,
                updated_at = NOW()
            WHERE id = @sessionId AND scheduled_date >= CURRENT_DATE AND status = 'planned'
              AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = training_sessions.id)
            """, ("json", updatedJson), ("text", legacyText), ("note", note), ("sessionId", sessionId));
        if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
            return Conflict(new { message = "La sesión ya no admite cambios (registrada, pasada o modificada)." });
        return Ok(new { exerciseId = replacement.ExerciseId, exercisePrescriptionData = JsonSerializer.Deserialize<JsonElement>(updatedJson) });
    }

    private async Task<int?> GetValidatedClientIdAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("clientId"), out var clientId)) return null;
        if (!int.TryParse(User.FindFirstValue("portalTokenVersion"), out var version)) return null;
        var valid = await _context.clients.AsNoTracking().AnyAsync(
            c => c.id == clientId && c.archived_at == null && c.portal_token_version == version, cancellationToken);
        return valid ? clientId : null;
    }

    private async Task<(string Json, string Goal, string Experience)?> LoadOwnSessionAsync(
        int clientId, int sessionId, CancellationToken cancellationToken)
    {
        await using var command = Command("""
            SELECT s.exercise_prescription_data::text, f.primary_goal, f.experience_level
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE s.id = @sessionId AND f.client_id = @clientId AND f.enabled = TRUE
              AND s.exercise_prescription_data IS NOT NULL
            """, ("sessionId", sessionId), ("clientId", clientId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static IReadOnlyList<(string Id, string Name, string[] Primary)> ReadPrescribedExercises(string json)
    {
        var result = new List<(string, string, string[])>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("exercises", out var exercises) || exercises.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var exercise in exercises.EnumerateArray())
            {
                if (!exercise.TryGetProperty("exerciseId", out var id) || id.ValueKind != JsonValueKind.String) continue;
                var name = exercise.TryGetProperty("exerciseNameSnapshot", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : id.GetString()!;
                var primary = exercise.TryGetProperty("primaryMuscleGroups", out var p) && p.ValueKind == JsonValueKind.Array
                    ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
                    : [];
                result.Add((id.GetString()!, name, primary));
            }
        }
        catch (JsonException) { }
        return result;
    }

    private sealed record SwapContext(
        IActionResult? Error, string Json, string ExerciseName,
        IReadOnlyList<Anguloso.Server.Logica.TrainingMuscleGroup> PrimaryMuscles,
        IReadOnlyCollection<Anguloso.Server.Logica.TrainingExerciseCandidate> Catalog,
        IReadOnlySet<string> SessionExerciseIds,
        Anguloso.Server.Logica.TrainingGoal Goal, Anguloso.Server.Logica.TrainingExperience Experience);

    private async Task<SwapContext> LoadSwapContextAsync(int clientId, int sessionId, string exerciseId, CancellationToken cancellationToken)
    {
        SwapContext Fail(IActionResult error) => new(error, string.Empty, string.Empty, [], [], new HashSet<string>(), default, default);
        var session = await LoadOwnSessionAsync(clientId, sessionId, cancellationToken);
        if (session is null) return Fail(NotFound());

        // Only an upcoming session without a record can change; completed history is never rewritten.
        await using (var state = Command("""
            SELECT (s.scheduled_date >= CURRENT_DATE AND s.status = 'planned'
                    AND NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id))
            FROM training_sessions s WHERE s.id = @sessionId
            """, ("sessionId", sessionId)))
            if (!(bool)(await state.ExecuteScalarAsync(cancellationToken) ?? false))
                return Fail(Conflict(new { message = "Solo se pueden intercambiar ejercicios de sesiones próximas que aún no se han registrado." }));

        var prescribed = ReadPrescribedExercises(session.Value.Json);
        var current = prescribed.FirstOrDefault(item => string.Equals(item.Id, exerciseId, StringComparison.OrdinalIgnoreCase));
        if (current.Id is null) return Fail(NotFound());

        var goal = Anguloso.Server.Logica.TrainingGoal.GeneralFitness;
        using (var document = JsonDocument.Parse(session.Value.Json))
            if (document.RootElement.TryGetProperty("goal", out var goalElement) && goalElement.ValueKind == JsonValueKind.String)
                Enum.TryParse(goalElement.GetString(), true, out goal);
        if (!Enum.TryParse<Anguloso.Server.Logica.TrainingExperience>(session.Value.Experience, true, out var experience))
            experience = Anguloso.Server.Logica.TrainingExperience.Beginner;

        // Owner id 0: only globally approved exercises (no professional's pending drafts) reach a patient.
        var catalog = await _catalog.LoadGeneratorCandidatesAsync(0, cancellationToken);
        var primary = current.Primary
            .Select(value => Enum.TryParse<Anguloso.Server.Logica.TrainingMuscleGroup>(value, true, out var muscle) ? (Anguloso.Server.Logica.TrainingMuscleGroup?)muscle : null)
            .Where(muscle => muscle.HasValue).Select(muscle => muscle!.Value).ToArray();
        return new(null, session.Value.Json, current.Name, primary, catalog,
            prescribed.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase), goal, experience);
    }

    [HttpPost("sessions/{sessionId:int}/record")]
    [Idempotent]
    public async Task<IActionResult> RecordSession(int sessionId, [FromBody] PatientTrainingRecordRequest request, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("clientId"), out var clientId)) return Unauthorized();
        if (!int.TryParse(User.FindFirstValue("portalTokenVersion"), out var version)) return Unauthorized();
        var clientValid = await _context.clients.AsNoTracking().AnyAsync(c => c.id == clientId && c.archived_at == null && c.portal_token_version == version, cancellationToken);
        if (!clientValid) return Unauthorized();
        var structuredFeedback = request.Outcome is not null;
        var missedSession = request.Outcome == "not_completed";
        if ((structuredFeedback && !missedSession && request.ActualDurationMinutes is null) ||
            request.ActualDurationMinutes is < 1 or > 1440 ||
            (structuredFeedback && !missedSession && request.SessionRpe is null) ||
            request.SessionRpe is < 0 or > 10)
            return BadRequest(new { message = "Para una sesión realizada, indica una duración entre 1 y 1440 minutos y un RPE entre 0 y 10." });
        if (!IsAllowed(request.Outcome, "completed", "partial", "not_completed") ||
            !IsAllowed(request.NonCompletionReason, "none", "time", "fatigue", "equipment", "motivation", "illness", "other") ||
            !IsAllowed(request.RecoveryRating, "unknown", "good", "partial", "insufficient") ||
            !IsAllowed(request.PainSignal, "none", "new_or_worsening"))
            return BadRequest(new { message = "El feedback de entrenamiento contiene un valor no válido." });
        if (request.Outcome is not null &&
            request.Outcome != "completed" &&
            (request.NonCompletionReason is null or "none"))
            return BadRequest(new { message = "Indica el motivo cuando la sesión fue parcial o no se realizó." });
        if (request.Outcome == "completed" &&
            request.NonCompletionReason is not (null or "none"))
            return BadRequest(new { message = "Una sesión completada no puede tener un motivo de no finalización." });
        if (request.Outcome is null &&
            (request.NonCompletionReason is not null || request.RecoveryRating is not null ||
             request.PainSignal is not null || request.ProgressionTargetAchieved is not null ||
             request.ExerciseFeedback is { Count: > 0 }))
            return BadRequest(new { message = "El feedback estructurado requiere indicar el resultado de la sesión." });
        if (request.Notes?.Length > 4000)
            return BadRequest(new { message = "Las notas no pueden superar los 4000 caracteres." });
        if (request.ExerciseFeedback is { Count: > 100 })
            return BadRequest(new { message = "No se pueden registrar más de 100 ejercicios por sesión." });
        if (request.ExerciseFeedback is not null)
        {
            var invalidExerciseFeedback = request.ExerciseFeedback.Any(item =>
                string.IsNullOrWhiteSpace(item.ExerciseName) || item.ExerciseName.Length > 200 ||
                item.PlannedSets is < 1 or > 100 || item.CompletedSets < 0 ||
                item.CompletedSets > item.PlannedSets ||
                (item.CompletedSets < item.PlannedSets &&
                    (item.SkippedSetReason is null ||
                     !IsAllowed(item.SkippedSetReason, "time", "fatigue", "equipment", "motivation", "illness", "pain", "load_too_heavy", "other"))) ||
                (item.CompletedSets == item.PlannedSets && item.SkippedSetReason is not null) ||
                !IsAllowed(item.Stimulus, "low", "adequate", "high") ||
                !IsAllowed(item.Discomfort, "none", "mild", "strong") ||
                !IsAllowed(item.PreFatigue, "recovered", "loaded"));
            if (invalidExerciseFeedback)
                return BadRequest(new { message = "Revisa las series realizadas y el motivo de las series omitidas de cada ejercicio." });

            foreach (var exercise in request.ExerciseFeedback.Where(item => item.Sets is { Count: > 0 }))
            {
                var sets = exercise.Sets!;
                var invalidSet = sets.Count > 100 || sets.Any(set =>
                    set.SetNumber is < 0 or > 50 ||
                    !IsAllowed(set.Kind, "warmup", "working") ||
                    set.WeightKg is < 0 or > 1000 ||
                    set.Repetitions is < 0 or > 1000 ||
                    set.Rir is < 0 or > 10 ||
                    (set.Completed && (set.Repetitions is null or < 1)) ||
                    (set.Kind == "warmup" && set.Completed && set.Repetitions is null));
                var duplicateSet = sets.GroupBy(set => (set.Kind, set.SetNumber)).Any(group => group.Count() > 1);
                var completedWorkingSets = sets.Count(set => set.Kind == "working" && set.Completed);
                if (invalidSet || duplicateSet || completedWorkingSets != exercise.CompletedSets)
                    return BadRequest(new { message = "Revisa la carga, repeticiones, RIR y estado de cada serie registrada." });
            }

            var duplicateExercise = request.ExerciseFeedback
                .Where(item => !string.IsNullOrWhiteSpace(item.ExerciseId))
                .GroupBy(item => item.ExerciseId, StringComparer.Ordinal)
                .Any(group => group.Count() > 1);
            if (duplicateExercise)
                return BadRequest(new { message = "Un ejercicio no puede aparecer repetido en el registro." });

            var identifiedFeedback = request.ExerciseFeedback
                .Where(item => !string.IsNullOrWhiteSpace(item.ExerciseId))
                .ToArray();
            if (identifiedFeedback.Length > 0)
            {
                await using var prescriptionCommand = Command("""
                    SELECT s.exercise_prescription_data::text
                    FROM training_sessions s
                    JOIN training_plans p ON p.id = s.plan_id
                    JOIN training_profiles f ON f.id = p.profile_id
                    WHERE s.id = @sessionId AND f.client_id = @clientId AND f.enabled = TRUE
                    """, ("sessionId", sessionId), ("clientId", clientId));
                var prescriptionValue = await prescriptionCommand.ExecuteScalarAsync(cancellationToken);
                if (prescriptionValue is not string prescriptionJson)
                    return BadRequest(new { message = "No se puede validar el ID del ejercicio porque esta sesión no tiene una prescripción estructurada." });

                try
                {
                    using var prescriptionDocument = JsonDocument.Parse(prescriptionJson);
                    if (prescriptionDocument.RootElement.ValueKind != JsonValueKind.Object ||
                        !prescriptionDocument.RootElement.TryGetProperty("exercises", out var prescribedExercises) ||
                        prescribedExercises.ValueKind != JsonValueKind.Array)
                        return BadRequest(new { message = "La prescripción estructurada de esta sesión no es válida." });

                    var prescribedSetsById = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var exercise in prescribedExercises.EnumerateArray())
                    {
                        if (exercise.ValueKind != JsonValueKind.Object ||
                            !exercise.TryGetProperty("exerciseId", out var idElement) ||
                            idElement.ValueKind != JsonValueKind.String ||
                            string.IsNullOrWhiteSpace(idElement.GetString()) ||
                            !exercise.TryGetProperty("sets", out var setsElement) ||
                            !setsElement.TryGetInt32(out var prescribedSets) ||
                            prescribedSets < 1 ||
                            !prescribedSetsById.TryAdd(idElement.GetString()!, prescribedSets))
                            return BadRequest(new { message = "La prescripción contiene ejercicios o series no válidos." });
                    }

                    if (identifiedFeedback.Any(item =>
                        !prescribedSetsById.TryGetValue(item.ExerciseId!, out var prescribedSets) ||
                        prescribedSets != item.PlannedSets))
                        return BadRequest(new { message = "El ID y el número de series de cada ejercicio deben coincidir con la prescripción de esta sesión." });
                }
                catch (JsonException)
                {
                    return BadRequest(new { message = "No se pudo validar la prescripción estructurada de esta sesión." });
                }
            }
        }

        await using var command = Command("""
            INSERT INTO training_session_records(
                session_id, client_id, completed_at, actual_duration_minutes, session_rpe, notes,
                outcome, non_completion_reason, recovery_rating, pain_signal, progression_target_achieved, exercise_feedback)
            SELECT s.id, @clientId, CASE WHEN CAST(@outcome AS text) = 'not_completed' THEN NULL ELSE COALESCE(@completedAt, NOW()) END, @duration, @rpe, @notes,
                   @outcome, @reason, @recovery, @pain, @progressionAchieved,
                   CASE WHEN CAST(@exerciseFeedback AS text) IS NULL THEN NULL ELSE CAST(@exerciseFeedback AS jsonb) END
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE s.id = @sessionId AND f.client_id = @clientId AND f.enabled = TRUE
            ON CONFLICT(session_id, client_id) DO UPDATE SET
                completed_at = EXCLUDED.completed_at,
                actual_duration_minutes = EXCLUDED.actual_duration_minutes,
                session_rpe = EXCLUDED.session_rpe,
                notes = EXCLUDED.notes,
                outcome = COALESCE(EXCLUDED.outcome, training_session_records.outcome),
                non_completion_reason = COALESCE(EXCLUDED.non_completion_reason, training_session_records.non_completion_reason),
                recovery_rating = COALESCE(EXCLUDED.recovery_rating, training_session_records.recovery_rating),
                pain_signal = COALESCE(EXCLUDED.pain_signal, training_session_records.pain_signal),
                progression_target_achieved = COALESCE(EXCLUDED.progression_target_achieved, training_session_records.progression_target_achieved),
                exercise_feedback = COALESCE(EXCLUDED.exercise_feedback, training_session_records.exercise_feedback),
                updated_at = NOW()
            RETURNING id
            """, ("sessionId", sessionId), ("clientId", clientId), ("completedAt", request.CompletedAt),
            ("duration", request.ActualDurationMinutes), ("rpe", request.SessionRpe), ("notes", request.Notes),
            ("outcome", request.Outcome), ("reason", request.NonCompletionReason), ("recovery", request.RecoveryRating),
            ("pain", request.PainSignal), ("progressionAchieved", request.ProgressionTargetAchieved),
            ("exerciseFeedback", request.ExerciseFeedback is { Count: > 0 }
                ? JsonSerializer.Serialize(request.ExerciseFeedback, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                : null));
        var id = await command.ExecuteScalarAsync(cancellationToken);
        if (id is null) return NotFound();
        if (structuredFeedback)
        {
            await CreateAdaptationProposalAsync(clientId, sessionId, Convert.ToInt64(id), cancellationToken);
            try { await ApplyMuscleFeedbackAsync(clientId, sessionId, Convert.ToInt64(id), cancellationToken); }
            catch (Exception) { /* muscle feedback is best effort; it must not fail the record */ }
            try { await _autoAdaptation.ApplyLoadSuggestionsAsync(clientId, cancellationToken); }
            catch (Exception) { /* suggestions are still shown from the progression engine */ }
        }
        return Ok(new { id = Convert.ToInt64(id), sessionId, clientId });
    }

    private async Task CreateAdaptationProposalAsync(int clientId, int sessionId, long recordId, CancellationToken cancellationToken)
    {
        var feedback = new List<(long RecordId, Anguloso.Server.Logica.TrainingSessionFeedback Feedback)>();
        await using (var command = Command("""
            SELECT r.id, r.outcome, r.non_completion_reason, r.recovery_rating, r.pain_signal,
                   r.session_rpe, r.progression_target_achieved, r.exercise_feedback::text
            FROM training_session_records r
            JOIN training_sessions s ON s.id = r.session_id
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE f.client_id = @clientId AND s.plan_id = (
                SELECT plan_id FROM training_sessions WHERE id = @sessionId
            ) AND p.status = 'active' AND r.outcome IS NOT NULL
            ORDER BY CASE WHEN r.id = @recordId THEN 0 ELSE 1 END, s.scheduled_date DESC, r.id DESC
            LIMIT 3
            """, ("clientId", clientId), ("sessionId", sessionId), ("recordId", recordId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                feedback.Add((reader.GetInt64(0), new Anguloso.Server.Logica.TrainingSessionFeedback(
                    ParseOutcome(reader.GetString(1)),
                    reader.IsDBNull(2) ? Anguloso.Server.Logica.TrainingNonCompletionReason.None : ParseReason(reader.GetString(2)),
                    reader.IsDBNull(3) ? Anguloso.Server.Logica.TrainingRecoveryRating.Unknown : ParseRecovery(reader.GetString(3)),
                    HasPainSkippedSet(reader.IsDBNull(7) ? null : reader.GetString(7))
                        ? Anguloso.Server.Logica.TrainingPainSignal.NewOrWorsening
                        : reader.IsDBNull(4) ? Anguloso.Server.Logica.TrainingPainSignal.None : ParsePain(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    reader.IsDBNull(6) ? null : reader.GetBoolean(6),
                    CalculateSetCompletionRatio(reader.IsDBNull(7) ? null : reader.GetString(7)))));
        }
        if (feedback.Count == 0 || feedback.All(x => x.RecordId != recordId)) return;
        feedback.Reverse();
        var decision = Anguloso.Server.Logica.TrainingAdaptationPolicy.Evaluate(feedback.Select(x => x.Feedback).ToArray());
        var exerciseRecommendations = await GetExerciseVolumeRecommendationsAsync(clientId, sessionId, cancellationToken);
        var fatigueCandidates = exerciseRecommendations
            .Where(item => item.Action == Anguloso.Server.Logica.TrainingExerciseVolumeAction.ProposeReduceOneSet)
            .ToArray();
        var restoreCandidates = exerciseRecommendations
            .Where(item => item.Action == Anguloso.Server.Logica.TrainingExerciseVolumeAction.ProposeRestoreOneSet)
            .ToArray();
        var hasExercisePain = exerciseRecommendations.Any(item =>
            item.Action == Anguloso.Server.Logica.TrainingExerciseVolumeAction.PauseAndReview);

        // A localized pain signal has precedence. Otherwise, repeated exercise-specific fatigue
        // can create a review proposal even when global session recovery looks acceptable.
        var action = decision.Action == Anguloso.Server.Logica.TrainingAdaptationAction.PauseAffectedExerciseAndReview || hasExercisePain
            ? "pause_affected_exercise_and_review"
            : fatigueCandidates.Length > 0
                ? "review_and_reduce_training_stress"
                : restoreCandidates.Length > 0 && decision.Action == Anguloso.Server.Logica.TrainingAdaptationAction.NoChange
                    ? "restore_volume"
                    : ToApiAction(decision.Action);
        var explanation = decision.Explanation;
        if (fatigueCandidates.Length > 0 && action == "review_and_reduce_training_stress")
            explanation += " Señales repetidas por ejercicio: " +
                string.Join(", ", fatigueCandidates.Select(item => item.ExerciseId)) +
                ". Revisa cada candidato; no se aplicará ningún cambio sin aprobación profesional.";
        if (action == "restore_volume")
            explanation = "Las últimas exposiciones se completaron sin señales de fatiga con el volumen reducido. Se propone recuperar una serie en: " +
                string.Join(", ", restoreCandidates.Select(item => item.ExerciseId)) + ". No se aplica ningún cambio sin aprobación profesional.";
        if (action is null) return;

        await using var supersede = Command("""
            UPDATE training_adaptation_proposals
            SET status = 'superseded', review_notes = 'Sustituida por una nueva evaluación del feedback.'
            WHERE source_record_id = @recordId AND status = 'pending' AND action <> @action
            """, ("recordId", recordId), ("action", action));
        await supersede.ExecuteNonQueryAsync(cancellationToken);

        await using var insert = Command("""
            INSERT INTO training_adaptation_proposals(
                client_id, plan_id, source_session_id, source_record_id, action, explanation)
            SELECT @clientId, s.plan_id, s.id, @recordId, @action, @explanation
            FROM training_sessions s
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE s.id = @sessionId AND f.client_id = @clientId AND p.status = 'active'
            ON CONFLICT(source_record_id, action) DO NOTHING
            """, ("clientId", clientId), ("sessionId", sessionId), ("recordId", recordId),
            ("action", action), ("explanation", explanation));
        await insert.ExecuteNonQueryAsync(cancellationToken);

        // The system adjusts the future sessions by itself (product decision). Pain and reorganisation
        // stay pending for a person; the policy also applies cooldowns. A failure never fails the record.
        try
        {
            long? proposalId = null; int planId = 0;
            await using (var find = Command("SELECT id, plan_id FROM training_adaptation_proposals WHERE source_record_id = @recordId AND action = @action AND status = 'pending'", ("recordId", recordId), ("action", action)))
            await using (var found = await find.ExecuteReaderAsync(cancellationToken))
                if (await found.ReadAsync(cancellationToken)) { proposalId = found.GetInt64(0); planId = found.GetInt32(1); }
            if (proposalId is not null)
            {
                var targetIds = action == "restore_volume" ? restoreCandidates.Select(item => item.ExerciseId).ToArray()
                    : action == "review_and_reduce_training_stress" ? fatigueCandidates.Select(item => item.ExerciseId).ToArray()
                    : Array.Empty<string>();
                await _autoAdaptation.TryApplyAsync(proposalId.Value, planId, action, targetIds, cancellationToken);
            }
        }
        catch (Exception) { /* the proposal simply remains pending */ }
    }


    /// <summary>
    /// Applies the post-exercise answers (stimulus, discomfort, pre-fatigue) of the plan's last sessions:
    /// +1/-1 set per muscle is applied by the system itself; strong discomfort pauses the exercise and
    /// repeated mild discomfort suggests a swap (both stay pending for a person / the patient).
    /// </summary>
    private async Task ApplyMuscleFeedbackAsync(int clientId, int sessionId, long recordId, CancellationToken cancellationToken)
    {
        var exposures = new List<Anguloso.Server.Logica.TrainingMuscleFeedbackExposure>();
        var goal = Anguloso.Server.Logica.TrainingGoal.GeneralFitness;
        int planId = 0;
        string experienceText = "beginner";
        await using (var command = Command("""
            SELECT s.plan_id, s.scheduled_date, s.exercise_prescription_data::text, r.exercise_feedback::text, f.experience_level
            FROM training_session_records r
            JOIN training_sessions s ON s.id = r.session_id
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE f.client_id = @clientId AND p.status = 'active' AND r.outcome IS NOT NULL
              AND s.plan_id = (SELECT plan_id FROM training_sessions WHERE id = @sessionId)
              AND s.exercise_prescription_data IS NOT NULL AND r.exercise_feedback IS NOT NULL
            ORDER BY s.scheduled_date DESC, s.id DESC LIMIT 4
            """, ("clientId", clientId), ("sessionId", sessionId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                planId = reader.GetInt32(0);
                experienceText = reader.GetString(4);
                var date = DateOnly.FromDateTime(reader.GetDateTime(1));
                var prescribed = new Dictionary<string, (string[] Muscles, int Sets)>(StringComparer.Ordinal);
                try
                {
                    using var prescription = JsonDocument.Parse(reader.GetString(2));
                    if (prescription.RootElement.TryGetProperty("goal", out var goalElement) && goalElement.ValueKind == JsonValueKind.String)
                        Enum.TryParse(goalElement.GetString(), true, out goal);
                    foreach (var exercise in prescription.RootElement.GetProperty("exercises").EnumerateArray())
                    {
                        var id = exercise.GetProperty("exerciseId").GetString();
                        if (id is null) continue;
                        var muscles = exercise.TryGetProperty("primaryMuscleGroups", out var m) && m.ValueKind == JsonValueKind.Array
                            ? m.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
                        prescribed[id] = (muscles, exercise.TryGetProperty("sets", out var s) && s.TryGetInt32(out var sets) ? sets : 0);
                    }
                    using var feedback = JsonDocument.Parse(reader.GetString(3));
                    foreach (var item in feedback.RootElement.EnumerateArray())
                    {
                        var id = item.TryGetProperty("exerciseId", out var e) ? e.GetString() : null;
                        if (id is null || !prescribed.TryGetValue(id, out var info)) continue;
                        string? Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                        exposures.Add(new(date, id, info.Muscles, info.Sets, Text("stimulus"), Text("discomfort"), Text("preFatigue")));
                    }
                }
                catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
            }
        }
        if (exposures.Count == 0 || planId == 0) return;
        exposures.Reverse(); // chronological
        if (!Enum.TryParse<Anguloso.Server.Logica.TrainingExperience>(experienceText, true, out var experience))
            experience = Anguloso.Server.Logica.TrainingExperience.Beginner;

        var plannedByMuscle = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var upcoming = Command("""
            SELECT s.exercise_prescription_data::text FROM training_sessions s
            WHERE s.plan_id = @planId AND s.scheduled_date > CURRENT_DATE AND s.scheduled_date <= CURRENT_DATE + 7
              AND s.status = 'planned' AND s.exercise_prescription_data IS NOT NULL
            """, ("planId", planId)))
        await using (var upcomingReader = await upcoming.ExecuteReaderAsync(cancellationToken))
            while (await upcomingReader.ReadAsync(cancellationToken))
                try
                {
                    using var doc = JsonDocument.Parse(upcomingReader.GetString(0));
                    foreach (var exercise in doc.RootElement.GetProperty("exercises").EnumerateArray())
                    {
                        var sets = exercise.TryGetProperty("sets", out var s) && s.TryGetInt32(out var n) ? n : 0;
                        if (exercise.TryGetProperty("primaryMuscleGroups", out var m) && m.ValueKind == JsonValueKind.Array)
                            foreach (var muscle in m.EnumerateArray().Select(x => x.GetString()!))
                                plannedByMuscle[muscle] = plannedByMuscle.GetValueOrDefault(muscle) + sets;
                    }
                }
                catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
        var ceilings = Anguloso.Server.Logica.TrainingProgramVolumeAllocator.GetWeeklyCeilings(goal, experience)
            .ToDictionary(pair => pair.Key.ToString(), pair => (int)pair.Value, StringComparer.Ordinal);

        var decisions = Anguloso.Server.Logica.TrainingMuscleFeedbackAdapter.Evaluate(exposures, plannedByMuscle, ceilings);
        // One proposal per action and record (unique constraint), but it must carry EVERY affected exercise:
        // dropping all but the first would silently lose strong-discomfort pauses or swaps.
        foreach (var actionGroup in decisions.GroupBy(item => item.Action))
        {
            var decision = actionGroup.First();
            var all = actionGroup.ToList();
            var explanation = string.Join(" ", all.Select(item => $"{item.Explanation} (ejercicio: {item.ExerciseId})"));
            if (all.Count > 1 && decision.Action is Anguloso.Server.Logica.TrainingMuscleFeedbackAction.AddSet or Anguloso.Server.Logica.TrainingMuscleFeedbackAction.ReduceSet)
                explanation += " Solo se aplica automáticamente el primer ajuste; el resto se reevalúa con las próximas sesiones.";
            var action = decision.Action switch
            {
                Anguloso.Server.Logica.TrainingMuscleFeedbackAction.AddSet => "add_muscle_set",
                Anguloso.Server.Logica.TrainingMuscleFeedbackAction.ReduceSet => "reduce_muscle_set",
                Anguloso.Server.Logica.TrainingMuscleFeedbackAction.SuggestSwap => "suggest_exercise_swap",
                _ => "pause_affected_exercise_and_review"
            };
            await using var insert = Command("""
                INSERT INTO training_adaptation_proposals(client_id, plan_id, source_session_id, source_record_id, action, explanation)
                VALUES (@clientId, @planId, @sessionId, @recordId, @action, @explanation)
                ON CONFLICT(source_record_id, action) DO NOTHING
                RETURNING id
                """, ("clientId", clientId), ("planId", planId), ("sessionId", sessionId), ("recordId", recordId),
                ("action", action), ("explanation", explanation));
            var proposalId = await insert.ExecuteScalarAsync(cancellationToken);
            if (proposalId is not null && action is "add_muscle_set" or "reduce_muscle_set")
                await _autoAdaptation.TryApplyAsync(Convert.ToInt64(proposalId), planId, action, [decision.ExerciseId], cancellationToken);
        }
    }

    private async Task<IReadOnlyList<Anguloso.Server.Logica.TrainingExerciseVolumeRecommendation>>
        GetExerciseVolumeRecommendationsAsync(int clientId, int sessionId, CancellationToken cancellationToken)
    {
        var records = new List<Anguloso.Server.Logica.TrainingExerciseVolumeFeedbackRecord>();
        await using (var command = Command("""
            SELECT r.outcome, r.exercise_feedback::text, s.exercise_prescription_data::text
            FROM training_session_records r
            JOIN training_sessions s ON s.id = r.session_id
            JOIN training_plans p ON p.id = s.plan_id
            JOIN training_profiles f ON f.id = p.profile_id
            WHERE f.client_id = @clientId AND s.plan_id = (
                SELECT plan_id FROM training_sessions WHERE id = @sessionId
            ) AND p.status = 'active' AND r.outcome IS NOT NULL
            ORDER BY s.scheduled_date DESC, r.id DESC
            LIMIT 12
            """, ("clientId", clientId), ("sessionId", sessionId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                records.Add(new Anguloso.Server.Logica.TrainingExerciseVolumeFeedbackRecord(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        var exposures = Anguloso.Server.Logica.TrainingExerciseVolumeFeedbackMapper.Map(records);
        return Anguloso.Server.Logica.TrainingExerciseVolumeAdapter.Evaluate(exposures);
    }

    private static bool HasPainSkippedSet(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array &&
                document.RootElement.EnumerateArray().Any(item =>
                    item.TryGetProperty("skippedSetReason", out var reason) &&
                    reason.ValueKind == JsonValueKind.String &&
                    string.Equals(reason.GetString(), "pain", StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static decimal? CalculateSetCompletionRatio(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            decimal planned = 0;
            decimal completed = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("plannedSets", out var plannedElement) ||
                    !item.TryGetProperty("completedSets", out var completedElement) ||
                    !plannedElement.TryGetDecimal(out var plannedSets) ||
                    !completedElement.TryGetDecimal(out var completedSets) ||
                    plannedSets <= 0 || completedSets < 0 || completedSets > plannedSets)
                    return null;
                planned += plannedSets;
                completed += completedSets;
            }
            return planned > 0 ? completed / planned : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private NpgsqlCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) connection.Open();
        var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        command.CommandText = sql;
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

    private static bool IsAllowed(string? value, params string[] allowed) =>
        value is null || allowed.Contains(value, StringComparer.Ordinal);

    private static Anguloso.Server.Logica.TrainingSessionOutcome ParseOutcome(string value) => value switch
    {
        "completed" => Anguloso.Server.Logica.TrainingSessionOutcome.Completed,
        "partial" => Anguloso.Server.Logica.TrainingSessionOutcome.Partial,
        _ => Anguloso.Server.Logica.TrainingSessionOutcome.NotCompleted
    };

    private static Anguloso.Server.Logica.TrainingNonCompletionReason ParseReason(string value) => value switch
    {
        "time" => Anguloso.Server.Logica.TrainingNonCompletionReason.Time,
        "fatigue" => Anguloso.Server.Logica.TrainingNonCompletionReason.Fatigue,
        "equipment" => Anguloso.Server.Logica.TrainingNonCompletionReason.Equipment,
        "motivation" => Anguloso.Server.Logica.TrainingNonCompletionReason.Motivation,
        "illness" => Anguloso.Server.Logica.TrainingNonCompletionReason.Illness,
        "other" => Anguloso.Server.Logica.TrainingNonCompletionReason.Other,
        _ => Anguloso.Server.Logica.TrainingNonCompletionReason.None
    };

    private static Anguloso.Server.Logica.TrainingRecoveryRating ParseRecovery(string value) => value switch
    {
        "good" => Anguloso.Server.Logica.TrainingRecoveryRating.Good,
        "partial" => Anguloso.Server.Logica.TrainingRecoveryRating.Partial,
        "insufficient" => Anguloso.Server.Logica.TrainingRecoveryRating.Insufficient,
        _ => Anguloso.Server.Logica.TrainingRecoveryRating.Unknown
    };

    private static Anguloso.Server.Logica.TrainingPainSignal ParsePain(string value) =>
        value == "new_or_worsening"
            ? Anguloso.Server.Logica.TrainingPainSignal.NewOrWorsening
            : Anguloso.Server.Logica.TrainingPainSignal.None;

    private static string ToApiAction(Anguloso.Server.Logica.TrainingAdaptationAction action) => action switch
    {
        Anguloso.Server.Logica.TrainingAdaptationAction.InsufficientData => "insufficient_data",
        Anguloso.Server.Logica.TrainingAdaptationAction.InvalidFeedback => "invalid_feedback",
        Anguloso.Server.Logica.TrainingAdaptationAction.NoChange => "no_change",
        Anguloso.Server.Logica.TrainingAdaptationAction.ReorganizeFutureSessions => "reorganize_future_sessions",
        Anguloso.Server.Logica.TrainingAdaptationAction.HoldProgressionAndReview => "hold_progression_and_review",
        Anguloso.Server.Logica.TrainingAdaptationAction.ReviewAndReduceTrainingStress => "review_and_reduce_training_stress",
        Anguloso.Server.Logica.TrainingAdaptationAction.ProgressAccordingToPrescription => "progress_according_to_prescription",
        Anguloso.Server.Logica.TrainingAdaptationAction.ProposeDeload => "propose_deload",
        _ => "pause_affected_exercise_and_review"
    };
}

public sealed record PatientTrainingSetFeedback(
    int SetNumber,
    string Kind = "working",
    decimal? WeightKg = null,
    int? Repetitions = null,
    decimal? Rir = null,
    bool Completed = false);

public sealed record PatientTrainingExerciseFeedback(
    string? ExerciseId,
    string ExerciseName,
    int PlannedSets,
    int CompletedSets,
    string? SkippedSetReason = null,
    List<PatientTrainingSetFeedback>? Sets = null,
    string? Stimulus = null,
    string? Discomfort = null,
    string? PreFatigue = null);

public sealed record PatientTrainingRecordRequest(
    DateTime? CompletedAt,
    int? ActualDurationMinutes,
    decimal? SessionRpe,
    string? Notes,
    string? Outcome = null,
    string? NonCompletionReason = null,
    string? RecoveryRating = null,
    string? PainSignal = null,
    bool? ProgressionTargetAchieved = null,
    List<PatientTrainingExerciseFeedback>? ExerciseFeedback = null);

public sealed record PatientTrainingSwapRequest(string ReplacementExerciseId);
