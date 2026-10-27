using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingProgramGeneratorTests
{
    [Fact]
    public void GeneratesDeterministicPlanForAllRequestedWeeksAndDays()
    {
        var request = Request();
        var first = TrainingProgramGenerator.Generate(request, Catalog());
        var second = TrainingProgramGenerator.Generate(request, Catalog());

        Assert.True(first.Succeeded, string.Join("; ", first.Errors));
        Assert.NotNull(first.Prescription);
        Assert.Equal(8, first.Prescription.Sessions.Count);
        Assert.Equal(
            first.Prescription.Sessions.Select(SessionSignature),
            second.Prescription!.Sessions.Select(SessionSignature));
        Assert.Empty(TrainingPrescriptionValidator.Validate(first.Prescription));
    }

    [Fact]
    public void GeneratorRespectsRequestedDaysAndSessionDuration()
    {
        var request = Request() with
        {
            TrainingDays = [DayOfWeek.Tuesday, DayOfWeek.Thursday],
            MaximumSessionDurationMinutes = 35
        };

        var result = TrainingProgramGenerator.Generate(request, Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.All(result.Prescription!.Sessions, session =>
        {
            Assert.Contains(session.Day, new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday });
            Assert.InRange(session.EstimatedDurationMinutes, 1, 35);
        });
        Assert.Equal(8, result.Prescription.Sessions.Count);
    }

    [Fact]
    public void ExcludedExerciseIsNeverSelected()
    {
        var result = TrainingProgramGenerator.Generate(
            Request() with { ExcludedExerciseIds = ["squat-basic"] },
            Catalog().Append(new TrainingExerciseCandidate(
                "squat-alternative", "catalog-2026-10", "Sentadilla alternativa",
                TrainingMovementPattern.Squat, [],
                [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
                TrainingExperience.Beginner, 40, true)).ToArray());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.DoesNotContain(result.Prescription!.Sessions.SelectMany(x => x.Exercises),
            exercise => exercise.ExerciseId == "squat-basic");
    }

    [Fact]
    public void UnreviewedOrEquipmentIncompatibleExercisesAreNotSelected()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { IsReviewed = false }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No hay un ejercicio revisado compatible", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidAvailabilityIsRejectedBeforeGeneration()
    {
        var request = Request() with
        {
            TrainingDays = [DayOfWeek.Monday, DayOfWeek.Monday]
        };

        var result = TrainingProgramGenerator.Generate(request, Catalog());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No se puede repetir un día", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateCatalogIdsAreRejected()
    {
        var catalog = Catalog().Append(Catalog()[0]).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("ID duplicado", StringComparison.Ordinal));
    }

    [Fact]
    public void UndefinedSuitableGoalIsRejected()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { SuitableGoals = [(TrainingGoal)999] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("objetivo compatible no válido", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateSuitableGoalsAreRejected()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { SuitableGoals = [TrainingGoal.Hypertrophy, TrainingGoal.Hypertrophy] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("repite objetivos compatibles", StringComparison.Ordinal));
    }

    [Fact]
    public void CandidateSelectionHonorsRequiredEquipmentAndGoal()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with
                {
                    ExerciseId = "squat-gym-only",
                    RequiredEquipment = ["barbell"],
                    SuitableGoals = [TrainingGoal.Strength]
                }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(
            Request() with { Goal = TrainingGoal.Hypertrophy, AvailableEquipment = [] },
            catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No hay un ejercicio revisado compatible", StringComparison.Ordinal));
    }

    private static string SessionSignature(TrainingSessionPrescription session) =>
        $"{session.WeekNumber}|{session.Day}|{session.Title}|" +
        string.Join(",", session.Exercises.Select(exercise =>
            $"{exercise.ExerciseId}:{exercise.Order}:{exercise.Sets}:{exercise.MinRepetitions}-{exercise.MaxRepetitions}"));

    private static TrainingProgramGenerationRequest Request() => new(
        TrainingGoal.Hypertrophy,
        TrainingExperience.Beginner,
        new DateOnly(2026, 10, 26),
        4,
        [DayOfWeek.Monday, DayOfWeek.Thursday],
        60,
        [],
        []);

    private static TrainingExerciseCandidate[] Catalog()
    {
        var patterns = Enum.GetValues<TrainingMovementPattern>();
        return patterns.Select(pattern => new TrainingExerciseCandidate(
            pattern == TrainingMovementPattern.Squat ? "squat-basic" : $"exercise-{pattern.ToString().ToLowerInvariant()}",
            "catalog-2026-10",
            $"Ejercicio {pattern}",
            pattern,
            [],
            [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
            TrainingExperience.Beginner,
            50,
            true)).ToArray();
    }
}
