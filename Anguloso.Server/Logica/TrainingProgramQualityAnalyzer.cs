namespace Anguloso.Server.Logica;

/// <summary>
/// Produces non-blocking, transparent quality diagnostics from reviewed muscle metadata.
/// The counts are programming estimates, not clinical validation or universally optimal targets.
/// </summary>
public static class TrainingProgramQualityAnalyzer
{
    private const int HighPrimarySetWarningThreshold = 20;
    private const decimal HighEstimatedEffectiveSetWarningThreshold = 20m;

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

                // Secondary-set credit is a transparent 0.5 bookkeeping heuristic, not an
                // evidence-established equivalence. Keep raw primary/secondary counts as well.
                var estimatedEffectiveSets = primarySets + secondarySets * TrainingProgramVolumeAllocator.SecondarySetCredit;
                muscles.Add(new TrainingMuscleVolume(muscle, primarySets, secondarySets,
                    primaryFrequency, secondaryFrequency, estimatedEffectiveSets));
                if (primarySets > HighPrimarySetWarningThreshold)
                    warnings.Add($"Semana {week}: {muscle} acumula {primarySets} series primarias; revisa el volumen y la recuperación.");
                if (estimatedEffectiveSets > HighEstimatedEffectiveSetWarningThreshold)
                    warnings.Add($"Semana {week}: {muscle} alcanza una estimación de volumen efectivo de {estimatedEffectiveSets:0.#} series (primarias {primarySets}, secundarias {secondarySets}); revisa la carga total. El crédito secundario es una heurística, no una equivalencia fisiológica.");
            }

            // Zero exposure for an accessory or stabilizing muscle is not automatically a
            // programming defect. Whether adductors, tibialis anterior, serratus, rotator cuff,
            // etc. need direct work depends on the goal, exercise selection, user priorities and
            // available time. Keep all raw zero values in the report, but do not turn every
            // untrained anatomical group into a quality warning.

            weeklyVolumes.Add(new TrainingWeeklyMuscleVolume(week, muscles));
        }

        return new TrainingProgramQualityReport(
            "Estimación basada en series prescritas y metadatos revisados. Las series primarias cuentan 1 y las secundarias 0,5 para una estimación orientativa; ese crédito secundario es una heurística de software, no una equivalencia fisiológica validada.",
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
