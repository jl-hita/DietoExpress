using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingExerciseProgressionEngineTests
{
    private static TrainingExerciseProgressionPrescription Prescription(decimal? increment = 2.5m) =>
        new("bench-press", 3, 8, 12, 2m, TrainingProgressionRule.DoubleProgression, increment);

    private static TrainingExerciseExposure Exposure(
        int reps, decimal rir = 2m, decimal load = 60m, bool recovered = true) =>
        new(Enumerable.Range(0, 3)
            .Select(_ => new TrainingExecutedSet("working", load, reps, rir, true)).ToArray(),
            RecoveryInsufficient: !recovered);

    [Fact]
    public void BelowTopOfRange_AddsAtMostOneRepPerSetAndKeepsLoad()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [Exposure(10)]);

        Assert.Equal(TrainingExerciseProgressionAction.AddRepetitions, result.Action);
        Assert.Equal(60m, result.SuggestedLoadKg);
        Assert.Equal(new[] { 11, 11, 11 }, result.SuggestedRepetitionsBySet);
    }

    [Fact]
    public void ReachingTopOnce_RequiresConfirmationBeforeLoadIncrease()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.ConfirmTopOfRange, result.Action);
        Assert.Null(result.SuggestedLoadKg);
    }

    [Fact]
    public void TwoConsecutiveTopRangeExposures_IncreaseBySmallestConfiguredIncrement()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(1.25m),
            [Exposure(12), Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Equal(61.25m, result.SuggestedLoadKg);
        Assert.Equal(1.25m, result.LoadIncreaseKg);
        Assert.Equal(new[] { 8, 8, 8 }, result.SuggestedRepetitionsBySet);
    }

    [Fact]
    public void LoadIncreaseWithoutKnownEquipmentIncrement_IsBoundedAndNotInvented()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(null),
            [Exposure(12), Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Null(result.SuggestedLoadKg);
        Assert.Null(result.LoadIncreaseKg);
        Assert.Equal(5m, result.MaximumLoadIncreasePercent);
        Assert.Contains("menor incremento disponible", result.Explanation);
    }

    [Fact]
    public void EquipmentIncrementAboveFivePercent_IsRejected()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(5m),
            [Exposure(12, load: 60m), Exposure(12, load: 60m)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
        Assert.Null(result.SuggestedLoadKg);
    }

    [Fact]
    public void ThreeTopOfRangeExposuresStillProgressLoadInsteadOfBeingFlaggedAsStagnant()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(1.25m),
            [Exposure(12), Exposure(12), Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Equal(61.25m, result.SuggestedLoadKg);
    }

    [Fact]
    public void ExerciseSpecificIncreaseCeilingOverridesDefault()
    {
        var prescription = Prescription(null) with { MaximumLoadIncreasePercent = 2m };
        var result = TrainingExerciseProgressionEngine.Evaluate(prescription,
            [Exposure(12), Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.IncreaseLoad, result.Action);
        Assert.Equal(2m, result.MaximumLoadIncreasePercent);
        Assert.Equal(new[] { 8, 8, 8 }, result.SuggestedRepetitionsBySet);
    }

    [Fact]
    public void InvalidIncreaseCeilingRejectsPrescription()
    {
        var prescription = Prescription() with { MaximumLoadIncreasePercent = 11m };
        var result = TrainingExerciseProgressionEngine.Evaluate(prescription, [Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.InvalidPrescription, result.Action);
    }

    [Fact]
    public void RirBelowTarget_HoldsProgressionEvenWhenRepsAreAtMaximum()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(12, rir: 1m), Exposure(12, rir: 1m)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void MissingRir_WhenPrescriptionHasTarget_HoldsProgression()
    {
        var exposure = new TrainingExerciseExposure(Enumerable.Range(0, 3)
            .Select(_ => new TrainingExecutedSet("working", 60m, 12, null, true)).ToArray());

        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [exposure, exposure]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void IncompleteSet_HoldsProgression()
    {
        var exposure = new TrainingExerciseExposure([
            new("working", 60m, 12, 2m, true),
            new("working", 60m, 12, 2m, true),
            new("working", 60m, 12, 2m, false)
        ]);

        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [exposure]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void WarmupSetsAreNotAcceptedAsWorkingSets()
    {
        var exposure = new TrainingExerciseExposure(Enumerable.Range(0, 3)
            .Select(_ => new TrainingExecutedSet("warmup", 20m, 12, 5m, true)).ToArray());

        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [exposure]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void RecentPainBlocksProgressionAcrossLaterPainFreeRecord()
    {
        var painful = Exposure(12) with { PainReported = true };
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [painful, Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void InsufficientRecoveryBlocksProgression()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(12), Exposure(12, recovered: false)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void IncompleteLatestExposureCannotBeSkippedToProgressFromOlderHistory()
    {
        var incomplete = Exposure(12) with { SessionCompleted = false };
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(12), Exposure(12), incomplete]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
        Assert.Null(result.SuggestedLoadKg);
    }

    [Fact]
    public void DifferentLoadsAcrossWorkingSets_AreNotAveraged()
    {
        var exposure = new TrainingExerciseExposure([
            new("working", 60m, 12, 2m, true),
            new("working", 57.5m, 12, 2m, true),
            new("working", 55m, 12, 2m, true)
        ]);

        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), [exposure]);

        Assert.Equal(TrainingExerciseProgressionAction.InvalidData, result.Action);
    }

    [Fact]
    public void RepetitionsAbovePrescribedMaximum_AreInvalidAndNeverTriggerLoadIncrease()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(13), Exposure(13)]);

        Assert.Equal(TrainingExerciseProgressionAction.InvalidData, result.Action);
        Assert.Null(result.SuggestedLoadKg);
        Assert.Null(result.LoadIncreaseKg);
    }

    [Fact]
    public void LowRepetitions_HoldRatherThanAutomaticallyReducingOrIncreasingLoad()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(6)]);

        Assert.Equal(TrainingExerciseProgressionAction.HoldAndReview, result.Action);
    }

    [Fact]
    public void ThreeExposuresWithoutRepetitionOrEffortImprovement_FlagStagnation()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(10), Exposure(10), Exposure(10)]);

        Assert.Equal(TrainingExerciseProgressionAction.StagnationReview, result.Action);
        Assert.Contains("tres exposiciones", result.Explanation);
    }

    [Fact]
    public void ImprovedRirPreventsFalseStagnation()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(),
            [Exposure(10, rir: 2m), Exposure(10, rir: 2.5m), Exposure(10, rir: 3m)]);

        Assert.Equal(TrainingExerciseProgressionAction.AddRepetitions, result.Action);
    }

    [Fact]
    public void NoProgressionRule_ReturnsNotApplicable()
    {
        var p = Prescription() with { ProgressionRule = TrainingProgressionRule.None };
        var result = TrainingExerciseProgressionEngine.Evaluate(p, [Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.NotApplicable, result.Action);
    }

    [Fact]
    public void UnsupportedProgressionRule_IsNotInterpretedAsDoubleProgression()
    {
        var p = Prescription() with { ProgressionRule = TrainingProgressionRule.IncreaseLoadWithinLimits };
        var result = TrainingExerciseProgressionEngine.Evaluate(p, [Exposure(12), Exposure(12)]);

        Assert.Equal(TrainingExerciseProgressionAction.NotApplicable, result.Action);
    }

    [Fact]
    public void InsufficientHistory_DoesNotInventProgression()
    {
        var result = TrainingExerciseProgressionEngine.Evaluate(Prescription(), []);

        Assert.Equal(TrainingExerciseProgressionAction.InsufficientData, result.Action);
    }
}
