namespace Anguloso.Server.Logica;

/// <summary>
/// Versioned, structured prescription contract for newly generated training plans.
/// Exercise names and catalog versions are snapshots so later catalog edits cannot silently rewrite history.
/// </summary>
public sealed record TrainingProgramPrescription(
    string EngineVersion,
    string Goal,
    DateOnly StartDate,
    int DurationWeeks,
    IReadOnlyList<TrainingSessionPrescription> Sessions,
    TrainingProgramQualityReport? QualityReport = null);

public sealed record TrainingSessionPrescription(
    int WeekNumber,
    DayOfWeek Day,
    string Title,
    int EstimatedDurationMinutes,
    IReadOnlyList<TrainingExercisePrescription> Exercises);

public sealed record TrainingExercisePrescription(
    string ExerciseId,
    string CatalogVersion,
    string ExerciseNameSnapshot,
    int Order,
    int Sets,
    int MinRepetitions,
    int MaxRepetitions,
    decimal? TargetRepetitionsInReserve,
    int RestSeconds,
    TrainingProgressionRule ProgressionRule,
    string SelectionRationale,
    IReadOnlyList<TrainingMuscleGroup> PrimaryMuscleGroups,
    IReadOnlyList<TrainingMuscleGroup> SecondaryMuscleGroups);

/// <summary>Controlled vocabulary for reviewed exercise muscle metadata.</summary>
public enum TrainingMuscleGroup
{
    Chest,
    Lats,
    UpperBack,
    Deltoids,
    Biceps,
    Triceps,
    Forearms,
    Abdominals,
    Obliques,
    ErectorSpinae,
    Glutes,
    Quadriceps,
    Hamstrings,
    Calves
}

public enum TrainingProgressionRule
{
    None,
    DoubleProgression,
    IncreaseLoadWithinLimits
}

