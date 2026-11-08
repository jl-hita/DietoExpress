using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingBlockProgressionTests
{
    [Fact]
    public void FourWeekBlockStartsWithAnIntroductoryWeekAndThenRaisesEffort()
    {
        var plan = Generate(weeks: 4);
        var week1 = Rir(plan, 1);
        var week3 = Rir(plan, 3);
        Assert.True(week1.Zip(week3, (a, b) => a == b + 1m).All(x => x), "RIR de la semana 1 = RIR base + 1");
        Assert.Equal(Rir(plan, 3), Rir(plan, 4));
        Assert.All(plan.Sessions.SelectMany(s => s.Exercises), e => Assert.InRange(e.TargetRepetitionsInReserve!.Value, 0m, 5m));
    }

    [Fact]
    public void FourWeekBlockTrimsOneSetOnlyInWeekOneForExercisesWithThreeOrMoreSets()
    {
        var plan = Generate(weeks: 4, goal: TrainingGoal.Hypertrophy, days: 2, minutes: 120, experience: TrainingExperience.Advanced);
        var w1 = plan.Sessions.Where(s => s.WeekNumber == 1).SelectMany(s => s.Exercises).ToArray();
        var w3 = plan.Sessions.Where(s => s.WeekNumber == 3).SelectMany(s => s.Exercises).ToArray();
        Assert.Equal(w3.Length, w1.Length);
        Assert.All(w1.Zip(w3), pair => Assert.Equal(
            pair.Second.Sets >= 3 ? pair.Second.Sets - 1 : pair.Second.Sets, pair.First.Sets));
        Assert.Contains(w3, e => e.Sets >= 3);
    }

    [Fact]
    public void ShortBlocksAreNotModified()
    {
        var plan = Generate(weeks: 2);
        Assert.Equal(Rir(plan, 1), Rir(plan, 2));
        Assert.Equal(
            plan.Sessions.Where(s => s.WeekNumber == 1).SelectMany(s => s.Exercises).Select(e => e.Sets),
            plan.Sessions.Where(s => s.WeekNumber == 2).SelectMany(s => s.Exercises).Select(e => e.Sets));
    }

    [Fact]
    public void FiveWeekBlockEasesInOverTwoWeeks()
    {
        var plan = Generate(weeks: 5);
        var w1 = Rir(plan, 1); var w2 = Rir(plan, 2); var w3 = Rir(plan, 3);
        Assert.True(w1.Zip(w2, (a, b) => a > b).All(x => x));
        Assert.True(w2.Zip(w3, (a, b) => a > b || a == 5m).All(x => x));
    }

    internal static TrainingProgramPrescription Generate(
        int weeks, TrainingGoal goal = TrainingGoal.Hypertrophy, int days = 2, int minutes = 90,
        TrainingExperience experience = TrainingExperience.Intermediate, TrainingProgramSplit split = TrainingProgramSplit.Automatic)
    {
        var all = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };
        var result = TrainingProgramGenerator.Generate(
            new(goal, experience, new DateOnly(2026, 11, 9), weeks, all.Take(days).ToArray(), minutes, [], [], null, false, split),
            Catalog());
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return result.Prescription!;
    }

    private static decimal[] Rir(TrainingProgramPrescription plan, int week) =>
        plan.Sessions.Where(s => s.WeekNumber == week).SelectMany(s => s.Exercises)
            .Select(e => e.TargetRepetitionsInReserve ?? 0m).ToArray();

    internal static TrainingExerciseCandidate[] Catalog() =>
        Enum.GetValues<TrainingMovementPattern>().Select(pattern =>
        {
            var (primary, secondary) = Muscles(pattern);
            return new TrainingExerciseCandidate($"ex-{pattern}", "catalog-test", $"Ejercicio {pattern}", pattern, [],
                [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
                TrainingExperience.Beginner, 50, true, primary, secondary);
        }).ToArray();

    private static (IReadOnlyList<TrainingMuscleGroup>, IReadOnlyList<TrainingMuscleGroup>) Muscles(TrainingMovementPattern p) => p switch
    {
        TrainingMovementPattern.Squat => ([TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings]),
        TrainingMovementPattern.Hinge => ([TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.ErectorSpinae]),
        TrainingMovementPattern.HorizontalPush => ([TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps]),
        TrainingMovementPattern.VerticalPush => ([TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.Triceps]),
        TrainingMovementPattern.HorizontalPull => ([TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps]),
        TrainingMovementPattern.VerticalPull => ([TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps]),
        TrainingMovementPattern.SingleLeg => ([TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Calves]),
        TrainingMovementPattern.Core => ([TrainingMuscleGroup.Abdominals], [TrainingMuscleGroup.Obliques]),
        _ => ([TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.Triceps])
    };
}
