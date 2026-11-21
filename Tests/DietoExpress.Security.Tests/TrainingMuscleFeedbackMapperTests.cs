using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingMuscleFeedbackMapperTests
{
    private const string Prescription = """{"goal":"Hypertrophy","exercises":[{"exerciseId":"bench","sets":3,"primaryMuscleGroups":["Chest"]},{"exerciseId":"fly","sets":2,"primaryMuscleGroups":["Chest"]}]}""";
    private const string BenchFeedback = """[{"exerciseId":"bench","plannedSets":3,"completedSets":3,"stimulus":"low","discomfort":"none","preFatigue":"recovered","sets":[{"kind":"working","completed":true},{"kind":"working","completed":true},{"kind":"working","completed":true}]}]""";

    [Fact]
    public void MapsNewestFirstHistoryChronologicallyAndAddsNeutralMissingExerciseExposure()
    {
        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "completed", BenchFeedback, Prescription),
            new(new DateOnly(2026, 11, 4), "completed", BenchFeedback, Prescription)
        ]);

        Assert.Equal(4, exposures.Count);
        Assert.Equal(new DateOnly(2026, 11, 4), exposures[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 6), exposures[2].Date);
        Assert.Equal("bench", exposures[0].ExerciseId);
        Assert.Equal("low", exposures[0].Stimulus);
        var missing = Assert.Single(exposures, item => item.ExerciseId == "fly" && item.Date == new DateOnly(2026, 11, 6));
        Assert.Null(missing.Stimulus);
        Assert.Null(missing.Discomfort);
        Assert.Null(missing.PreFatigue);
        Assert.True(missing.SessionCompleted);
    }

    [Fact]
    public void MapperAndPlannedVolumeUseOnlyReviewedMeaningfulSecondaryStimulus()
    {
        const string rowPrescription = """{"exercises":[{"exerciseId":"seated-cable-row","sets":3,"primaryMuscleGroups":["Lats","UpperBack"],"secondaryMuscleGroups":["Biceps","Forearms"]}]}""";

        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "completed",
                """[{"exerciseId":"seated-cable-row","stimulus":"low","discomfort":"none","preFatigue":"recovered"}]""",
                rowPrescription)
        ]);

        var exposure = Assert.Single(exposures);
        Assert.Equal(new[] { "Biceps", "Lats", "UpperBack" }, exposure.EffectiveMuscles);

        var planned = TrainingMuscleFeedbackMapper.CountPlannedEffectiveSets([rowPrescription]);
        Assert.Equal(3, planned["Biceps"]);
        Assert.Equal(3, planned["Lats"]);
        Assert.Equal(3, planned["UpperBack"]);
        Assert.False(planned.ContainsKey("Forearms"));
    }

    [Fact]
    public void UnusableNewestPrescriptionBlocksOlderFeedbackHistory()
    {
        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "completed", BenchFeedback, null),
            new(new DateOnly(2026, 11, 4), "completed", BenchFeedback, Prescription)
        ]);

        Assert.Empty(exposures);
    }

    [Fact]
    public void OlderUnusablePrescriptionDoesNotReplaceNewerValidExposures()
    {
        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "completed", BenchFeedback, Prescription),
            new(new DateOnly(2026, 11, 4), "completed", BenchFeedback, "not-json")
        ]);

        Assert.Equal(2, exposures.Count);
        Assert.All(exposures, item => Assert.Equal(new DateOnly(2026, 11, 6), item.Date));
    }

    [Fact]
    public void IncompleteSessionIsMarkedAndMalformedFeedbackRemainsNeutral()
    {
        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "partial", "not-json", Prescription)
        ]);

        Assert.Equal(2, exposures.Count);
        Assert.All(exposures, item =>
        {
            Assert.False(item.SessionCompleted);
            Assert.Null(item.Stimulus);
            Assert.Null(item.Discomfort);
            Assert.Null(item.PreFatigue);
        });
    }

    [Fact]
    public void CompletedSessionWithAnIncompleteExerciseCannotTriggerMuscleVolumeIncrease()
    {
        const string incompleteFeedback = """[{"exerciseId":"bench","plannedSets":3,"completedSets":2,"skippedSetReason":"time","stimulus":"low","discomfort":"none","preFatigue":"recovered","sets":[{"kind":"working","completed":true},{"kind":"working","completed":true},{"kind":"working","completed":false}]}]""";
        var records = new[]
        {
            new(new DateOnly(2026, 11, 6), "completed", incompleteFeedback, Prescription),
            new(new DateOnly(2026, 11, 4), "completed", incompleteFeedback, Prescription)
        };

        var exposures = TrainingMuscleFeedbackMapper.Map(records);
        var benchExposures = exposures.Where(item => item.ExerciseId == "bench").ToArray();
        Assert.Equal(2, benchExposures.Length);
        Assert.All(benchExposures, item => Assert.False(item.SessionCompleted));

        var decisions = TrainingMuscleFeedbackAdapter.Evaluate(
            exposures,
            new Dictionary<string, int> { ["Chest"] = 6 },
            new Dictionary<string, int> { ["Chest"] = 16 });
        Assert.DoesNotContain(decisions, item => item.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void PainSkipIsMappedAsASafetyBlockerEvenWhenDiscomfortIsNotAnswered()
    {
        const string painFeedback = """[{"exerciseId":"bench","plannedSets":3,"completedSets":2,"skippedSetReason":"pain","stimulus":"low","preFatigue":"recovered","sets":[{"kind":"working","completed":true},{"kind":"working","completed":true},{"kind":"working","completed":false}]}]""";
        var exposures = TrainingMuscleFeedbackMapper.Map([
            new(new DateOnly(2026, 11, 6), "completed", painFeedback, Prescription)
        ]);

        var bench = Assert.Single(exposures, item => item.ExerciseId == "bench");
        Assert.True(bench.PainReported);
        Assert.False(bench.SessionCompleted);

        var decisions = TrainingMuscleFeedbackAdapter.Evaluate(
            exposures,
            new Dictionary<string, int> { ["Chest"] = 6 },
            new Dictionary<string, int> { ["Chest"] = 16 });
        Assert.Contains(decisions, item =>
            item.Action == TrainingMuscleFeedbackAction.PauseExercise && item.ExerciseId == "bench");
        Assert.DoesNotContain(decisions, item => item.Action == TrainingMuscleFeedbackAction.AddSet);
    }

    [Fact]
    public void NonFatigueSkippedSetsCannotTriggerAutomaticMuscleVolumeReduction()
    {
        const string timeFeedback = """[{"exerciseId":"bench","plannedSets":3,"completedSets":2,"skippedSetReason":"time","stimulus":"high","discomfort":"none","preFatigue":"loaded","sets":[{"kind":"working","completed":true},{"kind":"working","completed":true},{"kind":"working","completed":false}]}]""";
        var records = new[]
        {
            new(new DateOnly(2026, 11, 8), "completed", timeFeedback, Prescription),
            new(new DateOnly(2026, 11, 6), "completed", timeFeedback, Prescription),
            new(new DateOnly(2026, 11, 4), "completed", timeFeedback, Prescription)
        };
        var exposures = TrainingMuscleFeedbackMapper.Map(records);

        var decisions = TrainingMuscleFeedbackAdapter.Evaluate(
            exposures,
            new Dictionary<string, int> { ["Chest"] = 10 },
            new Dictionary<string, int> { ["Chest"] = 16 });
        Assert.DoesNotContain(decisions, item => item.Action == TrainingMuscleFeedbackAction.ReduceSet);
    }

}
