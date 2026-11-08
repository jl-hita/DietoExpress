using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingProgramQualityDiagnosticsTests
{
    [Fact]
    public void StrengthAllocatorCapsEveryExerciseAtThreeWorkingSets()
    {
        var session = Session(1, DayOfWeek.Monday, "Fuerza", Exercise(
            "bench-press", "Press banca", 5,
            [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps]));

        var result = TrainingProgramVolumeAllocator.Allocate(
            [session], TrainingGoal.Strength, TrainingExperience.Advanced, null, 180);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.All(result.Sessions.SelectMany(item => item.Exercises),
            exercise => Assert.InRange(exercise.Sets, 1, 4));
    }

    [Fact]
    public void HypertrophyAllocatorKeepsItsExistingFiveSetCeiling()
    {
        var session = Session(1, DayOfWeek.Monday, "Hipertrofia", Exercise(
            "bench-press", "Press banca", 5,
            [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps]));

        var result = TrainingProgramVolumeAllocator.Allocate(
            [session], TrainingGoal.Hypertrophy, TrainingExperience.Advanced, null, 180);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(5, Assert.Single(Assert.Single(result.Sessions).Exercises).Sets);
    }

    [Fact]
    public void StrengthQualityWarnsWhenAMajorMuscleGetsOnlyOneEffectiveWeeklyExposure()
    {
        var plan = Plan("Strength",
            Session(1, DayOfWeek.Monday, "Torso",
                Exercise("bench-press", "Press banca", 2, [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps]),
                Exercise("barbell-row", "Remo con barra", 2, [TrainingMuscleGroup.UpperBack], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats]),
                Exercise("dumbbell-shoulder-press", "Press hombro", 2, [TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.Triceps])),
            Session(1, DayOfWeek.Thursday, "Pierna",
                Exercise("squat", "Sentadilla", 2, [TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.Glutes]),
                Exercise("deadlift", "Peso muerto", 2, [TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings]),
                Exercise("standing-calf-raise", "Gemelos", 2, [TrainingMuscleGroup.Calves], [])));

        var report = TrainingProgramQualityAnalyzer.Analyze(plan);

        Assert.Contains(report.Warnings, warning =>
            warning.Contains("Chest", StringComparison.Ordinal) &&
            warning.Contains("1 sesión", StringComparison.Ordinal) &&
            warning.Contains("dos días por semana", StringComparison.Ordinal));
        Assert.Contains(report.Warnings, warning =>
            warning.Contains("Quadriceps", StringComparison.Ordinal) &&
            warning.Contains("1 sesión", StringComparison.Ordinal));
    }

    [Fact]
    public void StrengthQualityDoesNotWarnWhenMajorMusclesReceiveTwoEffectiveWeeklyExposures()
    {
        var exercises = new[]
        {
            Exercise("bench-press", "Press banca", 2, [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps]),
            Exercise("barbell-row", "Remo con barra", 2, [TrainingMuscleGroup.UpperBack], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats]),
            Exercise("dumbbell-shoulder-press", "Press hombro", 2, [TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.Triceps]),
            Exercise("squat", "Sentadilla", 2, [TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.Glutes]),
            Exercise("deadlift", "Peso muerto", 2, [TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings]),
            Exercise("standing-calf-raise", "Gemelos", 2, [TrainingMuscleGroup.Calves], [])
        };
        var plan = Plan("Strength",
            Session(1, DayOfWeek.Monday, "Cuerpo completo A", exercises),
            Session(1, DayOfWeek.Thursday, "Cuerpo completo B", exercises));

        var report = TrainingProgramQualityAnalyzer.Analyze(plan);

        Assert.DoesNotContain(report.Warnings, warning =>
            warning.Contains("dos días por semana", StringComparison.Ordinal));
    }

    [Fact]
    public void HypertrophyAndGeneralFitnessAlsoWarnAboutSingleWeeklyExposure()
    {
        var plan = Plan("Hypertrophy",
            Session(1, DayOfWeek.Monday, "Torso",
                Exercise("bench-press", "Press banca", 2, [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps])));

        var report = TrainingProgramQualityAnalyzer.Analyze(plan);

        Assert.Contains(report.Warnings, warning =>
            warning.Contains("dos días por semana", StringComparison.Ordinal));

        foreach (var goal in new[] { "GeneralFitness" })
            Assert.Contains(TrainingProgramQualityAnalyzer.Analyze(Plan(goal, plan.Sessions.ToArray())).Warnings,
                warning => warning.Contains("Chest", StringComparison.Ordinal) && warning.Contains("dos días por semana", StringComparison.Ordinal));

        // Manual single-session previews must not be flagged: they only show one isolated session.
        Assert.DoesNotContain(TrainingProgramQualityAnalyzer.Analyze(Plan("Manual", plan.Sessions.ToArray())).Warnings,
            warning => warning.Contains("dos días por semana", StringComparison.Ordinal));
    }

    [Fact]
    public void MoreThanThirtyWorkingSetsInOneSessionProducesANonBlockingReviewWarning()
    {
        var exercises = Enumerable.Range(1, 7)
            .Select(index => Exercise($"test-exercise-{index}", $"Ejercicio {index}", 5,
                [TrainingMuscleGroup.Chest], []))
            .ToArray();
        var report = TrainingProgramQualityAnalyzer.Analyze(
            Plan("Hypertrophy", Session(1, DayOfWeek.Monday, "Sesión densa", exercises)));

        Assert.Contains(report.Warnings, warning =>
            warning.Contains("35 series de trabajo", StringComparison.Ordinal) &&
            warning.Contains("umbral interno de revisión de 30", StringComparison.Ordinal));
    }

    [Fact]
    public void ThirtyWorkingSetsDoNotTriggerTheSessionDensityWarning()
    {
        var exercises = Enumerable.Range(1, 6)
            .Select(index => Exercise($"test-exercise-{index}", $"Ejercicio {index}", 5,
                [TrainingMuscleGroup.Chest], []))
            .ToArray();
        var report = TrainingProgramQualityAnalyzer.Analyze(
            Plan("Hypertrophy", Session(1, DayOfWeek.Monday, "Sesión límite", exercises)));

        Assert.DoesNotContain(report.Warnings, warning =>
            warning.Contains("umbral interno de revisión de 30", StringComparison.Ordinal));
    }

    private static TrainingProgramPrescription Plan(
        string goal, params TrainingSessionPrescription[] sessions) =>
        new("quality-diagnostics-v1", goal, new DateOnly(2026, 11, 8), 1, sessions);

    private static TrainingSessionPrescription Session(
        int week, DayOfWeek day, string title, params TrainingExercisePrescription[] exercises) =>
        new(week, day, title, 60, exercises);

    private static TrainingExercisePrescription Exercise(
        string id,
        string name,
        int sets,
        IReadOnlyList<TrainingMuscleGroup> primary,
        IReadOnlyList<TrainingMuscleGroup> secondary) =>
        new(id, "catalog-v1", name, 1, sets, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "Prueba de regresión.",
            primary, secondary);
}
