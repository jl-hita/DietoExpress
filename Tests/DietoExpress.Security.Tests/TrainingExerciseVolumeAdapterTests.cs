using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingExerciseVolumeAdapterTests
{
    [Fact]
    public void RepeatedExerciseSpecificFatigueProposesOneSetReduction()
    {
        var history = new[]
        {
            Exposure("bench"),
            Exposure("bench", skippedReason: "fatigue"),
            Exposure("bench", skippedReason: "fatigue")
        };

        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(history));
        Assert.Equal(TrainingExerciseVolumeAction.ProposeReduceOneSet, recommendation.Action);
        Assert.Equal(2, recommendation.SuggestedWorkingSets);
    }

    [Fact]
    public void OneDifficultExposureDoesNotTriggerVolumeReduction()
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench", skippedReason: "fatigue"),
            Exposure("bench"),
            Exposure("bench")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
        Assert.Null(recommendation.SuggestedWorkingSets);
    }

    [Theory]
    [InlineData("time")]
    [InlineData("equipment")]
    [InlineData("illness")]
    [InlineData("motivation")]
    public void NonFatigueReasonsDoNotReduceExerciseVolume(string reason)
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench", skippedReason: reason),
            Exposure("bench", skippedReason: reason),
            Exposure("bench")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
    }

    [Fact]
    public void PainAlwaysPausesAndIsNotTreatedAsOrdinaryFatigue()
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench"),
            Exposure("bench", pain: true),
            Exposure("bench")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.PauseAndReview, recommendation.Action);
        Assert.Null(recommendation.SuggestedWorkingSets);
    }

    [Fact]
    public void TwoRirShortfallsCanTriggerReviewButOneExposureCannotCountTwice()
    {
        var oneExposure = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench", skippedReason: "fatigue", actualRir: 0m),
            Exposure("bench"),
            Exposure("bench")
        ]));
        Assert.Equal(TrainingExerciseVolumeAction.NoChange, oneExposure.Action);

        var repeated = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench"),
            Exposure("bench", actualRir: 0m),
            Exposure("bench", actualRir: 0m)
        ]));
        Assert.Equal(TrainingExerciseVolumeAction.ProposeReduceOneSet, repeated.Action);
    }

    [Fact]
    public void SingleSetExerciseIsReviewedButNeverReducedBelowOne()
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("plank", planned: 1, skippedReason: "fatigue"),
            Exposure("plank", planned: 1, skippedReason: "fatigue")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.ReviewWithoutReduction, recommendation.Action);
        Assert.Null(recommendation.SuggestedWorkingSets);
    }

    [Fact]
    public void IncompleteSessionDoesNotCountAsExerciseFatigueEvidence()
    {
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate([
            Exposure("bench", skippedReason: "fatigue", completedSession: false),
            Exposure("bench", skippedReason: "fatigue"),
            Exposure("bench")
        ]));

        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
    }

    [Fact]
    public void PartialSessionDoesNotCountAsCompletedExerciseFatigueEvidence()
    {
        const string feedback = """[{"exerciseId":"bench","plannedSets":3,"completedSets":2,"skippedSetReason":"fatigue","sets":[{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":false,"rir":null}]}]""";
        const string prescription = """{"exercises":[{"exerciseId":"bench","targetRepetitionsInReserve":2}]}""";
        var records = new[]
        {
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("partial", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("partial", feedback, prescription)
        };

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map(records);

        Assert.Equal(3, exposures.Count);
        Assert.True(exposures[0].SessionCompleted);
        Assert.False(exposures[1].SessionCompleted);
        Assert.False(exposures[2].SessionCompleted);
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(exposures));
        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
    }

    [Fact]
    public void MissingRirOnAnyCompletedWorkingSetDoesNotCreateAnAverage()
    {
        const string feedback = """[{"exerciseId":"bench","plannedSets":2,"completedSets":2,"sets":[{"kind":"working","completed":true,"rir":0},{"kind":"working","completed":true,"rir":null}]}]""";
        const string prescription = """{"exercises":[{"exerciseId":"bench","targetRepetitionsInReserve":2}]}""";
        var records = new[]
        {
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription)
        };

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map(records);

        Assert.All(exposures, exposure => Assert.Null(exposure.ActualAverageRir));
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(exposures));
        Assert.Equal(TrainingExerciseVolumeAction.NoChange, recommendation.Action);
    }

    [Fact]
    public void FeedbackMapperReadsStableExerciseIdAndRirButDoesNotSpreadGlobalPain()
    {
        const string feedback = """[{"exerciseId":"bench","plannedSets":3,"completedSets":2,"skippedSetReason":"fatigue","sets":[{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":true,"rir":1},{"kind":"working","completed":false,"rir":null}]}]""";
        const string prescription = """{"exercises":[{"exerciseId":"bench","targetRepetitionsInReserve":2}]}""";
        var records = new[]
        {
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription),
            new TrainingExerciseVolumeFeedbackRecord("completed", feedback, prescription)
        };

        var exposures = TrainingExerciseVolumeFeedbackMapper.Map(records);

        Assert.Equal(3, exposures.Count);
        Assert.All(exposures, exposure =>
        {
            Assert.Equal("bench", exposure.ExerciseId);
            Assert.Equal(2m, exposure.TargetRir);
            Assert.Equal(1m, exposure.ActualAverageRir);
            Assert.Equal("fatigue", exposure.SkippedSetReason);
            Assert.False(exposure.PainReported);
        });
        var recommendation = Assert.Single(TrainingExerciseVolumeAdapter.Evaluate(exposures));
        Assert.Equal(TrainingExerciseVolumeAction.ProposeReduceOneSet, recommendation.Action);
    }

    private static TrainingExerciseVolumeExposure Exposure(
        string id,
        int planned = 3,
        string? skippedReason = null,
        bool completedSession = true,
        bool pain = false,
        decimal? actualRir = null) =>
        new(id, planned, skippedReason is null ? planned : planned - 1,
            skippedReason, completedSession, pain, TargetRir: 2m, ActualAverageRir: actualRir);
}
