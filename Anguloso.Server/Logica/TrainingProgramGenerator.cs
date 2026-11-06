namespace Anguloso.Server.Logica;

/// <summary>
/// First deterministic draft generator. It only selects from explicitly supplied, reviewed catalog metadata.
/// It does not infer exercise properties from names or automatically publish/apply the generated plan.
/// </summary>
public static class TrainingProgramGenerator
{
    private const decimal SecondsPerRepEstimate = 2.5m;
    private const int TransitionSecondsPerExercise = 45;
    private const int WarmupAndTransitionsMinutes = 5;

    public static TrainingProgramGenerationResult Generate(
        TrainingProgramGenerationRequest request,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);

        var inputErrors = ValidateRequest(request);
        if (inputErrors.Count > 0)
            return new(null, inputErrors);

        var candidateErrors = ValidateCatalog(catalog);
        if (candidateErrors.Count > 0)
            return new(null, candidateErrors);

        var days = request.TrainingDays
            .Distinct()
            .OrderBy(day => ((int)day + 6) % 7)
            .ToArray();
        var split = BuildSplit(days.Length);
        var excluded = new HashSet<string>(request.ExcludedExerciseIds, StringComparer.OrdinalIgnoreCase);
        var equipment = new HashSet<string>(request.AvailableEquipment, StringComparer.OrdinalIgnoreCase);
        var sessions = new List<TrainingSessionPrescription>();
        for (var week = 1; week <= request.DurationWeeks; week++)
        {
            // Keep exercise selection stable across weeks to support measurable progression.
            // Variety is managed within each week's sessions, not across the mesocycle.
            var selectedThisWeek = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var dayIndex = 0; dayIndex < days.Length; dayIndex++)
            {
                var sessionTemplate = split[dayIndex];
                var requiredPatterns = sessionTemplate.Patterns;
                var prescriptions = new List<TrainingExercisePrescription>();
                for (var patternIndex = 0; patternIndex < requiredPatterns.Count; patternIndex++)
                {
                    var pattern = requiredPatterns[patternIndex];
                    var compatibleCandidates = catalog
                        .Where(x => (x.IsReviewed || x.IsOwnedByCurrentProfessional)
                            && x.MovementPattern == pattern
                            && x.SuitableGoals.Contains(request.Goal)
                            && (int)x.MinimumExperience <= (int)request.Experience
                            && (equipment.Count == 0 || x.RequiredEquipment.All(equipment.Contains))
                            && !excluded.Contains(x.ExerciseId))
                        .OrderByDescending(x => x.SelectionPriority)
                        .ThenBy(x => x.ExerciseId, StringComparer.Ordinal)
                        .ToArray();

                    // Prefer a different exercise elsewhere in this week's plan when possible.
                    // Never penalize an exercise for being selected in previous weeks: continuity
                    // across a mesocycle supports progression and performance tracking.
                    var candidate = compatibleCandidates
                        .FirstOrDefault(x => !selectedThisWeek.Contains(x.ExerciseId))
                        ?? compatibleCandidates.FirstOrDefault();

                    if (candidate is null)
                        return Failure(BuildCandidateDiagnostic(pattern, request, catalog, equipment, excluded));

                    selectedThisWeek.Add(candidate.ExerciseId);

                    var (sets, minReps, maxReps, rir, rest, progression) = PrescriptionFor(request.Goal, request.Experience, week, days.Length, pattern);
                    prescriptions.Add(new TrainingExercisePrescription(
                        candidate.ExerciseId,
                        candidate.CatalogVersion,
                        candidate.Name,
                        patternIndex + 1,
                        sets,
                        minReps,
                        maxReps,
                        rir,
                        rest,
                        progression,
                        $"Patrón {pattern}; metadatos {(candidate.IsReviewed ? "revisados" : "aportados por el profesional propietario")}; compatible con objetivo, experiencia, material y exclusiones.",
                        candidate.PrimaryMuscleGroups.ToArray(),
                        candidate.SecondaryMuscleGroups.ToArray()));
                }

                AddOptionalAccessoryExercises(
                    request, catalog, equipment, excluded, sessions,
                    prescriptions, week, days[dayIndex], sessionTemplate.Title, days.Length);

                // Final duration is calculated after weekly sets have been allocated.
                sessions.Add(new TrainingSessionPrescription(
                    week,
                    days[dayIndex],
                    sessionTemplate.Title,
                    WarmupAndTransitionsMinutes,
                    prescriptions));
            }
        }

        var allocation = TrainingProgramVolumeAllocator.Allocate(
            sessions, request.Goal, request.Experience, request.MuscleVolumeTargets,
            request.MaximumSessionDurationMinutes);
        if (!allocation.Succeeded)
            return new(null, allocation.Errors);

        var prescribedSessions = request.IsDeload
            ? ApplyConservativeDeload(allocation.Sessions)
            : allocation.Sessions;
        var plan = new TrainingProgramPrescription(
            "0.9.0",
            request.Goal.ToString(),
            request.StartDate,
            request.DurationWeeks,
            prescribedSessions);
        var qualityReport = TrainingProgramQualityAnalyzer.Analyze(plan);
        var oneSetExposures = allocation.Sessions.SelectMany(session => session.Exercises)
            .Count(exercise => exercise.Sets <= 1);
        var deloadWarnings = request.IsDeload
            ? new[]
            {
                "Semana/fase de descarga activa: las series se reducen aproximadamente entre un tercio y la mitad (redondeo hacia arriba, mínimo una por ejercicio) y el objetivo de esfuerzo se aleja dos RIR. Es una heurística conservadora revisable, no una pauta establecida por ACSM.",
                oneSetExposures > 0
                    ? $"{oneSetExposures} exposiciones ya estaban prescritas a una serie y mantienen esa serie; en ellas la descarga actúa mediante el mayor margen de RIR, no mediante un recorte adicional de series."
                    : null
            }.Where(warning => warning is not null).Select(warning => warning!).ToArray()
            : Array.Empty<string>();
        plan = plan with
        {
            QualityReport = qualityReport with
            {
                Warnings = qualityReport.Warnings.Concat(allocation.Warnings).Concat(deloadWarnings).Distinct(StringComparer.Ordinal).ToArray()
            }
        };
        var validationErrors = TrainingPrescriptionValidator.Validate(plan);
        return validationErrors.Count == 0
            ? new(plan, Array.Empty<string>())
            : new(null, validationErrors);

        TrainingProgramGenerationResult Failure(string error) => new(null, [error]);
    }

    private static IReadOnlyList<TrainingSessionPrescription> ApplyConservativeDeload(
        IReadOnlyList<TrainingSessionPrescription> sessions) =>
        sessions.Select(session => session with
        {
            Exercises = session.Exercises.Select(exercise => exercise with
            {
                Sets = Math.Max(1, (int)Math.Ceiling(exercise.Sets * 0.5m)),
                TargetRepetitionsInReserve = exercise.TargetRepetitionsInReserve is null
                    ? null
                    : Math.Min(5m, exercise.TargetRepetitionsInReserve.Value + 2m),
                SelectionRationale = exercise.SelectionRationale + " Descarga activa: reducción conservadora de series y mayor reserva respecto al fallo."
            }).ToArray()
        }).ToArray();

    private static string BuildCandidateDiagnostic(
        TrainingMovementPattern pattern,
        TrainingProgramGenerationRequest request,
        IReadOnlyCollection<TrainingExerciseCandidate> catalog,
        IReadOnlySet<string> equipment,
        IReadOnlySet<string> excluded)
    {
        var reviewed = catalog.Where(x => x.IsReviewed || x.IsOwnedByCurrentProfessional).ToArray();
        var matchingPattern = reviewed.Where(x => x.MovementPattern == pattern).ToArray();
        var matchingGoal = matchingPattern.Where(x => x.SuitableGoals.Contains(request.Goal)).ToArray();
        var matchingExperience = matchingGoal.Where(x => (int)x.MinimumExperience <= (int)request.Experience).ToArray();
        var matchingEquipment = matchingExperience.Where(x => equipment.Count == 0 || x.RequiredEquipment.All(equipment.Contains)).ToArray();
        var matchingExclusions = matchingEquipment.Where(x => !excluded.Contains(x.ExerciseId)).ToArray();

        return $"No hay ejercicios compatibles para el patrón '{pattern}'. " +
            $"Candidatos: catálogo={catalog.Count}, revisados/propios={reviewed.Length}, patrón={matchingPattern.Length}, " +
            $"objetivo={matchingGoal.Length}, experiencia={matchingExperience.Length}, material={matchingEquipment.Length}, " +
            $"tras exclusiones={matchingExclusions.Length}. " +
            "Comprueba aprobación, patrón, objetivo, nivel mínimo, material disponible y ejercicios excluidos.";
    }

    private static IReadOnlyList<string> ValidateRequest(TrainingProgramGenerationRequest request)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(request.Goal)) errors.Add("El objetivo de entrenamiento no es válido.");
        if (!Enum.IsDefined(request.Experience)) errors.Add("El nivel de experiencia no es válido.");
        if (request.DurationWeeks is < 1 or > 52) errors.Add("La duración debe estar entre 1 y 52 semanas.");
        if (request.IsDeload && request.DurationWeeks != 1)
            errors.Add("Una descarga activa debe durar exactamente una semana; divide la fase antes de generar.");
        if (request.TrainingDays is null || request.TrainingDays.Count is < 2 or > 4)
            errors.Add("Esta primera versión admite de 2 a 4 días de entrenamiento por semana.");
        else
        {
            if (request.TrainingDays.Any(day => !Enum.IsDefined(day)))
                errors.Add("La disponibilidad contiene un día no válido.");
            if (request.TrainingDays.Distinct().Count() != request.TrainingDays.Count)
                errors.Add("No se puede repetir un día de entrenamiento.");
        }
        if (request.MaximumSessionDurationMinutes is < 35 or > 180)
            errors.Add("La duración máxima de sesión debe estar entre 35 y 180 minutos.");
        if (request.AvailableEquipment is null)
            errors.Add("Debe indicarse el equipamiento disponible, aunque sea una lista vacía.");
        if (request.ExcludedExerciseIds is null)
            errors.Add("La lista de ejercicios excluidos es obligatoria, aunque esté vacía.");
        return errors;
    }

    private static IReadOnlyList<string> ValidateCatalog(IReadOnlyCollection<TrainingExerciseCandidate> catalog)
    {
        var errors = new List<string>();
        if (catalog.Count == 0)
        {
            errors.Add("El catálogo de ejercicios está vacío.");
            return errors;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exercise in catalog)
        {
            if (string.IsNullOrWhiteSpace(exercise.ExerciseId) || exercise.ExerciseId.Length > 100)
                errors.Add("Todos los ejercicios del catálogo deben tener un ID estable de hasta 100 caracteres.");
            else if (!seenIds.Add(exercise.ExerciseId))
                errors.Add($"El catálogo contiene el ID duplicado '{exercise.ExerciseId}'.");
            if (string.IsNullOrWhiteSpace(exercise.CatalogVersion) || exercise.CatalogVersion.Length > 40)
                errors.Add($"El ejercicio '{exercise.ExerciseId}' no tiene una versión de catálogo válida.");
            if (string.IsNullOrWhiteSpace(exercise.Name) || exercise.Name.Length > 160)
                errors.Add($"El ejercicio '{exercise.ExerciseId}' no tiene un nombre válido.");
            if (!Enum.IsDefined(exercise.MovementPattern))
                errors.Add($"El ejercicio '{exercise.ExerciseId}' tiene un patrón de movimiento no válido.");
            if (!Enum.IsDefined(exercise.MinimumExperience))
                errors.Add($"El ejercicio '{exercise.ExerciseId}' tiene un nivel mínimo de experiencia no válido.");
            if (exercise.SelectionPriority is < 0 or > 100)
                errors.Add($"El ejercicio '{exercise.ExerciseId}' debe tener una prioridad de selección entre 0 y 100.");
            if (exercise.PrimaryMuscleGroups is null || exercise.PrimaryMuscleGroups.Count == 0)
                errors.Add($"El ejercicio '{exercise.ExerciseId}' debe declarar al menos un grupo muscular principal revisado.");
            if (exercise.SecondaryMuscleGroups is null)
                errors.Add($"El ejercicio '{exercise.ExerciseId}' debe declarar grupos secundarios, aunque la lista esté vacía.");
            if (exercise.PrimaryMuscleGroups is not null)
            {
                if (exercise.PrimaryMuscleGroups.Any(muscle => !Enum.IsDefined(muscle)))
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' contiene un grupo muscular principal no válido.");
                if (exercise.PrimaryMuscleGroups.Distinct().Count() != exercise.PrimaryMuscleGroups.Count)
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' repite grupos musculares principales.");
            }
            if (exercise.SecondaryMuscleGroups is not null)
            {
                if (exercise.SecondaryMuscleGroups.Any(muscle => !Enum.IsDefined(muscle)))
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' contiene un grupo muscular secundario no válido.");
                if (exercise.SecondaryMuscleGroups.Distinct().Count() != exercise.SecondaryMuscleGroups.Count)
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' repite grupos musculares secundarios.");
                if (exercise.PrimaryMuscleGroups is not null &&
                    exercise.PrimaryMuscleGroups.Intersect(exercise.SecondaryMuscleGroups).Any())
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' declara un músculo como principal y secundario.");
            }

            if (exercise.RequiredEquipment is null || exercise.SuitableGoals is null || exercise.SuitableGoals.Count == 0)
            {
                errors.Add($"El ejercicio '{exercise.ExerciseId}' debe declarar material y al menos un objetivo compatible.");
            }
            else
            {
                if (exercise.RequiredEquipment.Any(string.IsNullOrWhiteSpace))
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' contiene un requisito de material vacío.");
                if (exercise.SuitableGoals.Any(goal => !Enum.IsDefined(goal)))
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' contiene un objetivo compatible no válido.");
                if (exercise.SuitableGoals.Distinct().Count() != exercise.SuitableGoals.Count)
                    errors.Add($"El ejercicio '{exercise.ExerciseId}' repite objetivos compatibles.");
            }
        }

        return errors;
    }

    private static void AddOptionalAccessoryExercises(
        TrainingProgramGenerationRequest request,
        IEnumerable<TrainingExerciseCandidate> catalog,
        HashSet<string> equipment,
        HashSet<string> excluded,
        IReadOnlyList<TrainingSessionPrescription> previousSessions,
        List<TrainingExercisePrescription> currentExercises,
        int week,
        DayOfWeek day,
        string title,
        int daysPerWeek)
    {
        var targets = TrainingProgramVolumeAllocator.GetWeeklyTargets(
            request.Goal, request.Experience, request.MuscleVolumeTargets);
        var isFullBody = title.Contains("completo", StringComparison.OrdinalIgnoreCase);
        // Short sessions must protect time for the core movement patterns. Accessory slots
        // scale with the available duration instead of being appended in the same quantity
        // to a 35-minute session and a 90-minute session.
        var slots = request.MaximumSessionDurationMinutes <= 40
            ? (isFullBody ? 1 : 0)
            : request.MaximumSessionDurationMinutes <= 65
                ? (isFullBody ? 2 : 1)
                : (isFullBody ? 3 : 2);
        var allowedMuscles = title.Contains("superior", StringComparison.OrdinalIgnoreCase)
            ? UpperBodyAccessoryMuscles
            : title.Contains("inferior", StringComparison.OrdinalIgnoreCase)
                ? LowerBodyAccessoryMuscles
                : null;

        for (var slot = 0; slot < slots; slot++)
        {
            var currentPrimaryVolume = new Dictionary<TrainingMuscleGroup, decimal>();
            foreach (var exercise in previousSessions.Where(session => session.WeekNumber == week)
                         .SelectMany(session => session.Exercises).Concat(currentExercises))
            foreach (var muscle in exercise.PrimaryMuscleGroups.Distinct())
                currentPrimaryVolume[muscle] = currentPrimaryVolume.GetValueOrDefault(muscle) + exercise.Sets;

            var candidates = catalog
                .Where(candidate => candidate.MovementPattern == TrainingMovementPattern.Accessory
                    && (candidate.IsReviewed || candidate.IsOwnedByCurrentProfessional)
                    && candidate.SuitableGoals.Contains(request.Goal)
                    && (int)candidate.MinimumExperience <= (int)request.Experience
                    && (equipment.Count == 0 || candidate.RequiredEquipment.All(equipment.Contains))
                    && !excluded.Contains(candidate.ExerciseId)
                    // Accessory exercises may repeat on different training days. Only prevent
                    // duplicate prescription of the same accessory within this session.
                    && !currentExercises.Any(exercise =>
                        string.Equals(exercise.ExerciseId, candidate.ExerciseId, StringComparison.OrdinalIgnoreCase)))
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Score = AccessoryDeficitScore(candidate, currentPrimaryVolume, targets, allowedMuscles)
                })
                .Where(item => item.Score > 0m)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Candidate.SelectionPriority)
                .ThenBy(item => item.Candidate.ExerciseId, StringComparer.Ordinal)
                .ToArray();

            var selected = false;
            foreach (var item in candidates)
            {
                var candidate = item.Candidate;
                var (sets, minReps, maxReps, rir, rest, progression) = PrescriptionFor(
                    request.Goal, request.Experience, week, daysPerWeek, TrainingMovementPattern.Accessory);
                var underTargetMuscles = candidate.PrimaryMuscleGroups.Distinct()
                    .Where(muscle => currentPrimaryVolume.GetValueOrDefault(muscle) < targets.GetValueOrDefault(muscle))
                    .ToArray();
                var prescription = new TrainingExercisePrescription(
                    candidate.ExerciseId,
                    candidate.CatalogVersion,
                    candidate.Name,
                    currentExercises.Count + 1,
                    sets,
                    minReps,
                    maxReps,
                    rir,
                    rest,
                    progression,
                    $"Accesorio seleccionado para atender el déficit primario de {string.Join(", ", underTargetMuscles)} sin sustituir los patrones básicos; metadatos {(candidate.IsReviewed ? "revisados" : "aportados por el profesional propietario")}.",
                    candidate.PrimaryMuscleGroups.ToArray(),
                    candidate.SecondaryMuscleGroups.ToArray());

                var durationSession = new TrainingSessionPrescription(
                    week,
                    day,
                    title,
                    WarmupAndTransitionsMinutes,
                    currentExercises.Append(prescription).Select(exercise => exercise with { Sets = 1 }).ToArray());
                if (TrainingProgramVolumeAllocator.EstimateDurationSeconds(durationSession) >
                    request.MaximumSessionDurationMinutes * 60m)
                    continue;

                currentExercises.Add(prescription);
                selected = true;
                break;
            }

            if (!selected)
                break;
        }
    }

    private static readonly HashSet<TrainingMuscleGroup> UpperBodyAccessoryMuscles =
    [
        TrainingMuscleGroup.Chest, TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack,
        TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Triceps,
        TrainingMuscleGroup.Forearms, TrainingMuscleGroup.Serratus, TrainingMuscleGroup.RotatorCuff
    ];

    private static readonly HashSet<TrainingMuscleGroup> LowerBodyAccessoryMuscles =
    [
        TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Hamstrings,
        TrainingMuscleGroup.Calves, TrainingMuscleGroup.Adductors, TrainingMuscleGroup.Abductors,
        TrainingMuscleGroup.HipFlexors, TrainingMuscleGroup.TibialisAnterior, TrainingMuscleGroup.ErectorSpinae
    ];

    private static decimal AccessoryDeficitScore(
        TrainingExerciseCandidate candidate,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> currentPrimaryVolume,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> targets,
        IReadOnlySet<TrainingMuscleGroup>? allowedMuscles)
    {
        var muscles = candidate.PrimaryMuscleGroups.Distinct()
            .Where(muscle => targets.ContainsKey(muscle) &&
                             (allowedMuscles is null || allowedMuscles.Contains(muscle)))
            .ToArray();
        if (muscles.Length == 0) return 0m;
        return muscles.Average(muscle =>
        {
            var deficit = Math.Max(0m, targets[muscle] - currentPrimaryVolume.GetValueOrDefault(muscle));
            var importance = muscle is TrainingMuscleGroup.Chest or TrainingMuscleGroup.Lats or
                TrainingMuscleGroup.UpperBack or TrainingMuscleGroup.Deltoids or TrainingMuscleGroup.Biceps or
                TrainingMuscleGroup.Triceps or TrainingMuscleGroup.Glutes or TrainingMuscleGroup.Quadriceps or
                TrainingMuscleGroup.Hamstrings or TrainingMuscleGroup.Calves ? 1m : 0.6m;
            return deficit * importance;
        });
    }

    private static IReadOnlyList<SessionTemplate> BuildSplit(int daysPerWeek)
    {
        var fullBodyA = new SessionTemplate("Cuerpo completo A",
            [TrainingMovementPattern.Squat, TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.Hinge, TrainingMovementPattern.Core]);
        var fullBodyB = new SessionTemplate("Cuerpo completo B",
            [TrainingMovementPattern.Hinge, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.VerticalPull, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core]);

        return daysPerWeek switch
        {
            2 => [fullBodyA, fullBodyB],
            3 => [fullBodyA, fullBodyB, fullBodyA with { Title = "Cuerpo completo C" }],
            4 =>
            [
                new SessionTemplate("Tren superior A", [TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.VerticalPull, TrainingMovementPattern.Core]),
                new SessionTemplate("Tren inferior A", [TrainingMovementPattern.Squat, TrainingMovementPattern.Hinge, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core]),
                new SessionTemplate("Tren superior B", [TrainingMovementPattern.HorizontalPush, TrainingMovementPattern.HorizontalPull, TrainingMovementPattern.VerticalPush, TrainingMovementPattern.VerticalPull, TrainingMovementPattern.Core]),
                new SessionTemplate("Tren inferior B", [TrainingMovementPattern.Squat, TrainingMovementPattern.Hinge, TrainingMovementPattern.SingleLeg, TrainingMovementPattern.Core])
            ],
            _ => []
        };
    }

    /// <summary>
    /// Keep rep ranges and effort targets stable through the mesocycle. Weekly set distribution
    /// is handled by TrainingProgramVolumeAllocator. Deload planning is a separate, explicit
    /// mesocycle decision; this prescription method never imposes a fixed calendar deload.
    /// </summary>
    private static (int Sets, int MinReps, int MaxReps, decimal Rir, int RestSeconds, TrainingProgressionRule Progression)
        PrescriptionFor(TrainingGoal goal, TrainingExperience experience, int week, int daysPerWeek, TrainingMovementPattern pattern)
    {
        var beginner = experience == TrainingExperience.Beginner;
        if (pattern is TrainingMovementPattern.Accessory or TrainingMovementPattern.Core or TrainingMovementPattern.SingleLeg)
            return (1, goal == TrainingGoal.Strength ? 8 : 10, 15,
                beginner ? 3m : 2m, pattern == TrainingMovementPattern.Core ? 60 : 90,
                TrainingProgressionRule.DoubleProgression);
        // Give the main bilateral compound patterns a two-set starting dose. The allocator
        // preserves this baseline and adds volume only where the weekly targets and time allow.
        var baseline = goal switch
        {
            TrainingGoal.Strength => (Sets: 2, MinReps: 5, MaxReps: 8, Rir: beginner ? 3m : 2m, RestSeconds: 180),
            TrainingGoal.Hypertrophy => (Sets: 2, MinReps: 8, MaxReps: 12, Rir: beginner ? 3m : 2m, RestSeconds: 120),
            _ => (Sets: 2, MinReps: 8, MaxReps: 15, Rir: 3m, RestSeconds: 90)
        };
        return (baseline.Sets, baseline.MinReps, baseline.MaxReps, baseline.Rir,
            baseline.RestSeconds, TrainingProgressionRule.DoubleProgression);
    }

    private sealed record SessionTemplate(string Title, IReadOnlyList<TrainingMovementPattern> Patterns);
}

