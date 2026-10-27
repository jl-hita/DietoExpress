using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingAdaptationPolicyTests
{
    [Fact]
    public void EmptyHistory_DoesNotInventARecommendation()
    {
        var result = TrainingAdaptationPolicy.Evaluate(Array.Empty<TrainingSessionFeedback>());
        Assert.Equal(TrainingAdaptationAction.InsufficientData, result.Action);
    }

    [Fact]
    public void MissedForTime_ReorganizesOnlyFutureSessions()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.NotCompleted, TrainingNonCompletionReason.Time)
        ]);
        Assert.Equal(TrainingAdaptationAction.ReorganizeFutureSessions, result.Action);
    }

    [Fact]
    public void FatigueRelatedPartialSession_HoldsProgression()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Partial, TrainingNonCompletionReason.Fatigue, TrainingRecoveryRating.Partial)
        ]);
        Assert.Equal(TrainingAdaptationAction.HoldProgressionAndReview, result.Action);
    }

    [Fact]
    public void IllnessReasonOnCompletedSession_DoesNotTriggerNonCompletionRule()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, TrainingNonCompletionReason.Illness,
                Recovery: TrainingRecoveryRating.Good, SessionRpe: 6)
        ]);
        Assert.Equal(TrainingAdaptationAction.NoChange, result.Action);
    }

    [Fact]
    public void FatigueReasonOnCompletedSession_DoesNotTriggerNonCompletionRule()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, TrainingNonCompletionReason.Fatigue,
                Recovery: TrainingRecoveryRating.Good, SessionRpe: 6)
        ]);
        Assert.Equal(TrainingAdaptationAction.NoChange, result.Action);
    }

    [Fact]
    public void RepeatedInsufficientRecovery_ReviewsTrainingStress()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient),
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good),
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Insufficient)
        ]);
        Assert.Equal(TrainingAdaptationAction.ReviewAndReduceTrainingStress, result.Action);
    }

    [Fact]
    public void NewOrWorseningPain_TakesPriorityOverProgression()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good,
                PainSignal: TrainingPainSignal.NewOrWorsening, SessionRpe: 6, ProgressionTargetAchieved: true)
        ]);
        Assert.Equal(TrainingAdaptationAction.PauseAffectedExerciseAndReview, result.Action);
    }

    [Fact]
    public void ProgressionRequiresAllExplicitCriteria()
    {
        var feedback = new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good,
            SessionRpe: 8, ProgressionTargetAchieved: true);
        Assert.Equal(TrainingAdaptationAction.ProgressAccordingToPrescription, TrainingAdaptationPolicy.Evaluate([feedback]).Action);
    }

    [Theory]
    [InlineData(null, TrainingRecoveryRating.Good)]
    [InlineData(true, TrainingRecoveryRating.Unknown)]
    [InlineData(true, TrainingRecoveryRating.Partial)]
    [InlineData(false, TrainingRecoveryRating.Good)]
    public void MissingOrIncompleteProgressionEvidence_DoesNotProgress(bool? progressionTargetAchieved, TrainingRecoveryRating recovery)
    {
        var feedback = new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: recovery,
            SessionRpe: 7, ProgressionTargetAchieved: progressionTargetAchieved);
        Assert.NotEqual(TrainingAdaptationAction.ProgressAccordingToPrescription, TrainingAdaptationPolicy.Evaluate([feedback]).Action);
    }

    [Fact]
    public void VeryHighEffort_HoldsProgression()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, Recovery: TrainingRecoveryRating.Good,
                SessionRpe: 9, ProgressionTargetAchieved: true)
        ]);
        Assert.Equal(TrainingAdaptationAction.HoldProgressionAndReview, result.Action);
    }

    [Fact]
    public void UnknownRecovery_IsNotTreatedAsGoodRecovery()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, SessionRpe: 6, ProgressionTargetAchieved: true)
        ]);
        Assert.NotEqual(TrainingAdaptationAction.ProgressAccordingToPrescription, result.Action);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(10.1)]
    public void OutOfRangeRpe_IsRejected(decimal rpe)
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback(TrainingSessionOutcome.Completed, SessionRpe: rpe)
        ]);
        Assert.Equal(TrainingAdaptationAction.InvalidFeedback, result.Action);
    }

    [Fact]
    public void UndefinedEnumValue_IsRejected()
    {
        var result = TrainingAdaptationPolicy.Evaluate([
            new TrainingSessionFeedback((TrainingSessionOutcome)999)
        ]);
        Assert.Equal(TrainingAdaptationAction.InvalidFeedback, result.Action);
    }

    [Fact]
    public void NullHistory_ThrowsInsteadOfBeingSilentlyAccepted()
    {
        Assert.Throws<ArgumentNullException>(() => TrainingAdaptationPolicy.Evaluate(null!));
    }
}
