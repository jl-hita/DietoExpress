using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingProgramVolumeAllocatorTests
{
    [Fact]
    public void SecondaryContributionIsReportedAtHalfCreditWithoutReplacingRawCounts()
    {
        var session = Session(new TrainingExercisePrescription(
            "bench", "v1", "Press banca", 1, 2, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test",
            [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps]));

        var volume = TrainingProgramVolumeAllocator.CalculateEffectiveVolume([session]);

        Assert.Equal(2m, volume[TrainingMuscleGroup.Chest]);
        Assert.Equal(1m, volume[TrainingMuscleGroup.Triceps]);
        Assert.Equal(0.5m, TrainingProgramVolumeAllocator.SecondarySetCredit);
    }

    [Fact]
    public void PriorityAndExplicitTargetsChangeOnlyTheRequestedMuscleDose()
    {
        var sessions = new[]
        {
            Session(
                new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], []),
                new TrainingExercisePrescription("row", "v1", "Remo", 2, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.UpperBack], [TrainingMuscleGroup.Biceps])),
            new TrainingSessionPrescription(1, DayOfWeek.Wednesday, "Sesión B", 5,
            [
                new TrainingExercisePrescription("incline", "v1", "Press inclinado", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], [])
            ])
        };

        var defaultAllocation = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner, null, 60);
        var priorityAllocation = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, Priority: 2)], 60);
        var explicitAllocation = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, TargetWeeklyPrimarySets: 4m)], 60);

        Assert.True(defaultAllocation.Succeeded, string.Join("; ", defaultAllocation.Errors));
        Assert.True(priorityAllocation.Succeeded, string.Join("; ", priorityAllocation.Errors));
        Assert.True(explicitAllocation.Succeeded, string.Join("; ", explicitAllocation.Errors));

        var defaultChest = TrainingProgramVolumeAllocator.CalculateEffectiveVolume(defaultAllocation.Sessions)[TrainingMuscleGroup.Chest];
        var priorityChest = TrainingProgramVolumeAllocator.CalculateEffectiveVolume(priorityAllocation.Sessions)[TrainingMuscleGroup.Chest];
        var explicitChest = TrainingProgramVolumeAllocator.CalculateEffectiveVolume(explicitAllocation.Sessions)[TrainingMuscleGroup.Chest];

        Assert.True(priorityChest > defaultChest);
        Assert.Equal(4m, explicitChest);
    }

    [Fact]
    public void DefaultWeeklyTargetsFocusOnMainGroupsAndOnlyAddAccessoryGroupsWhenPrioritized()
    {
        var sessions = new[]
        {
            Session(
                new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], []),
                new TrainingExercisePrescription("curl", "v1", "Curl de bíceps", 2, 1, 8, 12, 2m, 90,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Biceps], []),
                new TrainingExercisePrescription("knee-raise", "v1", "Elevación de rodillas", 3, 1, 8, 12, 2m, 90,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.HipFlexors], []))
        };

        var defaults = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner, null, 60);
        Assert.True(defaults.Succeeded, string.Join("; ", defaults.Errors));
        Assert.True(defaults.Sessions[0].Exercises.Single(x => x.ExerciseId == "bench").Sets > 1);
        Assert.Equal(3, defaults.Sessions[0].Exercises.Single(x => x.ExerciseId == "curl").Sets);
        Assert.Equal(1, defaults.Sessions[0].Exercises.Single(x => x.ExerciseId == "knee-raise").Sets);

        var prioritized = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.HipFlexors, Priority: 1)], 60);
        Assert.True(prioritized.Succeeded, string.Join("; ", prioritized.Errors));
        Assert.True(prioritized.Sessions[0].Exercises.Single(x => x.ExerciseId == "knee-raise").Sets > 1);
    }

    [Fact]
    public void AllocationIsDeterministicAndRespectsPerExerciseSetCeiling()
    {
        var sessions = new[]
        {
            Session(
                new TrainingExercisePrescription("squat", "v1", "Sentadilla", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings]),
                new TrainingExercisePrescription("leg-press", "v1", "Prensa", 2, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], []))
        };

        var first = TrainingProgramVolumeAllocator.Allocate(sessions, TrainingGoal.Hypertrophy,
            TrainingExperience.Advanced, null, 60);
        var second = TrainingProgramVolumeAllocator.Allocate(sessions, TrainingGoal.Hypertrophy,
            TrainingExperience.Advanced, null, 60);

        Assert.True(first.Succeeded, string.Join("; ", first.Errors));
        Assert.Equal(
            first.Sessions.SelectMany(x => x.Exercises).Select(x => (x.ExerciseId, x.Sets)),
            second.Sessions.SelectMany(x => x.Exercises).Select(x => (x.ExerciseId, x.Sets)));
        Assert.All(first.Sessions.SelectMany(x => x.Exercises), exercise => Assert.InRange(exercise.Sets, 1, 5));
    }


    [Fact]
    public void AllocationCanUseACompoundForAnUnderTargetMuscleWhenAnotherPrimaryMuscleIsAlreadyAtTarget()
    {
        var sessions = new[]
        {
            Session(
                new TrainingExercisePrescription("squat", "v1", "Sentadilla", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test",
                    [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], []),
                new TrainingExercisePrescription("glute-bridge", "v1", "Puente de glúteos", 2, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test",
                    [TrainingMuscleGroup.Glutes], []))
        };

        var result = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [
                new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Quadriceps, TargetWeeklyPrimarySets: 4m),
                new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Glutes, TargetWeeklyPrimarySets: 2m)
            ], 60);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var volume = TrainingProgramVolumeAllocator.CalculatePrimaryVolume(result.Sessions);
        Assert.Equal(2m, volume[TrainingMuscleGroup.Quadriceps]);
        Assert.Equal(3m, volume[TrainingMuscleGroup.Glutes]);
        Assert.Contains(result.Warnings, warning => warning.Contains("Quadriceps alcanza", StringComparison.Ordinal));
    }

    [Fact]
    public void AllocationDistributesTheSameAccessoryAcrossSessionsBeforeMaxingOneSession()
    {
        var sessions = new[]
        {
            Session(new TrainingExercisePrescription("biceps-curl", "v1", "Curl de bíceps", 1, 1, 10, 15, 2m, 90,
                TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Biceps], [])),
            new TrainingSessionPrescription(1, DayOfWeek.Thursday, "Sesión B", 5,
            [
                new TrainingExercisePrescription("biceps-curl", "v1", "Curl de bíceps", 1, 1, 10, 15, 2m, 90,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Biceps], [])
            ])
        };

        var result = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner, null, 60);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(6, result.Sessions.Sum(session => session.Exercises.Single().Sets));
        Assert.Equal(new[] { 3, 3 }, result.Sessions.Select(session => session.Exercises.Single().Sets));
    }

    [Fact]
    public void DurationLimitPreventsUnboundedVolumeAndExplainsUnmetTarget()
    {
        var sessions = new[]
        {
            Session(new TrainingExercisePrescription("squat", "v1", "Sentadilla", 1, 1, 8, 12, 2m, 180,
                TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Quadriceps], []))
        };

        var result = TrainingProgramVolumeAllocator.Allocate(sessions, TrainingGoal.Hypertrophy,
            TrainingExperience.Advanced, null, 35);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.InRange(result.Sessions[0].EstimatedDurationMinutes, 1, 35);
        Assert.Contains(result.Warnings, warning => warning.Contains("Quadriceps", StringComparison.Ordinal));
    }

    [Fact]
    public void FractionalWeeklySetTargetsAreRejected()
    {
        var session = Session(new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], []));

        var result = TrainingProgramVolumeAllocator.Allocate([session], TrainingGoal.Hypertrophy,
            TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, TargetWeeklyPrimarySets: 4.5m)], 60);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("entero", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateOrOutOfRangeMusclePreferencesAreRejected()
    {
        var session = Session(new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], []));

        var result = TrainingProgramVolumeAllocator.Allocate([session], TrainingGoal.Hypertrophy,
            TrainingExperience.Beginner,
            [
                new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, Priority: 1),
                new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, Priority: 2)
            ], 60);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("repite", StringComparison.Ordinal));
    }

    private static TrainingSessionPrescription Session(params TrainingExercisePrescription[] exercises) =>
        new(1, DayOfWeek.Monday, "Sesión", 5, exercises);
}
