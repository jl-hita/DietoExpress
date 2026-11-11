using System.IO;
using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>El generador de mesociclos tiene en cuenta el bloque anterior del mismo plan.</summary>
public sealed class TrainingBlockHistoryTests
{
    private static readonly DayOfWeek[] Days = [DayOfWeek.Monday, DayOfWeek.Thursday];

    private static TrainingExerciseCandidate Squat(string id, int priority) => new(
        id, "catalog-test", $"Sentadilla {id}", TrainingMovementPattern.Squat, [],
        [TrainingGoal.Hypertrophy], TrainingExperience.Beginner, priority, true,
        [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings]);

    private static TrainingExerciseCandidate[] Catalog() =>
        TrainingBlockProgressionTests.Catalog()
            .Where(c => c.MovementPattern != TrainingMovementPattern.Squat)
            .Concat([Squat("squat-a", 80), Squat("squat-b", 70), Squat("squat-c", 65)])
            .ToArray();

    private static TrainingProgramPrescription Generate(
        int weeks, IReadOnlyList<string>? previous, bool continues = false, bool deload = false, int? rotation = null)
    {
        var result = TrainingProgramGenerator.Generate(
            new(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, new DateOnly(2026, 11, 9), weeks,
                Days, 90, [], [], null, deload, TrainingProgramSplit.FullBody, rotation, null, previous, continues),
            Catalog());
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        return result.Prescription!;
    }

    private static string SquatOf(TrainingProgramPrescription plan) =>
        plan.Sessions.SelectMany(s => s.Exercises).First(e => e.ExerciseId.StartsWith("squat-", StringComparison.Ordinal)).ExerciseId;

    [Fact]
    public void TrainingBlockMovesToAnEquallySuitableExerciseNotUsedByThePreviousBlock()
    {
        Assert.Equal("squat-a", SquatOf(Generate(4, null)));
        Assert.Equal("squat-b", SquatOf(Generate(4, ["squat-a"])));
        Assert.Equal("squat-a", SquatOf(Generate(4, ["squat-b"])));
    }

    [Fact]
    public void HistoryDecidesRotationInsteadOfTheBlockNumber()
    {
        // Un índice de rotación alto no altera el resultado cuando hay historial real del bloque anterior.
        Assert.Equal(SquatOf(Generate(4, ["squat-a"], rotation: 0)), SquatOf(Generate(4, ["squat-a"], rotation: 5)));
    }

    [Fact]
    public void DeloadKeepsTheExercisesOfThePreviousBlock()
    {
        Assert.Equal("squat-b", SquatOf(Generate(1, ["squat-b"], deload: true)));
    }

    [Fact]
    public void BlockContinuingATrainingBlockDoesNotRepeatTheFullIntroductionWeek()
    {
        var fresh = Generate(4, null);
        var continued = Generate(4, ["squat-a"], continues: true);
        var freshW1 = fresh.Sessions.Where(s => s.WeekNumber == 1).SelectMany(s => s.Exercises).ToArray();
        var freshW3 = fresh.Sessions.Where(s => s.WeekNumber == 3).SelectMany(s => s.Exercises).ToArray();
        var contW1 = continued.Sessions.Where(s => s.WeekNumber == 1).SelectMany(s => s.Exercises).ToArray();
        var contW3 = continued.Sessions.Where(s => s.WeekNumber == 3).SelectMany(s => s.Exercises).ToArray();

        // Sin continuidad se recortan series y se sube 1 RIR; con continuidad no se recortan y solo se sube 0,5.
        Assert.Contains(freshW1.Zip(freshW3), pair => pair.First.Sets < pair.Second.Sets);
        Assert.All(contW1.Zip(contW3), pair => Assert.Equal(pair.Second.Sets, pair.First.Sets));
        Assert.All(contW1.Zip(contW3), pair => Assert.Equal(pair.Second.TargetRepetitionsInReserve + 0.5m, pair.First.TargetRepetitionsInReserve));
    }

    [Fact]
    public void ControllerAppliesTheSameHistoryToPreviewAndSavedGeneration()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Anguloso.sln"))) dir = dir.Parent;
        var controller = TrainingControllerSource.ReadAll().Replace("\r\n", "\n");
        var ui = TrainingComponentSource.ReadAll();

        Assert.Equal(2, controller.Split("WithHistoryAsync(").Length - 1 - 1); // definición + 2 usos = 3 apariciones
        Assert.Contains("GetPreviousBlockAsync", controller);
        Assert.Contains("COALESCE(end_date, start_date) < @start", controller);
        Assert.Contains("generate-preview?planId=", ui);
    }
}