/// <summary>
/// Validates structural invariants before a generated plan can be persisted or presented.
/// It intentionally does not claim that an otherwise valid plan is physiologically optimal.
/// </summary>
public static class TrainingPrescriptionValidator
{
    public static IReadOnlyList<string> Validate(TrainingProgramPrescription? plan)
    {
        var errors = new List<string>();
        if (plan is null)
        {
            errors.Add("El plan es obligatorio.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(plan.EngineVersion) || plan.EngineVersion.Length > 40)
            errors.Add("La versión del motor debe tener entre 1 y 40 caracteres.");
        if (string.IsNullOrWhiteSpace(plan.Goal) || plan.Goal.Length > 80)
            errors.Add("El objetivo debe tener entre 1 y 80 caracteres.");
        if (plan.DurationWeeks is < 1 or > 52)
            errors.Add("La duración del plan debe estar entre 1 y 52 semanas.");
        if (plan.Sessions is null || plan.Sessions.Count == 0)
        {
            errors.Add("El plan debe contener al menos una sesión.");
            return errors;
        }

        var sessionKeys = new HashSet<(int Week, DayOfWeek Day)>();
        for (var sessionIndex = 0; sessionIndex < plan.Sessions.Count; sessionIndex++)
        {
            var session = plan.Sessions[sessionIndex];
            var prefix = $"Sesión {sessionIndex + 1}";

            if (session.WeekNumber < 1 || session.WeekNumber > plan.DurationWeeks)
                errors.Add($"{prefix}: la semana debe estar dentro de la duración del plan.");
            if (!Enum.IsDefined(session.Day))
                errors.Add($"{prefix}: el día de la semana no es válido.");
            if (!sessionKeys.Add((session.WeekNumber, session.Day)))
                errors.Add($"{prefix}: hay más de una sesión asignada al mismo día de esa semana.");
            if (string.IsNullOrWhiteSpace(session.Title) || session.Title.Length > 160)
                errors.Add($"{prefix}: el título debe tener entre 1 y 160 caracteres.");
            if (session.EstimatedDurationMinutes is < 1 or > 180)
                errors.Add($"{prefix}: la duración estimada debe estar entre 1 y 180 minutos.");
            if (session.Exercises is null || session.Exercises.Count == 0)
            {
                errors.Add($"{prefix}: debe contener al menos un ejercicio.");
                continue;
            }
            if (session.Exercises.Count > 20)
                errors.Add($"{prefix}: no puede contener más de 20 ejercicios.");

            var exerciseOrders = new HashSet<int>();
            for (var exerciseIndex = 0; exerciseIndex < session.Exercises.Count; exerciseIndex++)
            {
                var exercise = session.Exercises[exerciseIndex];
                var exercisePrefix = $"{prefix}, ejercicio {exerciseIndex + 1}";

                if (string.IsNullOrWhiteSpace(exercise.ExerciseId) || exercise.ExerciseId.Length > 100)
                    errors.Add($"{exercisePrefix}: el identificador estable del ejercicio es obligatorio (máximo 100 caracteres).");
                if (string.IsNullOrWhiteSpace(exercise.CatalogVersion) || exercise.CatalogVersion.Length > 40)
                    errors.Add($"{exercisePrefix}: la versión del catálogo es obligatoria (máximo 40 caracteres).");
                if (string.IsNullOrWhiteSpace(exercise.ExerciseNameSnapshot) || exercise.ExerciseNameSnapshot.Length > 160)
                    errors.Add($"{exercisePrefix}: el nombre histórico del ejercicio es obligatorio (máximo 160 caracteres).");
                if (exercise.Order < 1 || !exerciseOrders.Add(exercise.Order))
                    errors.Add($"{exercisePrefix}: el orden debe ser positivo y no repetirse.");
                if (exercise.Sets is < 1 or > 10)
                    errors.Add($"{exercisePrefix}: las series deben estar entre 1 y 10.");
                if (exercise.MinRepetitions is < 1 or > 30 ||
                    exercise.MaxRepetitions is < 1 or > 30 ||
                    exercise.MinRepetitions > exercise.MaxRepetitions)
                    errors.Add($"{exercisePrefix}: el rango de repeticiones debe ser válido entre 1 y 30.");
                if (exercise.TargetRepetitionsInReserve is < 0 or > 5)
                    errors.Add($"{exercisePrefix}: el RIR objetivo debe estar entre 0 y 5.");
                if (exercise.RestSeconds is < 0 or > 600)
                    errors.Add($"{exercisePrefix}: el descanso debe estar entre 0 y 600 segundos.");
                if (!Enum.IsDefined(exercise.ProgressionRule))
                    errors.Add($"{exercisePrefix}: la regla de progresión no es válida.");
                if (string.IsNullOrWhiteSpace(exercise.SelectionRationale) || exercise.SelectionRationale.Length > 500)
                    errors.Add($"{exercisePrefix}: debe documentarse el motivo de selección (máximo 500 caracteres).");
                if (exercise.PrimaryMuscleGroups is null || exercise.PrimaryMuscleGroups.Count == 0)
                    errors.Add($"{exercisePrefix}: debe declarar al menos un grupo muscular principal revisado.");
                if (exercise.SecondaryMuscleGroups is null)
                    errors.Add($"{exercisePrefix}: la lista de grupos musculares secundarios es obligatoria, aunque esté vacía.");
                if (exercise.PrimaryMuscleGroups is not null)
                {
                    if (exercise.PrimaryMuscleGroups.Any(muscle => !Enum.IsDefined(muscle)))
                        errors.Add($"{exercisePrefix}: contiene un grupo muscular principal no válido.");
                    if (exercise.PrimaryMuscleGroups.Distinct().Count() != exercise.PrimaryMuscleGroups.Count)
                        errors.Add($"{exercisePrefix}: repite grupos musculares principales.");
                }
                if (exercise.SecondaryMuscleGroups is not null)
                {
                    if (exercise.SecondaryMuscleGroups.Any(muscle => !Enum.IsDefined(muscle)))
                        errors.Add($"{exercisePrefix}: contiene un grupo muscular secundario no válido.");
                    if (exercise.SecondaryMuscleGroups.Distinct().Count() != exercise.SecondaryMuscleGroups.Count)
                        errors.Add($"{exercisePrefix}: repite grupos musculares secundarios.");
                    if (exercise.PrimaryMuscleGroups is not null &&
                        exercise.PrimaryMuscleGroups.Intersect(exercise.SecondaryMuscleGroups).Any())
                        errors.Add($"{exercisePrefix}: un grupo muscular no puede ser principal y secundario a la vez.");
                }
            }
        }

        return errors;
    }
}
