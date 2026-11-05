namespace Anguloso.Server.Logica;

/// <summary>
/// Exercise-level double-progression engine. It produces a suggestion for the next exposure;
/// it never edits a prescription, changes completed history, or applies a load automatically.
/// </summary>
public static class TrainingExerciseProgressionEngine
{
    private const int RequiredConsecutiveTopRangeExposures = 2;
    private const decimal DefaultMaximumLoadIncreasePercent = 5m;

    public static TrainingExerciseProgressionRecommendation Evaluate(
        TrainingExerciseProgressionPrescription prescription,
        IReadOnlyList<TrainingExerciseExposure> history)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        ArgumentNullException.ThrowIfNull(history);

        var validation = ValidatePrescription(prescription);
        if (validation is not null)
            return Recommendation(TrainingExerciseProgressionAction.InvalidPrescription, validation);

        if (prescription.ProgressionRule != TrainingProgressionRule.DoubleProgression)
            return Recommendation(TrainingExerciseProgressionAction.NotApplicable,
                "Esta regla de progresión no tiene todavía una semántica validada para recomendaciones automáticas. Mantén la prescripción y solicita revisión profesional.");

        // A pain report is exercise/session safety context, not a performance score.
        // Never let a later pain-free record erase a recent unresolved pain signal.
        if (history.TakeLast(3).Any(x => x.PainReported))
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "Hay una señal reciente de dolor. No progreses este ejercicio hasta revisar el problema y confirmar que es seguro retomarlo.");

        var exposures = history.TakeLast(3).ToArray();

        if (exposures.Length == 0)
            return Recommendation(TrainingExerciseProgressionAction.InsufficientData,
                "Todavía no hay una sesión con series registradas para este ejercicio.");

        // An incomplete latest exposure is meaningful negative context. Do not skip it
        // and accidentally progress from an older, better session.
        if (!exposures[^1].SessionCompleted)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "La última exposición no se completó. Mantén la carga y revisa el motivo antes de progresar.");

        var latest = AnalyzeExposure(prescription, exposures[^1]);
        if (!latest.IsUsable)
            return Recommendation(latest.Invalid ? TrainingExerciseProgressionAction.InvalidData : TrainingExerciseProgressionAction.HoldAndReview,
                latest.Explanation);

        if (exposures[^1].RecoveryInsufficient)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "La recuperación registrada fue insuficiente. Mantén la carga y revisa el contexto antes de progresar.");

        if (latest.BelowMinimumRepetitions)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "No se alcanzó el mínimo del rango prescrito. Mantén la carga y revisa técnica, descanso, fatiga y contexto; no compenses aumentando el esfuerzo.");

        if (latest.BelowTargetRir)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                "El esfuerzo superó el objetivo de RIR. Mantén la carga; no aumentes peso ni repeticiones hasta recuperar el margen prescrito.");

        if (HasStagnated(prescription, exposures))
            return Recommendation(TrainingExerciseProgressionAction.StagnationReview,
                "El rendimiento no mejora en tres exposiciones consecutivas. Antes de cambiar la carga, revisa adherencia, técnica, descansos, recuperación y si el rango/RIR siguen siendo adecuados con tu profesional.");

        if (!latest.AtTopOfRange)
        {
            var nextRepetitions = latest.Sets
                .Select(set => Math.Min(prescription.MaxRepetitions, set.Repetitions!.Value + 1))
                .ToArray();
            return new(
                TrainingExerciseProgressionAction.AddRepetitions,
                "Mantén la carga y añade como máximo una repetición por serie en la próxima exposición, sin superar el rango prescrito ni perder el RIR objetivo.",
                latest.LoadKg,
                null,
                nextRepetitions,
                null);
        }

        // A single good session is not enough to justify a load jump. Requiring two
        // consecutive top-of-range exposures reduces reactions to day-to-day noise.
        var consecutiveTopRange = 1;
        for (var index = exposures.Length - 2; index >= 0; index--)
        {
            var previous = AnalyzeExposure(prescription, exposures[index]);
            if (!exposures[index].SessionCompleted || !previous.IsUsable || previous.BelowTargetRir || previous.BelowMinimumRepetitions ||
                !previous.AtTopOfRange || previous.LoadKg != latest.LoadKg ||
                exposures[index].RecoveryInsufficient || exposures[index].PainReported)
                break;
            consecutiveTopRange++;
        }

        if (consecutiveTopRange < RequiredConsecutiveTopRangeExposures)
            return Recommendation(TrainingExerciseProgressionAction.ConfirmTopOfRange,
                "Has alcanzado el máximo del rango. Repite la carga y confirma el resultado en otra sesión antes de subir peso.");

        var maximumIncreasePercent = prescription.MaximumLoadIncreasePercent ?? DefaultMaximumLoadIncreasePercent;
        var maximumIncrease = decimal.Round(
            latest.LoadKg * maximumIncreasePercent / 100m, 2, MidpointRounding.AwayFromZero);
        if (prescription.SmallestLoadIncrementKg is not > 0)
            return new(
                TrainingExerciseProgressionAction.IncreaseLoad,
                "Dos exposiciones consecutivas cumplen el rango y el RIR. Sube solo el menor incremento disponible del material, sin superar el 5 % de la carga actual; registra el nuevo peso para calibrar futuras recomendaciones.",
                null, null, Enumerable.Repeat(prescription.MinRepetitions, prescription.PlannedWorkingSets).ToArray(),
                DefaultMaximumLoadIncreasePercent);

        var increment = prescription.SmallestLoadIncrementKg.Value;
        if (increment > maximumIncrease)
            return Recommendation(TrainingExerciseProgressionAction.HoldAndReview,
                $"El incremento disponible ({increment:0.##} kg) supera el límite conservador del 5 % ({maximumIncrease:0.##} kg). Mantén la carga o ajusta el material; no fuerces un salto excesivo.");

        var suggestedLoad = decimal.Round(latest.LoadKg + increment, 2, MidpointRounding.AwayFromZero);
        return new(
            TrainingExerciseProgressionAction.IncreaseLoad,
            "Dos exposiciones consecutivas cumplen el rango y el RIR. Aumenta un único incremento mínimo del material y vuelve al extremo inferior del rango de repeticiones.",
            suggestedLoad,
            increment,
            Enumerable.Repeat(prescription.MinRepetitions, prescription.PlannedWorkingSets).ToArray(),
            maximumIncreasePercent);
    }

    private static bool HasStagnated(
        TrainingExerciseProgressionPrescription prescription,
        IReadOnlyList<TrainingExerciseExposure> exposures)
    {
        if (exposures.Count < 3) return false;
        var recent = exposures.TakeLast(3).ToArray();
        if (recent.Any(x => !x.SessionCompleted || x.RecoveryInsufficient || x.PainReported))
            return false;

        var analyses = recent.Select(x => AnalyzeExposure(prescription, x)).ToArray();
        if (analyses.Any(x => !x.IsUsable || x.BelowMinimumRepetitions || x.BelowTargetRir))
            return false;
        if (analyses.Select(x => x.LoadKg).Distinct().Count() != 1)
            return false;

        var totalRepetitions = analyses
            .Select(x => x.Sets.Sum(set => set.Repetitions!.Value))
            .ToArray();
        var noRepetitionProgress = totalRepetitions[1] <= totalRepetitions[0] &&
                                   totalRepetitions[2] <= totalRepetitions[1];

        // RIR estimates are noisy. A change greater than half an RIR is treated as a
        // possible effort improvement; smaller differences do not hide a three-session plateau.
        var allRirRecorded = analyses.All(x => x.Sets.All(set => set.Rir is not null));
        var noRirImprovement = !allRirRecorded || analyses
            .Select(x => x.Sets.Average(set => set.Rir!.Value))
            .Zip(analyses.Skip(1).Select(x => x.Sets.Average(set => set.Rir!.Value)),
                (previous, next) => next <= previous + 0.5m)
            .All(noMeaningfulImprovement => noMeaningfulImprovement);

        return noRepetitionProgress && noRirImprovement;
    }

    private static ExposureAnalysis AnalyzeExposure(
        TrainingExerciseProgressionPrescription prescription,
        TrainingExerciseExposure exposure)
    {
        if (exposure.WorkingSets is null || exposure.WorkingSets.Count != prescription.PlannedWorkingSets)
            return ExposureAnalysis.Failure("El número de series efectivas registradas no coincide con las prescritas.");

        var sets = exposure.WorkingSets;
        if (sets.Any(x => !string.Equals(x.Kind, "working", StringComparison.Ordinal) ||
                          !x.Completed || x.WeightKg is null or <= 0 ||
                          x.Repetitions is null or < 1 or > 100 ||
                          x.Rir is < 0 or > 10))
            return ExposureAnalysis.Failure("Faltan datos válidos de carga, repeticiones o finalización en alguna serie efectiva.");

        var load = sets[0].WeightKg!.Value;
        // The current contract prescribes one working load per exercise. Mixed-load
        // back-off/drop sets are not silently averaged; they need an explicit future model.
        if (sets.Any(x => x.WeightKg != load))
            return ExposureAnalysis.Failure("Las series usan cargas distintas. No se calcula una progresión única hasta que la prescripción modele esas cargas.", invalid: true);

        var belowMinimum = sets.Any(x => x.Repetitions!.Value < prescription.MinRepetitions);
        var belowRir = prescription.TargetRir is not null &&
                       sets.Any(x => x.Rir is null || x.Rir.Value < prescription.TargetRir.Value);
        var atTop = sets.All(x => x.Repetitions!.Value >= prescription.MaxRepetitions);
        return new(true, false, null, load, sets, belowMinimum, belowRir, atTop);
    }

    private static string? ValidatePrescription(TrainingExerciseProgressionPrescription p)
    {
        if (string.IsNullOrWhiteSpace(p.ExerciseId) || p.PlannedWorkingSets is < 1 or > 20 ||
            p.MinRepetitions is < 1 or > 100 || p.MaxRepetitions < p.MinRepetitions ||
            p.MaxRepetitions > 100 || p.TargetRir is < 0 or > 10 ||
            p.SmallestLoadIncrementKg is <= 0 ||
            p.MaximumLoadIncreasePercent is <= 0 or > 10)
            return "La prescripción del ejercicio contiene valores incompletos o fuera de rango.";
        return null;
    }

    private static TrainingExerciseProgressionRecommendation Recommendation(
        TrainingExerciseProgressionAction action, string explanation) =>
        new(action, explanation, null, null, null, null);

    private sealed record ExposureAnalysis(
        bool IsUsable,
        bool Invalid,
        string? Explanation,
        decimal LoadKg,
        IReadOnlyList<TrainingExecutedSet> Sets,
        bool BelowMinimumRepetitions,
        bool BelowTargetRir,
        bool AtTopOfRange)
    {
        public static ExposureAnalysis Failure(string explanation, bool invalid = false) =>
            new(false, invalid, explanation, 0m, Array.Empty<TrainingExecutedSet>(), false, false, false);
    }
}

