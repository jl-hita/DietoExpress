namespace Anguloso.Server.Logica;

/// <summary>
/// Allocates weekly working sets across the selected exercise schedule. Primary muscles receive
/// full credit and secondary muscles half credit; the latter is an explicit bookkeeping heuristic,
/// not a physiological equivalence or an ACSM prescription.
/// </summary>
public static class TrainingProgramVolumeAllocator
{
    public const decimal SecondarySetCredit = 0.5m;
    private const int MinimumSetsPerExercise = 1;
    private const int MaximumSetsPerExercise = 5;
    private const decimal SecondsPerRepEstimate = 2.5m;
    private const int TransitionSecondsPerExercise = 45;
    private const int SessionOverheadMinutes = 5;

    public static TrainingVolumeAllocationResult Allocate(
        IReadOnlyList<TrainingSessionPrescription> sessions,
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        int maximumSessionDurationMinutes)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var errors = ValidatePreferences(preferences);
        if (errors.Count > 0)
            return new(sessions, Array.Empty<string>(), errors);

        var targets = BuildTargets(goal, experience, preferences);
        var output = sessions.ToDictionary(
            session => (session.WeekNumber, session.Day),
            session => session with
            {
                Exercises = session.Exercises.Select(exercise => exercise with { Sets = MinimumSetsPerExercise }).ToArray()
            });
        var warnings = new List<string>();

