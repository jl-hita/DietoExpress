namespace Anguloso.Server.Logica;

/// <summary>
/// Produces non-blocking, transparent quality diagnostics from reviewed muscle metadata.
/// The counts are programming estimates, not clinical validation or universally optimal targets.
/// </summary>
public static class TrainingProgramQualityAnalyzer
{
    private const int HighEffectiveSetReviewThreshold = 22;

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
