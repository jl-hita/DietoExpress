using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingMuscleFeedbackTests
{
    private static TrainingMuscleFeedbackExposure E(
        int day, string id = "bench", string muscle = "Chest", int sets = 3,
        string? stimulus = "adequate", string? discomfort = "none", string? pre = "recovered", bool completed = true) =>
        new(new DateOnly(2026, 11, day), id, [muscle], sets, stimulus, discomfort, pre, completed);

    private static readonly Dictionary<string, int> Planned = new() { ["Chest"] = 10 };
    private static readonly Dictionary<string, int> Ceiling = new() { ["Chest"] = 16 };

    private static IReadOnlyList<TrainingMuscleFeedbackDecision> Run(params TrainingMuscleFeedbackExposure[] history) =>
        TrainingMuscleFeedbackAdapter.Evaluate(history, Planned, Ceiling);

    [Fact]
    public void AdequateAnswersChangeNothing() =>
        Assert.Empty(Run(E(2), E(4), E(6)));

    [Fact]
    public void LowStimulusTwiceWhileRecoveredAddsOneSetToTheLeanestExercise()
    {
        var result = Run(E(2, stimulus: "low"), E(4, "bench", sets: 4, stimulus: "low"), E(4, "fly", sets: 2, stimulus: "low"));
        var add = Assert.Single(result);
        Assert.Equal(TrainingMuscleFeedbackAction.AddSet, add.Action);
        Assert.Equal("fly", add.ExerciseId);
        Assert.Equal("Chest", add.Muscle);
    }

    [Fact]
    public void LowStimulusNeedsTwoSessionsRecoveryNoDiscomfortAndRoomBelowTheCeiling()
    {
        Assert.Empty(Run(E(4, stimulus: "low")));                                                  // one session only
        Assert.Empty(Run(E(2, stimulus: "low"), E(4, stimulus: "low", pre: "loaded")));             // not recovered
        Assert.Empty(Run(E(2, stimulus: "low"), E(4, stimulus: "low", pre: null)));                 // missing answer is not evidence
        Assert.DoesNotContain(Run(E(2, stimulus: "low", discomfort: "mild"), E(4, stimulus: "low")),
            d => d.Action == TrainingMuscleFeedbackAction.AddSet);
        Assert.Empty(TrainingMuscleFeedbackAdapter.Evaluate(
            [E(2, stimulus: "low"), E(4, stimulus: "low")], new Dictionary<string, int> { ["Chest"] = 16 }, Ceiling));
    }

    [Fact]
    public void HighStimulusWithAccumulatedFatigueRemovesOneSetButKeepsAtLeastOne()
    {
        var result = Run(E(2, stimulus: "high", pre: "loaded"), E(4, stimulus: "high", pre: "loaded", sets: 3), E(6, stimulus: "high", pre: "loaded"));
        var reduce = Assert.Single(result);
        Assert.Equal(TrainingMuscleFeedbackAction.ReduceSet, reduce.Action);
        Assert.Empty(Run(E(2, stimulus: "high", pre: "loaded", sets: 1), E(4, stimulus: "high", pre: "loaded", sets: 1)));
        Assert.Empty(Run(E(2, stimulus: "high", pre: "recovered"), E(4, stimulus: "high", pre: "recovered")));
    }

    [Fact]
    public void LowStimulusAddsASetOnlyToTheExerciseThatReportedLowStimulus()
    {
        var result = Run(
            E(2, "bench", sets: 3, stimulus: "low"),
            E(4, "bench", sets: 3, stimulus: "low"),
            E(4, "fly", sets: 5, stimulus: "low"));

        var add = Assert.Single(result);
        Assert.Equal(TrainingMuscleFeedbackAction.AddSet, add.Action);
        Assert.Equal("bench", add.ExerciseId);
    }

    [Fact]
    public void StrengthFeedbackCannotAddAFourthSetToAnExercise()
    {
        var result = TrainingMuscleFeedbackAdapter.Evaluate(
            [
                E(2, "bench", sets: 3, stimulus: "low"),
                E(4, "bench", sets: 3, stimulus: "low")
            ],
            Planned,
            Ceiling,
            TrainingProgramVolumeAllocator.GetMaximumSetsPerExercise(TrainingGoal.Strength));

        Assert.DoesNotContain(result, decision => decision.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void StrengthFeedbackCanIncreaseAnExerciseFromTwoToThreeSets()
    {
        var result = TrainingMuscleFeedbackAdapter.Evaluate(
            [
                E(2, "bench", sets: 2, stimulus: "low"),
                E(4, "bench", sets: 2, stimulus: "low")
            ],
            Planned,
            Ceiling,
            TrainingProgramVolumeAllocator.GetMaximumSetsPerExercise(TrainingGoal.Strength));

        var add = Assert.Single(result);
        Assert.Equal(TrainingMuscleFeedbackAction.AddSet, add.Action);
        Assert.Equal("bench", add.ExerciseId);
    }

    [Fact]
    public void HighStimulusAndLoadedStateMustCoincideInAtLeastTwoRecentSessions()
    {
        var result = Run(
            E(2, stimulus: "high", pre: "recovered"),
            E(4, stimulus: "adequate", pre: "loaded"),
            E(6, stimulus: "high", pre: "loaded"));

        Assert.DoesNotContain(result, decision => decision.Action == TrainingMuscleFeedbackAction.ReduceSet);
    }

    [Fact]
    public void HighAndLoadedSignalThatResolvedInLatestSessionDoesNotReduceVolume()
    {
        var result = Run(
            E(2, stimulus: "high", pre: "loaded"),
            E(4, stimulus: "high", pre: "loaded"),
            E(6, stimulus: "high", pre: "recovered"));

        Assert.DoesNotContain(result, decision => decision.Action == TrainingMuscleFeedbackAction.ReduceSet);
    }

    [Fact]
    public void ReductionNeverTargetsAnUnflaggedExerciseOfTheSameMuscle()
    {
        var result = Run(
            E(2, "bench", sets: 3, stimulus: "high", pre: "loaded"),
            E(4, "bench", sets: 3, stimulus: "high", pre: "loaded"),
            E(6, "bench", sets: 1, stimulus: "high", pre: "loaded"),
            E(6, "fly", sets: 4, stimulus: "adequate", pre: "loaded"));

        Assert.DoesNotContain(result, decision => decision.Action == TrainingMuscleFeedbackAction.ReduceSet);
    }

    [Fact]
    public void StrongDiscomfortPausesAndMildRepeatedDiscomfortSuggestsASwap()
    {
        var strong = Run(E(2), E(4, discomfort: "strong"));
        Assert.Contains(strong, d => d.Action == TrainingMuscleFeedbackAction.PauseExercise && d.ExerciseId == "bench");
        var mild = Run(E(2, discomfort: "mild"), E(4), E(6, discomfort: "mild"));
        Assert.Contains(mild, d => d.Action == TrainingMuscleFeedbackAction.SuggestSwap);
        Assert.Empty(Run(E(2, discomfort: "mild"), E(4), E(6)));
    }

    [Fact]
    public void ExercisesWithDiscomfortNeverFeedVolumeIncreases()
    {
        var result = Run(E(2, stimulus: "low", discomfort: "mild"), E(4, stimulus: "low", discomfort: "mild"), E(6, stimulus: "low"));
        Assert.DoesNotContain(result, d => d.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void MixedLowAndAdequateExercisesDoNotTriggerMuscleVolumeIncrease()
    {
        var result = Run(
            E(2, "bench", stimulus: "low"), E(2, "fly", sets: 2, stimulus: "adequate"),
            E(4, "bench", stimulus: "low"), E(4, "fly", sets: 2, stimulus: "adequate"));
        Assert.DoesNotContain(result, item => item.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void MissingExerciseFeedbackDoesNotCountAsLowStimulusEvidence()
    {
        var result = Run(
            E(2, "bench", stimulus: "low"), E(2, "fly", sets: 2, stimulus: null, discomfort: null, pre: null),
            E(4, "bench", stimulus: "low"), E(4, "fly", sets: 2, stimulus: null, discomfort: null, pre: null));
        Assert.DoesNotContain(result, item => item.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void DiscomfortOnOneExerciseCannotBeHiddenWhenEvaluatingMuscleVolumeIncrease()
    {
        var result = Run(
            E(2, "bench", stimulus: "low"), E(2, "fly", sets: 2, stimulus: "low", discomfort: "mild"),
            E(4, "bench", stimulus: "low"), E(4, "fly", sets: 2, stimulus: "low", discomfort: "mild"));
        Assert.DoesNotContain(result, item => item.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void HighAndLoadedSignalsFromDifferentExercisesDoNotReduceVolume()
    {
        var result = Run(
            E(2, "bench", stimulus: "high", pre: "loaded"),
            E(4, "fly", sets: 2, stimulus: "high", pre: "loaded"),
            E(6, "bench", stimulus: "high", pre: "recovered"));
        Assert.DoesNotContain(result, item => item.Action == TrainingMuscleFeedbackAction.ReduceSet);
    }

    [Fact]
    public void ReductionTargetsTheSameExerciseThatRepeatedlyReportedHighStimulusAndFatigue()
    {
        var result = Run(
            E(2, "bench", sets: 3, stimulus: "high", pre: "loaded"),
            E(4, "bench", sets: 3, stimulus: "high", pre: "loaded"),
            E(6, "bench", sets: 3, stimulus: "high", pre: "loaded"),
            E(6, "fly", sets: 4, stimulus: "adequate", pre: "loaded"));
        var reduce = Assert.Single(result.Where(item => item.Action == TrainingMuscleFeedbackAction.ReduceSet));
        Assert.Equal("bench", reduce.ExerciseId);
    }

    [Fact]
    public void IncompleteSessionsCannotTriggerVolumeIncreaseOrReduction()
    {
        var add = Run(E(2, stimulus: "low"), E(4, stimulus: "low", completed: false));
        Assert.DoesNotContain(add, item => item.Action == TrainingMuscleFeedbackAction.AddSet);

        var reduce = Run(
            E(2, stimulus: "high", pre: "loaded"),
            E(4, stimulus: "high", pre: "loaded"),
            E(6, stimulus: "high", pre: "loaded", completed: false));
        Assert.DoesNotContain(reduce, item => item.Action == TrainingMuscleFeedbackAction.ReduceSet);
    }

}
