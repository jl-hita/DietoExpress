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
    private const decimal WeeklyMinutesForFullDefaultVolume = 180m;
    private const decimal MinimumDefaultVolumeTimeFactor = 0.65m;

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

        var sessionsPerWeek = sessions.GroupBy(session => session.WeekNumber)
            .Select(group => group.Count())
            .DefaultIfEmpty(3)
            .Max();
        var defaultVolumeTimeFactor = DefaultVolumeTimeFactor(sessionsPerWeek, maximumSessionDurationMinutes);
        var targets = BuildTargets(goal, experience, preferences, sessionsPerWeek, maximumSessionDurationMinutes);
        var warnings = new List<string>();
        var explicitTargetMuscles = (preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            .Where(preference => preference.TargetWeeklyPrimarySets is not null)
            .Select(preference => preference.MuscleGroup)
            .ToHashSet();
        var hasDefaultTargetMuscles = sessions.SelectMany(session => session.Exercises)
            .SelectMany(exercise => exercise.PrimaryMuscleGroups)
            .Any(muscle => !explicitTargetMuscles.Contains(muscle));
        if (defaultVolumeTimeFactor < 1m && hasDefaultTargetMuscles)
            warnings.Add($"Los objetivos predeterminados de series se ajustan de forma conservadora al tiempo semanal disponible ({sessionsPerWeek} sesiones × {maximumSessionDurationMinutes} min; factor {defaultVolumeTimeFactor:0.##}). Es una heurística de planificación, no una dosis fisiológica establecida por ACSM; los objetivos explícitos del profesional no se reducen.");
        var output = sessions.ToDictionary(
            session => (session.WeekNumber, session.Day),
            session => session with
            {
                Exercises = session.Exercises.Select(exercise => exercise with
                {
                    Sets = Math.Clamp(exercise.Sets, MinimumSetsPerExercise, MaximumSetsPerExercise)
                }).ToArray()
            });
        foreach (var week in output.Keys.Select(key => key.WeekNumber).Distinct().Order())
        {
            var weekKeys = output.Keys.Where(key => key.WeekNumber == week).OrderBy(key => key.Day).ToArray();
            var weekSessions = weekKeys.Select(key => output[key]).ToArray();
            var primaryCoveredMuscles = weekSessions.SelectMany(x => x.Exercises)
                .SelectMany(x => x.PrimaryMuscleGroups)
                .Distinct()
                .ToArray();
            // Calves are a priority group even when the current draft omitted direct work:
            // absence must be reported rather than silently removing the target from evaluation.
            var applicableTargets = targets.Where(target =>
                    primaryCoveredMuscles.Contains(target.Key) || target.Key == TrainingMuscleGroup.Calves)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var primaryVolume = CalculatePrimaryVolume(weekSessions);

            foreach (var muscle in applicableTargets.Keys.Where(muscle => !primaryCoveredMuscles.Contains(muscle)))
                warnings.Add($"Semana {week}: no hay ningún ejercicio aprobado que trabaje {muscle} como músculo primario; no se inventa una cobertura indirecta para cumplir su prioridad.");

            foreach (var preference in preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            {
                if (!applicableTargets.ContainsKey(preference.MuscleGroup) &&
                    !primaryCoveredMuscles.Contains(preference.MuscleGroup))
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
                if (!primaryCoveredMuscles.Contains(muscle))
                    continue;

                var achieved = primaryVolume.GetValueOrDefault(muscle);
                if (achieved + 0.001m < target)
                    warnings.Add($"Semana {week}: {muscle} alcanza {achieved:0.##} series primarias frente al objetivo orientativo de {target:0.##}; no se fuerza más volumen porque no queda una opción compatible de ejercicio, material y tiempo que permita añadir series sin superar los límites de equilibrio muscular, series por ejercicio o duración.");
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

        // A compound may contribute one additional set when another primary muscle has
        // just reached its target, but must not keep accumulating volume once any primary
        // muscle is already above target. This small buffer prevents a rigid all-or-nothing
        // cutoff without allowing large glute/hamstring overshoots to chase another deficit.
        if (primaryMuscles.Any(muscle =>
                primaryVolume.GetValueOrDefault(muscle) > targets[muscle]))
            return 0m;

        var normalizedDeficit = primaryMuscles.Average(muscle =>
            Math.Max(0m, targets[muscle] - primaryVolume.GetValueOrDefault(muscle)) / targets[muscle]);
        return normalizedDeficit > 0m ? normalizedDeficit : 0m;
    }

    internal static IReadOnlyDictionary<TrainingMuscleGroup, decimal> GetWeeklyTargets(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        int sessionsPerWeek = 3,
        int maximumSessionDurationMinutes = 60) =>
        BuildTargets(goal, experience, preferences, sessionsPerWeek, maximumSessionDurationMinutes);

    internal static decimal EstimateDurationSeconds(TrainingSessionPrescription session) =>
        EstimateDuration(session);

    private static Dictionary<TrainingMuscleGroup, decimal> BuildTargets(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences,
        int sessionsPerWeek,
        int maximumSessionDurationMinutes)
    {
        var baseBaseline = goal switch
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
        var baseline = Math.Max(1m, Math.Ceiling(
            baseBaseline * DefaultVolumeTimeFactor(sessionsPerWeek, maximumSessionDurationMinutes)));

        // Do not assign a universal weekly set quota to every anatomical label. The default
        // dose applies to the main muscle groups typically trained in general strength/hypertrophy
        // programs. Smaller/accessory groups (e.g. hip flexors, obliques, adductors, abductors,
        // serratus, tibialis anterior and rotator cuff) receive direct volume when the professional
        // explicitly prioritizes them, rather than creating automatic deficits in every plan.
        var primaryTargetMuscles = new[]
        {
            TrainingMuscleGroup.Chest,
            TrainingMuscleGroup.Lats,
            TrainingMuscleGroup.UpperBack,
            TrainingMuscleGroup.Deltoids,
            TrainingMuscleGroup.Glutes,
            TrainingMuscleGroup.Quadriceps,
            TrainingMuscleGroup.Hamstrings,
            TrainingMuscleGroup.Calves
        };
        // Direct work for smaller muscles is supplemental to compound pressing/pulling and
        // receives a lower default dose. Professionals can raise it explicitly with priorities.
        var supplementalTargetMuscles = new[]
        {
            TrainingMuscleGroup.Biceps,
            TrainingMuscleGroup.Triceps,
            TrainingMuscleGroup.Abdominals
        };
        var targets = primaryTargetMuscles.ToDictionary(muscle => muscle, _ => baseline);
        // Hamstrings remain trained through hinges and knee-flexion options when available,
        // but are a secondary default priority: do not let their nominal target displace
        // quadriceps, glutes or calves in constrained plans. Professionals can override it.
        targets[TrainingMuscleGroup.Hamstrings] = Math.Max(2m, Math.Ceiling(baseline * 0.5m));
        var supplementalBaseline = Math.Max(1m, Math.Ceiling(baseline / 2m));
        foreach (var muscle in supplementalTargetMuscles)
            targets[muscle] = supplementalBaseline;
        foreach (var preference in preferences ?? Array.Empty<TrainingMuscleVolumeTarget>())
            targets[preference.MuscleGroup] = preference.TargetWeeklyPrimarySets ?? baseline + preference.Priority * 2m;
        return targets;
    }

    private static decimal DefaultVolumeTimeFactor(int sessionsPerWeek, int maximumSessionDurationMinutes)
    {
        // The generator requires at least two weekly sessions. A single-session allocation is
        // also used for isolated allocator tests/drafts, so it cannot reliably represent a
        // complete weekly time budget and must retain the unscaled baseline.
        if (sessionsPerWeek < 2)
            return 1m;

        var weeklyMinutes = Math.Max(0, sessionsPerWeek) * Math.Max(0, maximumSessionDurationMinutes);
        return Math.Clamp(weeklyMinutes / WeeklyMinutesForFullDefaultVolume,
            MinimumDefaultVolumeTimeFactor, 1m);
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
