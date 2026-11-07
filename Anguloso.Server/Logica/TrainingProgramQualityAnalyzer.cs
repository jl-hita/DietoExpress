namespace Anguloso.Server.Logica;

/// <summary>
/// Produces non-blocking, transparent quality diagnostics from reviewed muscle metadata.
/// The counts are programming estimates, not clinical validation or universally optimal targets.
/// </summary>
public static class TrainingProgramQualityAnalyzer
{
    private const int HighEffectiveSetReviewThreshold = 22;

    public static TrainingManualVolumeAnalysisResult AnalyzeManual(
        IReadOnlyCollection<TrainingExerciseCandidate> catalog,
        IReadOnlyList<TrainingManualVolumeSessionInput> sessions)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sessions);
        if (sessions.Count > 7)
            throw new ArgumentOutOfRangeException(nameof(sessions), "Una vista semanal admite como máximo siete días.");
        if (sessions.Any(session => session is null || !Enum.IsDefined(session.Day)))
            throw new ArgumentException("Todas las sesiones deben indicar un día válido.", nameof(sessions));
        if (sessions.Any(session => !string.IsNullOrWhiteSpace(session.Title) && session.Title!.Length > 160))
            throw new ArgumentException("El título de cada sesión admite hasta 160 caracteres.", nameof(sessions));
        if (sessions.Any(session => session.Exercises is null || session.Exercises.Count > 20))
            throw new ArgumentException("Cada sesión admite hasta 20 ejercicios.", nameof(sessions));
        if (sessions.SelectMany(session => session.Exercises).Any(input =>
            input is null || input.Sets is < 1 or > 10 ||
            (input.ExerciseId?.Length ?? 0) > 100 || (input.ExerciseName?.Length ?? 0) > 160))
            throw new ArgumentException("Cada ejercicio debe tener entre 1 y 10 series, un ID de hasta 100 caracteres y un nombre de hasta 160.", nameof(sessions));

        var byId = catalog.ToDictionary(candidate => candidate.ExerciseId, StringComparer.OrdinalIgnoreCase);
        var byName = catalog
            .GroupBy(candidate => NormalizeExerciseName(candidate.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var unclassified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prescriptions = new List<TrainingSessionPrescription>();

        foreach (var dayGroup in sessions.GroupBy(session => session.Day).OrderBy(group => (int)group.Key))
        {
            if (!Enum.IsDefined(dayGroup.Key))
                throw new ArgumentOutOfRangeException(nameof(sessions), "La sesión contiene un día no válido.");

            var exercises = new List<TrainingExercisePrescription>();
            foreach (var input in dayGroup.SelectMany(session => session.Exercises ?? []))
            {
                if (input.Sets is < 1 or > 10)
                    throw new ArgumentOutOfRangeException(nameof(sessions), "Las series de cada ejercicio deben estar entre 1 y 10.");

                TrainingExerciseCandidate? candidate = null;
                if (!string.IsNullOrWhiteSpace(input.ExerciseId))
                    byId.TryGetValue(input.ExerciseId.Trim(), out candidate);
                if (candidate is null && !string.IsNullOrWhiteSpace(input.ExerciseName))
                    byName.TryGetValue(NormalizeExerciseName(input.ExerciseName), out candidate);
                if (candidate is null)
                {
                    if (!string.IsNullOrWhiteSpace(input.ExerciseName))
                        unclassified.Add(input.ExerciseName.Trim());
                    continue;
                }

                exercises.Add(new TrainingExercisePrescription(
                    candidate.ExerciseId,
                    candidate.CatalogVersion,
                    candidate.Name,
                    exercises.Count + 1,
                    input.Sets,
                    8,
                    12,
                    null,
                    90,
                    TrainingProgressionRule.None,
                    "Prescripción manual; metadatos revisados del catálogo.",
                    candidate.PrimaryMuscleGroups,
                    candidate.SecondaryMuscleGroups));
            }

            if (exercises.Count > 0)
                prescriptions.Add(new TrainingSessionPrescription(
                    1, dayGroup.Key, dayGroup.First().Title?.Trim() is { Length: > 0 } title ? title : "Sesión manual",
                    60, exercises));
        }

        var plan = new TrainingProgramPrescription(
            "manual-volume-preview-v1",
            "Manual",
            new DateOnly(2026, 1, 5),
            1,
            prescriptions);
        return new TrainingManualVolumeAnalysisResult(
            Analyze(plan),
            unclassified.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string NormalizeExerciseName(string value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static TrainingProgramQualityReport Analyze(TrainingProgramPrescription plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var weeklyVolumes = new List<TrainingWeeklyMuscleVolume>();
        var warnings = new List<string>();

        for (var week = 1; week <= plan.DurationWeeks; week++)
        {
            var sessions = plan.Sessions.Where(session => session.WeekNumber == week).ToArray();
            var muscles = new List<TrainingMuscleVolume>();

            foreach (var muscle in Enum.GetValues<TrainingMuscleGroup>())
            {
                var primarySets = 0;
                var secondarySets = 0;
                var primaryFrequency = 0;
                var secondaryFrequency = 0;

                foreach (var session in sessions)
                {
                    var primaryInSession = false;
                    var secondaryInSession = false;
                    foreach (var exercise in session.Exercises)
                    {
                        if (exercise.PrimaryMuscleGroups.Contains(muscle))
                        {
                            primarySets += exercise.Sets;
                            primaryInSession = true;
                        }

                        if (exercise.SecondaryMuscleGroups.Contains(muscle))
                        {
                            secondarySets += exercise.Sets;
                            secondaryInSession = true;
                        }
                    }

                    if (primaryInSession) primaryFrequency++;
                    if (secondaryInSession) secondaryFrequency++;
                }

                // Preserve raw primary/secondary counts for traceability; effective volume
                // is calculated separately from the exercise-specific stimulus audit.
                var estimatedEffectiveSets = sessions.SelectMany(session => session.Exercises)
                    .Where(exercise => TrainingExerciseStimulus.GetEffectiveMuscles(
                        exercise.ExerciseId, exercise.PrimaryMuscleGroups, exercise.SecondaryMuscleGroups).Contains(muscle))
                    .Sum(exercise => exercise.Sets);
                muscles.Add(new TrainingMuscleVolume(muscle, primarySets, secondarySets,
                    primaryFrequency, secondaryFrequency, estimatedEffectiveSets));
                if (estimatedEffectiveSets > HighEffectiveSetReviewThreshold)
                    warnings.Add($"Semana {week}: {muscle} acumula {estimatedEffectiveSets:0.##} series con estímulo efectivo estimado; revisa volumen, rendimiento y recuperación.");
            }

            // Zero exposure for an accessory or stabilizing muscle is not automatically a
            // programming defect. Whether adductors, tibialis anterior, serratus, rotator cuff,
            // etc. need direct work depends on the goal, exercise selection, user priorities and
            // available time. Keep all raw zero values in the report, but do not turn every
            // untrained anatomical group into a quality warning.

            weeklyVolumes.Add(new TrainingWeeklyMuscleVolume(week, muscles));
        }

        return new TrainingProgramQualityReport(
            "Estimación basada en series prescritas y una auditoría explícita por ejercicio: cuentan las series de músculos primarios y las contribuciones secundarias con estímulo dinámico relevante. No se contabilizan estabilizadores ni participaciones incidentales. Las contribuciones secundarias no tienen un crédito fraccionario universal.",
            weeklyVolumes,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }
}

public sealed record TrainingProgramQualityReport(
    string Methodology,
    IReadOnlyList<TrainingWeeklyMuscleVolume> WeeklyMuscleVolumes,
    IReadOnlyList<string> Warnings);

public sealed record TrainingWeeklyMuscleVolume(
    int WeekNumber,
    IReadOnlyList<TrainingMuscleVolume> Muscles);

public sealed record TrainingMuscleVolume(
    TrainingMuscleGroup MuscleGroup,
    int PrimarySets,
    int SecondarySets,
    int PrimaryFrequencySessions,
    int SecondaryFrequencySessions,
    decimal EstimatedEffectiveSets = 0m);

public sealed record TrainingManualVolumeSessionInput(
    DayOfWeek Day,
    string? Title,
    IReadOnlyList<TrainingManualVolumeExerciseInput> Exercises);

public sealed record TrainingManualVolumeExerciseInput(
    string? ExerciseId,
    string ExerciseName,
    int Sets);

public sealed record TrainingManualVolumeAnalysisResult(
    TrainingProgramQualityReport QualityReport,
    IReadOnlyList<string> UnclassifiedExerciseNames);

public sealed record TrainingManualVolumePreviewRequest(
    IReadOnlyList<TrainingManualVolumeSessionInput> Sessions);