        foreach (var week in output.Keys.Select(key => key.WeekNumber).Distinct().Order())
        {
            var weekKeys = output.Keys.Where(key => key.WeekNumber == week).OrderBy(key => key.Day).ToArray();
            var weekSessions = weekKeys.Select(key => output[key]).ToArray();
            var primaryCoveredMuscles = weekSessions.SelectMany(x => x.Exercises)
                .SelectMany(x => x.PrimaryMuscleGroups)
                .Distinct()
                .ToArray();
            var applicableTargets = targets.Where(target => primaryCoveredMuscles.Contains(target.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var primaryVolume = CalculatePrimaryVolume(weekSessions);

            foreach (var preference in preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            {
                if (!primaryCoveredMuscles.Contains(preference.MuscleGroup))
                    warnings.Add($"Semana {week}: no hay ningún ejercicio aprobado que trabaje {preference.MuscleGroup} como músculo primario; no se inventa una cobertura indirecta para cumplir su prioridad.");
            }

            // Greedy allocation chooses the exercise that best addresses the largest remaining
            // normalized muscle deficit, while respecting per-exercise and per-session limits.
            // A set on a multi-primary exercise contributes to each reviewed primary muscle;
            // no extra credit is given merely because an exercise lists more muscle labels.
            while (true)
            {
                AllocationCandidate? best = null;
                for (var sessionIndex = 0; sessionIndex < weekSessions.Length; sessionIndex++)
                {
                    var session = weekSessions[sessionIndex];
                    var duration = EstimateDuration(session);
                    for (var exerciseIndex = 0; exerciseIndex < session.Exercises.Count; exerciseIndex++)
                    {
                        var exercise = session.Exercises[exerciseIndex];
                        if (exercise.Sets >= MaximumSetsPerExercise)
                            continue;
                        var addedDuration = AdditionalSetSeconds(exercise);
                        if (duration + addedDuration > maximumSessionDurationMinutes * 60m)
                            continue;

                        var score = DeficitScore(exercise, primaryVolume, applicableTargets);
                        if (score <= 0m)
                            continue;
                        var localPrimaryVolume = exercise.PrimaryMuscleGroups.Distinct()
                            .Select(muscle => session.Exercises
                                .Where(item => item.PrimaryMuscleGroups.Contains(muscle))
                                .Sum(item => (decimal)item.Sets))
                            .DefaultIfEmpty(0m)
                            .Average();
                        var candidate = new AllocationCandidate(sessionIndex, exerciseIndex, score, localPrimaryVolume);
                        if (best is null || candidate.Score > best.Score ||
                            (candidate.Score == best.Score && candidate.LocalPrimaryVolume < best.LocalPrimaryVolume))
                            best = candidate;
                    }
                }

                if (best is null)
                    break;

                var selectedSession = weekSessions[best.SessionIndex];
                var exercises = selectedSession.Exercises.ToArray();
                exercises[best.ExerciseIndex] = exercises[best.ExerciseIndex] with
                {
                    Sets = exercises[best.ExerciseIndex].Sets + 1,
                    SelectionRationale = AppendAllocationRationale(exercises[best.ExerciseIndex].SelectionRationale)
                };
                weekSessions[best.SessionIndex] = selectedSession with { Exercises = exercises };
                primaryVolume = CalculatePrimaryVolume(weekSessions);
            }

            foreach (var (muscle, target) in applicableTargets.OrderBy(x => x.Key))
            {
                var achieved = primaryVolume.GetValueOrDefault(muscle);
                if (achieved + 0.001m < target)
                    warnings.Add($"Semana {week}: {muscle} alcanza {achieved:0.##} series primarias frente al objetivo orientativo de {target:0.##}; no se fuerza más volumen porque se alcanzó el límite de series o duración disponible.");
            }

            for (var index = 0; index < weekKeys.Length; index++)
            {
                var session = weekSessions[index];
                var durationMinutes = (int)Math.Ceiling(EstimateDuration(session) / 60m);
                if (durationMinutes > maximumSessionDurationMinutes)
                    return new(sessions, warnings,
                        [$"La asignación de volumen hace que '{session.Title}' requiera {durationMinutes} minutos y supere el límite de {maximumSessionDurationMinutes}."]);
                output[weekKeys[index]] = session with { EstimatedDurationMinutes = durationMinutes };
            }
        }

        return new(output.Values.OrderBy(x => x.WeekNumber).ThenBy(x => x.Day).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray(), Array.Empty<string>());
    }

    public static IReadOnlyDictionary<TrainingMuscleGroup, decimal> CalculateEffectiveVolume(
        IEnumerable<TrainingSessionPrescription> sessions)
    {
        var volume = new Dictionary<TrainingMuscleGroup, decimal>();
        foreach (var exercise in sessions.SelectMany(x => x.Exercises))
        {
            foreach (var muscle in exercise.PrimaryMuscleGroups.Distinct())
                volume[muscle] = volume.GetValueOrDefault(muscle) + exercise.Sets;
            foreach (var muscle in exercise.SecondaryMuscleGroups.Distinct())
                volume[muscle] = volume.GetValueOrDefault(muscle) + exercise.Sets * SecondarySetCredit;
        }
        return volume;
    }

    public static IReadOnlyDictionary<TrainingMuscleGroup, decimal> CalculatePrimaryVolume(
        IEnumerable<TrainingSessionPrescription> sessions)
    {
        var volume = new Dictionary<TrainingMuscleGroup, decimal>();
        foreach (var exercise in sessions.SelectMany(x => x.Exercises))
        foreach (var muscle in exercise.PrimaryMuscleGroups.Distinct())
            volume[muscle] = volume.GetValueOrDefault(muscle) + exercise.Sets;
        return volume;
    }

    private static decimal DeficitScore(
        TrainingExercisePrescription exercise,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> primaryVolume,
        IReadOnlyDictionary<TrainingMuscleGroup, decimal> targets)
    {
        // Targets refer to direct primary sets. Do not add another compound set when one
        // of its primary muscles has already reached its target; prefer an alternative
        // exercise, or report the remaining deficit if no balanced option exists.
        var primaryMuscles = exercise.PrimaryMuscleGroups.Distinct()
            .Where(targets.ContainsKey)
            .ToArray();
        if (primaryMuscles.Length == 0)
            return 0m;

        // A compound movement can still be useful for an under-target primary muscle when
        // another primary muscle has already reached its target. The previous all-or-nothing
        // guard discarded that candidate entirely, creating avoidable deficits (for example,
        // quadriceps work blocked because glutes had already reached their target).
        var normalizedDeficit = primaryMuscles.Average(muscle =>
            Math.Max(0m, targets[muscle] - primaryVolume.GetValueOrDefault(muscle)) / targets[muscle]);
        if (normalizedDeficit <= 0m)
            return 0m;

        // Averaging across all primary muscles naturally ranks a focused isolation above
        // a compound when both address the same deficit, while still allowing the compound
        // to contribute if no more selective candidate can close the remaining gap.
        return normalizedDeficit;
    }

    internal static IReadOnlyDictionary<TrainingMuscleGroup, decimal> GetWeeklyTargets(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences) =>
        BuildTargets(goal, experience, preferences);

    internal static decimal EstimateDurationSeconds(TrainingSessionPrescription session) =>
        EstimateDuration(session);

    private static Dictionary<TrainingMuscleGroup, decimal> BuildTargets(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences)
    {
        var baseline = goal switch
        {
            TrainingGoal.Hypertrophy => experience switch
            {
                TrainingExperience.Beginner => 6m,
                TrainingExperience.Intermediate => 8m,
                _ => 10m
            },
            TrainingGoal.Strength => experience == TrainingExperience.Beginner ? 6m : 8m,
            _ => experience == TrainingExperience.Beginner ? 4m : 6m
        };

        var targets = Enum.GetValues<TrainingMuscleGroup>()
            .ToDictionary(muscle => muscle, _ => baseline);
        foreach (var preference in preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            targets[preference.MuscleGroup] = preference.TargetWeeklyPrimarySets ?? baseline + preference.Priority * 2m;
        return targets;
    }

    private static IReadOnlyList<string> ValidatePreferences(IReadOnlyList<TrainingMuscleVolumeTarget>? preferences)
    {
        if (preferences is null) return Array.Empty<string>();
        var errors = new List<string>();
        var seen = new HashSet<TrainingMuscleGroup>();
        foreach (var preference in preferences)
        {
            if (!Enum.IsDefined(preference.MuscleGroup))
                errors.Add("La preferencia contiene un grupo muscular no válido.");
            else if (!seen.Add(preference.MuscleGroup))
                errors.Add($"La preferencia repite el grupo muscular {preference.MuscleGroup}.");
            if (preference.Priority is < 0 or > 3)
                errors.Add($"La prioridad de {preference.MuscleGroup} debe estar entre 0 y 3.");
            if (preference.TargetWeeklyPrimarySets is { } target &&
                (target is < 1 or > 20 || target != decimal.Truncate(target)))
                errors.Add($"El objetivo semanal de {preference.MuscleGroup} debe ser un entero entre 1 y 20 series primarias.");
        }
        return errors;
    }

    private static decimal EstimateDuration(TrainingSessionPrescription session)
    {
        var seconds = SessionOverheadMinutes * 60m;
        foreach (var exercise in session.Exercises)
        {
            var averageRepetitions = (exercise.MinRepetitions + exercise.MaxRepetitions) / 2m;
            seconds += exercise.Sets * averageRepetitions * SecondsPerRepEstimate;
            seconds += Math.Max(0, exercise.Sets - 1) * exercise.RestSeconds;
            seconds += TransitionSecondsPerExercise;
        }
        return seconds;
    }

    private static decimal AdditionalSetSeconds(TrainingExercisePrescription exercise) =>
        ((exercise.MinRepetitions + exercise.MaxRepetitions) / 2m) * SecondsPerRepEstimate + exercise.RestSeconds;

    private static string AppendAllocationRationale(string rationale)
    {
        const string marker = "volumen semanal individualizado";
        return rationale.Contains(marker, StringComparison.OrdinalIgnoreCase)
            ? rationale
            : $"{rationale} Ajuste de series por {marker} (motor 0.8.0).";
    }

    private sealed record AllocationCandidate(
        int SessionIndex, int ExerciseIndex, decimal Score, decimal LocalPrimaryVolume);
}

public sealed record TrainingMuscleVolumeTarget(
    TrainingMuscleGroup MuscleGroup,
    int Priority = 0,
    decimal? TargetWeeklyPrimarySets = null);

public sealed record TrainingVolumeAllocationResult(
    IReadOnlyList<TrainingSessionPrescription> Sessions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}
