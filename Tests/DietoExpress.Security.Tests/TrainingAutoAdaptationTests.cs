using System.Text.Json.Nodes;
using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingAutoAdaptationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static TrainingAutoAdaptationDecision Decide(
        string action, string[]? ids = null, params TrainingAutoAdaptationRecord[] recent) =>
        TrainingAutoAdaptationPolicy.Decide(action, ids ?? [], Now, recent);

    [Theory]
    [InlineData("review_and_reduce_training_stress", new[] { "squat" }, TrainingAutoAdaptationKind.ReduceSets)]
    [InlineData("review_and_reduce_training_stress", new string[0], TrainingAutoAdaptationKind.RaiseReserve)]
    [InlineData("restore_volume", new[] { "squat" }, TrainingAutoAdaptationKind.RestoreSets)]
    [InlineData("propose_deload", new string[0], TrainingAutoAdaptationKind.Deload)]
    [InlineData("hold_progression_and_review", new string[0], TrainingAutoAdaptationKind.HoldProgression)]
    public void SmallReversibleAdjustmentsAreAppliedWithoutAProfessional(string action, string[] ids, TrainingAutoAdaptationKind expected)
    {
        var decision = Decide(action, ids);
        Assert.True(decision.ShouldApply);
        Assert.Equal(expected, decision.Kind);
    }

    [Theory]
    [InlineData("pause_affected_exercise_and_review")]
    [InlineData("reorganize_future_sessions")]
    [InlineData("no_change")]
    [InlineData("anything_else")]
    public void PainAndReorganisationAreNeverAppliedAutomatically(string action)
    {
        var decision = Decide(action);
        Assert.False(decision.ShouldApply);
        Assert.NotNull(decision.SkipReason);
    }

    [Fact]
    public void ACooldownStopsTheSameKindOfChangeFromStacking()
    {
        var recent = new TrainingAutoAdaptationRecord(TrainingAutoAdaptationKind.ReduceSets, Now.AddDays(-5));
        Assert.False(Decide("review_and_reduce_training_stress", ["squat"], recent).ShouldApply);
        var old = new TrainingAutoAdaptationRecord(TrainingAutoAdaptationKind.ReduceSets, Now.AddDays(-15));
        Assert.True(Decide("review_and_reduce_training_stress", ["squat"], old).ShouldApply);
    }

    [Fact]
    public void ADeloadHappensAtMostOnceEvery28DaysAndAbsorbsOtherReductions()
    {
        Assert.False(Decide("propose_deload", null, new TrainingAutoAdaptationRecord(TrainingAutoAdaptationKind.Deload, Now.AddDays(-27))).ShouldApply);
        Assert.True(Decide("propose_deload", null, new TrainingAutoAdaptationRecord(TrainingAutoAdaptationKind.Deload, Now.AddDays(-29))).ShouldApply);
        Assert.False(Decide("review_and_reduce_training_stress", ["squat"], new TrainingAutoAdaptationRecord(TrainingAutoAdaptationKind.Deload, Now.AddDays(-3))).ShouldApply);
    }

    [Theory]
    [InlineData("review_and_reduce_training_stress", "squat")]
    [InlineData("review_and_reduce_training_stress", "")]
    [InlineData("restore_volume", "squat")]
    [InlineData("add_muscle_set", "bench")]
    [InlineData("reduce_muscle_set", "bench")]
    [InlineData("hold_progression_and_review", "")]
    public void ActiveDeloadBlocksOtherAutomaticLoadAndVolumeAdjustments(string action, string exerciseId)
    {
        var ids = string.IsNullOrWhiteSpace(exerciseId) ? [] : [exerciseId];
        var recentDeload = new TrainingAutoAdaptationRecord(
            TrainingAutoAdaptationKind.Deload, Now.AddDays(-3));

        var decision = Decide(action, ids, recentDeload);

        Assert.False(decision.ShouldApply);
        Assert.Equal(TrainingAutoAdaptationKind.None, decision.Kind);
        Assert.Contains("descarga en curso", decision.SkipReason ?? "");
    }

    private const string Session = """{"exercises":[{"exerciseId":"squat","exerciseNameSnapshot":"Sentadilla","order":1,"sets":4,"minRepetitions":5,"maxRepetitions":8,"targetRepetitionsInReserve":2,"restSeconds":180,"progressionRule":1,"catalogVersion":"v"},{"exerciseId":"curl","exerciseNameSnapshot":"Curl","order":2,"sets":1,"minRepetitions":8,"maxRepetitions":12,"targetRepetitionsInReserve":4,"restSeconds":90,"progressionRule":1,"catalogVersion":"v"}]}""";

    [Fact]
    public void DeloadHalvesSetsAddsTwoRirStopsProgressionAndOnlyHappensOnce()
    {
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryApplyDeload(Session, out var updated, out _));
        var exercises = JsonNode.Parse(updated)!["exercises"]!;
        Assert.Equal(2, (int)exercises[0]!["sets"]!);
        Assert.Equal(4m, (decimal)exercises[0]!["targetRepetitionsInReserve"]!);
        Assert.Equal(1, (int)exercises[1]!["sets"]!);          // never below one set
        Assert.Equal(5m, (decimal)exercises[1]!["targetRepetitionsInReserve"]!); // capped at 5
        Assert.Equal(0, (int)exercises[0]!["progressionRule"]!);
        Assert.Equal(4, (int)exercises[0]!["adaptationOriginalSets"]!);
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TryApplyDeload(updated, out _, out _));
    }

    [Fact]
    public void DeloadedSetsCanBeRestoredLaterNeverAboveTheOriginalDose()
    {
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryApplyDeload(Session, out var deloaded, out _));
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryRestoreSets(deloaded, new HashSet<string> { "squat" }, out var restored, out _, out _));
        Assert.Equal(3, (int)JsonNode.Parse(restored)!["exercises"]![0]!["sets"]!);
    }

    [Fact]
    public void SetLoadWritesTheLoadAndReasonAndIgnoresNoOpsAndInvalidValues()
    {
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TrySetLoad(Session, "squat", 92.5m, "Carga reducida automáticamente.", out var updated, out var legacy));
        var squat = JsonNode.Parse(updated)!["exercises"]![0]!;
        Assert.Equal(92.5m, (decimal)squat["targetLoadKg"]!);
        Assert.Contains("carga", legacy);
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TrySetLoad(updated, "squat", 92.5m, "x", out _, out _));
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TrySetLoad(Session, "missing", 50m, "x", out _, out _));
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TrySetLoad(Session, "squat", 0m, "x", out _, out _));
    }

    [Fact]
    public void EveryAutomaticChangeIsRecordedAndOnlyTouchesFutureUnrecordedSessions()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Anguloso.sln"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "Anguloso.Server", "Logica", "TrainingAutoAdaptationApplier.cs"));
        Assert.Contains("reviewed_by_user_id = NULL", source);
        Assert.Contains("auto:", source);
        Assert.Contains("s.scheduled_date > CURRENT_DATE", source);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM training_session_records", source);
        Assert.Contains("BeginTransactionAsync", source);
    }

    [Theory]
    [InlineData("add_muscle_set", TrainingAutoAdaptationKind.AddMuscleSet)]
    [InlineData("reduce_muscle_set", TrainingAutoAdaptationKind.ReduceMuscleSet)]
    public void MuscleFeedbackSetChangesAreAppliedAutomaticallyWithACooldown(string action, TrainingAutoAdaptationKind kind)
    {
        Assert.Equal(kind, Decide(action, ["bench"]).Kind);
        Assert.False(Decide(action, ["bench"], new TrainingAutoAdaptationRecord(kind, Now.AddDays(-3))).ShouldApply);
    }

    [Fact]
    public void AdjustSetsStaysBetweenOneAndFiveAndRemembersTheOriginalDoseWhenReducing()
    {
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryAdjustSets(Session, "squat", 1, out var more, out _));
        Assert.Equal(5, (int)JsonNode.Parse(more)!["exercises"]![0]!["sets"]!);
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TryAdjustSets(more, "squat", 1, out _, out _));      // max 5
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TryAdjustSets(Session, "curl", -1, out _, out _));    // min 1
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryAdjustSets(Session, "squat", -1, out var less, out _));
        var squat = JsonNode.Parse(less)!["exercises"]![0]!;
        Assert.Equal(3, (int)squat["sets"]!);
        Assert.Equal(4, (int)squat["adaptationOriginalSets"]!);
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TryAdjustSets(Session, "nope", 1, out _, out _));
    }
}
