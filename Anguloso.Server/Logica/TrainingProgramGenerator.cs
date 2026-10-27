namespace Anguloso.Server.Logica;

/// <summary>
/// First deterministic draft generator. It only selects from explicitly supplied, reviewed catalog metadata.
/// It does not infer exercise properties from names or automatically publish/apply the generated plan.
/// </summary>
public static class TrainingProgramGenerator
{
    private const int MinutesPerExerciseEstimate = 6;
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
            for (var dayIndex = 0; dayIndex < days.Length; dayIndex++)
            {
                var sessionTemplate = split[dayIndex];
                var requiredPatterns = sessionTemplate.Patterns;
                var estimatedDuration = WarmupAndTransitionsMinutes + requiredPatterns.Count * MinutesPerExerciseEstimate;
                if (estimatedDuration > request.MaximumSessionDurationMinutes)
                    return Failure($"La sesión '{sessionTemplate.Title}' requiere aproximadamente {estimatedDuration} minutos y supera el límite solicitado.");

                var prescriptions = new List<TrainingExercisePrescription>();
                for (var patternIndex = 0; patternIndex < requiredPatterns.Count; patternIndex++)
                {
                    var pattern = requiredPatterns[patternIndex];
                    var candidate = catalog
                        .Where(x => x.IsReviewed
                            && x.MovementPattern == pattern
                            && x.SuitableGoals.Contains(request.Goal)
                            && (int)x.MinimumExperience <= (int)request.Experience
                            && x.RequiredEquipment.All(equipment.Contains)
                            && !excluded.Contains(x.ExerciseId))
                        .OrderByDescending(x => x.SelectionPriority)
                        .ThenBy(x => x.ExerciseId, StringComparer.Ordinal)
                        .FirstOrDefault();

                    if (candidate is null)
                        return Failure($"No hay un ejercicio revisado compatible con el patrón '{pattern}', el objetivo, la experiencia, el material y las exclusiones configuradas.");

                    var (sets, minReps, maxReps, rir, rest, progression) = PrescriptionFor(request.Goal, request.Experience, week);
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
                        $"Patrón {pattern}; metadatos revisados; compatible con objetivo, experiencia, material y exclusiones." ));
                }

                sessions.Add(new TrainingSessionPrescription(
                    week,
                    days[dayIndex],
                    sessionTemplate.Title,
                    estimatedDuration,
                    prescriptions));
            }
        }

        var plan = new TrainingProgramPrescription(
            "0.3.0",
            request.Goal.ToString(),
            request.StartDate,
            request.DurationWeeks,
            sessions);
        var validationErrors = TrainingPrescriptionValidator.Validate(plan);
        return validationErrors.Count == 0
            ? new(plan, Array.Empty<string>())
            : new(null, validationErrors);

        TrainingProgramGenerationResult Failure(string error) => new(null, [error]);
    }

    private static IReadOnlyList<string> ValidateRequest(TrainingProgramGenerationRequest request)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(request.Goal)) errors.Add("El objetivo de entrenamiento no es válido.");
        if (!Enum.IsDefined(request.Experience)) errors.Add("El nivel de experiencia no es válido.");
        if (request.DurationWeeks is < 1 or > 52) errors.Add("La duración debe estar entre 1 y 52 semanas.");
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
    /// Conservative four-week microcycle: establish, build, build, deload.
    /// This is a provisional software heuristic, not a universally validated prescription.
    /// </summary>
    private static (int Sets, int MinReps, int MaxReps, decimal Rir, int RestSeconds, TrainingProgressionRule Progression)
        PrescriptionFor(TrainingGoal goal, TrainingExperience experience, int week)
    {
        var beginner = experience == TrainingExperience.Beginner;
        var baseline = goal switch
        {
            TrainingGoal.Strength => (Sets: beginner ? 2 : 3, MinReps: 5, MaxReps: 8, Rir: beginner ? 3m : 2m, RestSeconds: 180),
            TrainingGoal.Hypertrophy => (Sets: beginner ? 2 : 3, MinReps: 8, MaxReps: 12, Rir: beginner ? 3m : 2m, RestSeconds: 120),
            _ => (Sets: 2, MinReps: 8, MaxReps: 15, Rir: 3m, RestSeconds: 90)
        };

        var weekInCycle = ((week - 1) % 4) + 1;
        var sets = weekInCycle switch
        {
            2 or 3 => Math.Min(4, baseline.Sets + 1),
            4 => Math.Max(1, baseline.Sets - 1),
            _ => baseline.Sets
        };
        var rir = weekInCycle == 4 ? Math.Min(5m, baseline.Rir + 1m) : baseline.Rir;

        return (sets, baseline.MinReps, baseline.MaxReps, rir, baseline.RestSeconds, TrainingProgressionRule.DoubleProgression);
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
    IReadOnlyList<string> ExcludedExerciseIds);

public sealed record TrainingExerciseCandidate(
    string ExerciseId,
    string CatalogVersion,
    string Name,
    TrainingMovementPattern MovementPattern,
    IReadOnlyList<string> RequiredEquipment,
    IReadOnlyList<TrainingGoal> SuitableGoals,
    TrainingExperience MinimumExperience,
    int SelectionPriority,
    bool IsReviewed);

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
    Core
}