public sealed record TrainingProgramGenerationRequest(
    TrainingGoal Goal,
    TrainingExperience Experience,
    DateOnly StartDate,
    int DurationWeeks,
    IReadOnlyList<DayOfWeek> TrainingDays,
    int MaximumSessionDurationMinutes,
    IReadOnlyList<string> AvailableEquipment,
    IReadOnlyList<string> ExcludedExerciseIds,
    IReadOnlyList<TrainingMuscleVolumeTarget>? MuscleVolumeTargets = null,
    bool IsDeload = false);

public sealed record TrainingExerciseCandidate(
    string ExerciseId,
    string CatalogVersion,
    string Name,
    TrainingMovementPattern MovementPattern,
    IReadOnlyList<string> RequiredEquipment,
    IReadOnlyList<TrainingGoal> SuitableGoals,
    TrainingExperience MinimumExperience,
    int SelectionPriority,
    bool IsReviewed,
    IReadOnlyList<TrainingMuscleGroup> PrimaryMuscleGroups,
    IReadOnlyList<TrainingMuscleGroup> SecondaryMuscleGroups,
    bool IsOwnedByCurrentProfessional = false);

public sealed record TrainingProgramGenerationResult(
    TrainingProgramPrescription? Prescription,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Prescription is not null && Errors.Count == 0;
}

public enum TrainingGoal
{
    GeneralFitness,
    Hypertrophy,
    Strength
}

public enum TrainingExperience
{
    Beginner,
    Intermediate,
    Advanced
}

public enum TrainingMovementPattern
{
    Squat,
    Hinge,
    HorizontalPush,
    VerticalPush,
    HorizontalPull,
    VerticalPull,
    SingleLeg,
    Core,
    Accessory
}