public sealed record TrainingExerciseProgressionPrescription(
    string ExerciseId,
    int PlannedWorkingSets,
    int MinRepetitions,
    int MaxRepetitions,
    decimal? TargetRir,
    TrainingProgressionRule ProgressionRule,
    decimal? SmallestLoadIncrementKg = null,
    decimal? MaximumLoadIncreasePercent = null);

public sealed record TrainingExerciseExposure(
    IReadOnlyList<TrainingExecutedSet> WorkingSets,
    bool SessionCompleted = true,
    bool RecoveryInsufficient = false,
    bool PainReported = false);

public sealed record TrainingExecutedSet(
    string Kind,
    decimal? WeightKg,
    int? Repetitions,
    decimal? Rir,
    bool Completed);

public enum TrainingExerciseProgressionAction
{
    InvalidPrescription,
    InvalidData,
    InsufficientData,
    NotApplicable,
    HoldAndReview,
    AddRepetitions,
    ConfirmTopOfRange,
    IncreaseLoad,
    StagnationReview
}

public sealed record TrainingExerciseProgressionRecommendation(
    TrainingExerciseProgressionAction Action,
    string Explanation,
    decimal? SuggestedLoadKg,
    decimal? LoadIncreaseKg,
    IReadOnlyList<int>? SuggestedRepetitionsBySet,
    decimal? MaximumLoadIncreasePercent);
