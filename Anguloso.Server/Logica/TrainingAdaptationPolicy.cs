namespace Anguloso.Server.Logica;

/// <summary>
/// Deterministic, conservative decision policy for interpreting training feedback.
/// It returns a recommendation for future sessions; it never edits a plan or diagnoses an injury.
/// Feedback must be supplied oldest-to-newest; the latest item represents the most recent session.
/// </summary>
public static class TrainingAdaptationPolicy
{
    public static TrainingAdaptationDecision Evaluate(IReadOnlyList<TrainingSessionFeedback> recentFeedback)
    {
        ArgumentNullException.ThrowIfNull(recentFeedback);

        if (recentFeedback.Count == 0)
            return Decision(TrainingAdaptationAction.InsufficientData,
                "Todavía no hay registros suficientes para adaptar el entrenamiento.");

        // Invalid telemetry must never be interpreted as evidence for progression or adaptation.
        if (recentFeedback.Any(HasInvalidValues))
            return Decision(TrainingAdaptationAction.InvalidFeedback,
                "Hay datos de entrenamiento no válidos. Corrige los registros antes de evaluar cambios.");

        var latest = recentFeedback[^1];

        // Pain is a safety signal, not a variable to optimize by simply lowering load.
        if (latest.PainSignal is TrainingPainSignal.NewOrWorsening)
            return Decision(TrainingAdaptationAction.PauseAffectedExerciseAndReview,
                "Pausa el ejercicio implicado y solicita valoración profesional antes de reanudarlo.");

        // Count only explicit recovery feedback; unknown values must not be treated as good recovery.
        var recent = recentFeedback.TakeLast(3).ToArray();
        var insufficientRecoveryCount = recent.Count(x =>
            x.Recovery is TrainingRecoveryRating.Insufficient);

        if (insufficientRecoveryCount >= 2)
            return Decision(TrainingAdaptationAction.ReviewAndReduceTrainingStress,
                "La recuperación insuficiente se repite. Revisa el volumen, la intensidad y la separación entre sesiones futuras.");

        if (latest.Outcome is TrainingSessionOutcome.NotCompleted &&
            latest.NonCompletionReason is TrainingNonCompletionReason.Time)
            return Decision(TrainingAdaptationAction.ReorganizeFutureSessions,
                "La sesión se omitió por falta de tiempo; reorganiza solo las sesiones futuras si el calendario y la recuperación lo permiten.");

        if ((latest.Outcome is TrainingSessionOutcome.Partial or TrainingSessionOutcome.NotCompleted) &&
            (latest.NonCompletionReason is TrainingNonCompletionReason.Fatigue or TrainingNonCompletionReason.Illness) ||
            latest.Recovery is TrainingRecoveryRating.Insufficient)
            return Decision(TrainingAdaptationAction.HoldProgressionAndReview,
                "No aumentes automáticamente la exigencia. Revisa el contexto y confirma la recuperación antes de progresar.");

        if (latest.Outcome is TrainingSessionOutcome.Completed &&
            latest.ProgressionTargetAchieved == true &&
            latest.SessionRpe is <= 8 &&
            latest.Recovery is TrainingRecoveryRating.Good)
            return Decision(TrainingAdaptationAction.ProgressAccordingToPrescription,
                "Se cumplen los criterios registrados para aplicar la progresión ya prescrita; respeta sus límites de carga y repeticiones.");

        if (latest.SessionRpe is >= 9)
            return Decision(TrainingAdaptationAction.HoldProgressionAndReview,
                "El esfuerzo registrado fue muy alto. Mantén la progresión en pausa hasta revisar rendimiento y recuperación.");

        return Decision(TrainingAdaptationAction.NoChange,
            "Los datos no justifican un cambio automático. Mantén la planificación y sigue registrando la respuesta.");

        static TrainingAdaptationDecision Decision(TrainingAdaptationAction action, string explanation) =>
            new(action, explanation);
    }

    private static bool HasInvalidValues(TrainingSessionFeedback feedback) =>
        !Enum.IsDefined(feedback.Outcome) ||
        !Enum.IsDefined(feedback.NonCompletionReason) ||
        !Enum.IsDefined(feedback.Recovery) ||
        !Enum.IsDefined(feedback.PainSignal) ||
        feedback.SessionRpe is < 0 or > 10;
}

public enum TrainingSessionOutcome
{
    Completed,
    Partial,
    NotCompleted
}

public enum TrainingNonCompletionReason
{
    None,
    Time,
    Fatigue,
    Equipment,
    Motivation,
    Illness,
    Other
}

public enum TrainingRecoveryRating
{
    Unknown,
    Good,
    Partial,
    Insufficient
}

public enum TrainingPainSignal
{
    None,
    NewOrWorsening
}

public enum TrainingAdaptationAction
{
    InsufficientData,
    InvalidFeedback,
    NoChange,
    ReorganizeFutureSessions,
    HoldProgressionAndReview,
    ReviewAndReduceTrainingStress,
    ProgressAccordingToPrescription,
    PauseAffectedExerciseAndReview
}

/// <param name="Outcome">What happened in the session.</param>
/// <param name="NonCompletionReason">Why it was partial or not completed, if applicable.</param>
/// <param name="Recovery">Self-reported recovery before the next session.</param>
/// <param name="PainSignal">A safety signal, not a diagnosis.</param>
/// <param name="SessionRpe">Optional whole-session effort rating from 0 to 10.</param>
/// <param name="ProgressionTargetAchieved">True only when the existing prescription's progression criteria were met.</param>
public sealed record TrainingSessionFeedback(
    TrainingSessionOutcome Outcome,
    TrainingNonCompletionReason NonCompletionReason = TrainingNonCompletionReason.None,
    TrainingRecoveryRating Recovery = TrainingRecoveryRating.Unknown,
    TrainingPainSignal PainSignal = TrainingPainSignal.None,
    decimal? SessionRpe = null,
    bool? ProgressionTargetAchieved = null);

public sealed record TrainingAdaptationDecision(
    TrainingAdaptationAction Action,
    string Explanation);
