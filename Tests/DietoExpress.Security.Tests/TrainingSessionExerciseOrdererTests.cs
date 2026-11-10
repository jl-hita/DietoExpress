using Anguloso.Server.Logica;
using System.IO;
using System.Reflection;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingSessionExerciseOrdererTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    [Fact]
    public void GeneratorAllocatesWeeklyVolumeBeforeApplyingExerciseOrdering()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "TrainingProgramGenerator.cs"));
        var allocation = source.IndexOf("TrainingProgramVolumeAllocator.Allocate(", StringComparison.Ordinal);
        var ordering = source.IndexOf("TrainingSessionExerciseOrderer.Reorder(", StringComparison.Ordinal);

        Assert.True(allocation >= 0 && ordering > allocation,
            "La ordenación de ejercicios debe ejecutarse después de asignar las series semanales para no alterar los desempates del asignador.");
    }

    [Fact]
    public void KeepsFirstLiftAndMovesIndependentCompoundAheadOfOverlappingPress()
    {
        var bench = Exercise("bench", TrainingMovementPattern.HorizontalPush,
            [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Deltoids]);
        var overhead = Exercise("overhead", TrainingMovementPattern.VerticalPush,
            [TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Chest]);
        var row = Exercise("row", TrainingMovementPattern.HorizontalPull,
            [TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps]);
        var input = new[]
        {
            Prescription(bench, 1),
            Prescription(overhead, 2),
            Prescription(row, 3)
        };

        var result = Reorder(input, [bench, overhead, row], null);

        Assert.Equal(new[] { "bench", "row", "overhead" }, result.Select(x => x.ExerciseId));
        Assert.Equal(new[] { 1, 2, 3 }, result.Select(x => x.Order));
    }

    [Fact]
    public void KeepsMainPatternsBeforeAccessoriesAndCoreAndDoesNotChangePrescription()
    {
        var squat = Exercise("squat", TrainingMovementPattern.Squat,
            [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings]);
        var legExtension = Exercise("extension", TrainingMovementPattern.Accessory,
            [TrainingMuscleGroup.Quadriceps], []);
        var core = Exercise("core", TrainingMovementPattern.Core,
            [TrainingMuscleGroup.Abdominals], []);
        var hinge = Exercise("hinge", TrainingMovementPattern.Hinge,
            [TrainingMuscleGroup.Hamstrings], [TrainingMuscleGroup.Glutes]);
        var input = new[]
        {
            Prescription(squat, 1, sets: 3),
            Prescription(legExtension, 2, sets: 2),
            Prescription(core, 3, sets: 1),
            Prescription(hinge, 4, sets: 4)
        };

        var result = Reorder(input, [squat, legExtension, core, hinge], null);

        Assert.Equal(new[] { "squat", "hinge", "extension", "core" }, result.Select(x => x.ExerciseId));
        Assert.Equal(new[] { 3, 4, 2, 1 }, result.Select(x => x.Sets));
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Select(x => x.Order));
        foreach (var exercise in result)
        {
            var original = input.Single(item => item.ExerciseId == exercise.ExerciseId);
            Assert.Equal(original with { Order = exercise.Order }, exercise);
        }
    }

    [Fact]
    public void UsesMusclePriorityAsTieBreakerAndIsDeterministic()
    {
        var squat = Exercise("squat", TrainingMovementPattern.Squat,
            [TrainingMuscleGroup.Quadriceps], []);
        var chest = Exercise("chest", TrainingMovementPattern.HorizontalPush,
            [TrainingMuscleGroup.Chest], []);
        var back = Exercise("back", TrainingMovementPattern.HorizontalPull,
            [TrainingMuscleGroup.UpperBack], []);
        var input = new[] { Prescription(squat, 1), Prescription(back, 2), Prescription(chest, 3) };
        var preferences = new[] { new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, Priority: 3) };

        var first = Reorder(input, [squat, back, chest], preferences);
        var second = Reorder(input, [squat, back, chest], preferences);

        Assert.Equal(new[] { "squat", "chest", "back" }, first.Select(x => x.ExerciseId));
        Assert.Equal(first.Select(x => x.ExerciseId), second.Select(x => x.ExerciseId));
    }

    private static IReadOnlyList<TrainingExercisePrescription> Reorder(
        IReadOnlyList<TrainingExercisePrescription> exercises,
        IEnumerable<TrainingExerciseCandidate> catalog,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences)
    {
        // The orderer is an implementation detail. Invoke it through reflection rather than
        // widening its production visibility solely to satisfy a test in another assembly.
        var ordererType = typeof(TrainingProgramGenerator).Assembly.GetType(
            "Anguloso.Server.Logica.TrainingSessionExerciseOrderer",
            throwOnError: true)!;
        var method = ordererType.GetMethod("Reorder", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("TrainingSessionExerciseOrderer.Reorder was not found.");
        var result = method.Invoke(null, new object?[] { exercises, catalog, preferences });
        return Assert.IsAssignableFrom<IReadOnlyList<TrainingExercisePrescription>>(result);
    }

    private static TrainingExerciseCandidate Exercise(
        string id,
        TrainingMovementPattern pattern,
        IReadOnlyList<TrainingMuscleGroup> primary,
        IReadOnlyList<TrainingMuscleGroup> secondary) =>
        new(id, "test", id, pattern, [], [TrainingGoal.Hypertrophy], TrainingExperience.Beginner,
            50, true, primary, secondary);

    private static TrainingExercisePrescription Prescription(TrainingExerciseCandidate candidate, int order, int sets = 2) =>
        new(candidate.ExerciseId, candidate.CatalogVersion, candidate.Name, order, sets, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test", candidate.PrimaryMuscleGroups,
            candidate.SecondaryMuscleGroups);
}
