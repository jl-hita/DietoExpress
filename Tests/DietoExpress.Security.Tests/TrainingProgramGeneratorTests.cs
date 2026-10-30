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
    public void SessionDurationEstimateAccountsForGoalRestAndWorkInsteadOfFixedPatternCount()
    {
        var general = TrainingProgramGenerator.Generate(
            Request() with { Goal = TrainingGoal.GeneralFitness, Experience = TrainingExperience.Advanced }, Catalog());
        var strength = TrainingProgramGenerator.Generate(
            Request() with { Goal = TrainingGoal.Strength, Experience = TrainingExperience.Advanced }, Catalog());

        Assert.True(general.Succeeded, string.Join("; ", general.Errors));
        Assert.True(strength.Succeeded, string.Join("; ", strength.Errors));
        Assert.True(
            strength.Prescription!.Sessions.Max(x => x.EstimatedDurationMinutes) >
            general.Prescription!.Sessions.Max(x => x.EstimatedDurationMinutes));
        Assert.All(strength.Prescription.Sessions, session => Assert.InRange(session.EstimatedDurationMinutes, 1, 60));
    }

    [Fact]
    public void AdvancedHypertrophyKeepsBuildWeekPrescriptionAndReportsHighVolumeForReview()
    {
        foreach (var days in new[]
        {
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }
        })
        {
            var result = TrainingProgramGenerator.Generate(Request() with
            {
                Goal = TrainingGoal.Hypertrophy,
                Experience = TrainingExperience.Advanced,
                TrainingDays = days
            }, Catalog());

            Assert.True(result.Succeeded, string.Join("; ", result.Errors));
            var plan = result.Prescription!;
            var buildWeekLowerBody = plan.Sessions
                .Where(session => session.WeekNumber is 2 or 3)
                .SelectMany(session => session.Exercises)
                .Where(exercise => exercise.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Glutes))
                .ToArray();

            Assert.Contains(buildWeekLowerBody, exercise => exercise.Sets == 4);
            Assert.Contains(plan.QualityReport!.Warnings,
                warning => warning.Contains("series primarias", StringComparison.Ordinal));
            Assert.NotEmpty(plan.QualityReport.WeeklyMuscleVolumes);
        }
    }

    [Fact]
    public void GeneratorKeepsExerciseSelectionAcrossMesocycleWeeksWhenCompatible()
    {
        var alternative = new TrainingExerciseCandidate(
            "squat-alternative", "catalog-2026-10", "Sentadilla alternativa",
            TrainingMovementPattern.Squat, [],
            [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
            TrainingExperience.Beginner, 40, true,
            [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes],
            [TrainingMuscleGroup.Hamstrings]);
        var result = TrainingProgramGenerator.Generate(
            Request() with { DurationWeeks = 4 },
            Catalog().Append(alternative).ToArray());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var squatByWeek = result.Prescription!.Sessions
            .SelectMany(session => session.Exercises.Select(exercise => new { session.WeekNumber, Exercise = exercise }))
            .Where(item => item.Exercise.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Quadriceps))
            .GroupBy(item => item.WeekNumber)
            .ToDictionary(group => group.Key, group => group.First().Exercise.ExerciseId);

        Assert.Equal("squat-basic", squatByWeek[1]);
        Assert.All(squatByWeek.Values, exerciseId => Assert.Equal(squatByWeek[1], exerciseId));
    }

    [Fact]
    public void GeneratorUsesDifferentHorizontalPullExercisesWithinWeekWhenAlternativesExist()
    {
        var alternative = new TrainingExerciseCandidate(
            "horizontal-pull-alternative", "catalog-2026-10", "Remo alternativo",
            TrainingMovementPattern.HorizontalPull, [],
            [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
            TrainingExperience.Beginner, 40, true,
            [TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Lats],
            [TrainingMuscleGroup.Biceps]);
        var result = TrainingProgramGenerator.Generate(
            Request() with
            {
                DurationWeeks = 4,
                TrainingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday]
            },
            Catalog().Append(alternative).ToArray());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var weeklyPulls = result.Prescription!.Sessions
            .Where(session => session.WeekNumber == 1)
            .SelectMany(session => session.Exercises)
            .Where(exercise => exercise.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.UpperBack))
            .Select(exercise => exercise.ExerciseId)
            .ToArray();

        Assert.Equal(2, weeklyPulls.Length);
        Assert.Equal(2, weeklyPulls.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void MissingPatternErrorReportsCandidateFilteringStages()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.HorizontalPull
                ? exercise with { IsReviewed = false }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Contains("patrón 'HorizontalPull'", error);
        Assert.Contains("revisados/propios=", error);
        Assert.Contains("objetivo=", error);
        Assert.Contains("tras exclusiones=", error);
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
                TrainingExperience.Beginner, 40, true,
                [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes],
                [TrainingMuscleGroup.ErectorSpinae])).ToArray());

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
    public void OwnerCanGenerateWithTheirOwnPendingExerciseButNotAnUnownedPendingExercise()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { IsReviewed = false, IsOwnedByCurrentProfessional = true }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Contains(result.Prescription!.Sessions.SelectMany(x => x.Exercises), exercise => exercise.ExerciseId == "squat-basic");

        var notOwned = catalog.Select(exercise => exercise with { IsOwnedByCurrentProfessional = false }).ToArray();
        var denied = TrainingProgramGenerator.Generate(Request(), notOwned);
        Assert.False(denied.Succeeded);
        Assert.Contains(denied.Errors, error => error.Contains("No hay un ejercicio revisado compatible", StringComparison.Ordinal));
    }

    [Fact]
    public void ExerciseCatalogRequiresReviewedMuscleGroups()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { PrimaryMuscleGroups = [] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("al menos un grupo muscular principal", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedPlanSnapshotsPrimaryAndSecondaryMuscleGroups()
    {
        var result = TrainingProgramGenerator.Generate(Request(), Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var push = result.Prescription!.Sessions.SelectMany(session => session.Exercises)
            .First(exercise => exercise.ExerciseId == "exercise-horizontalpush");
        Assert.Contains(TrainingMuscleGroup.Chest, push.PrimaryMuscleGroups);
        Assert.Contains(TrainingMuscleGroup.Triceps, push.SecondaryMuscleGroups);
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

    [Fact]
    public void WeeklyPeriodizationBuildsVolumeAndSchedulesDeloadEveryFourthWeek()
    {
        var request = Request() with { DurationWeeks = 8 };
        var result = TrainingProgramGenerator.Generate(request, Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var sessions = result.Prescription!.Sessions;
        var firstExerciseByWeek = sessions
            .GroupBy(session => session.WeekNumber)
            .ToDictionary(group => group.Key, group => group.First().Exercises[0]);

        Assert.Equal(2, firstExerciseByWeek[1].Sets); // baseline
        Assert.Equal(3, firstExerciseByWeek[2].Sets); // build
        Assert.Equal(3, firstExerciseByWeek[3].Sets); // build
        Assert.Equal(1, firstExerciseByWeek[4].Sets); // deload
        Assert.Equal(2, firstExerciseByWeek[5].Sets); // next cycle baseline
        Assert.Equal(3, firstExerciseByWeek[6].Sets);
        Assert.Equal(1, firstExerciseByWeek[8].Sets);
        Assert.Equal(4m, firstExerciseByWeek[4].TargetRepetitionsInReserve!.Value);
        Assert.Equal(3m, firstExerciseByWeek[5].TargetRepetitionsInReserve!.Value);
        Assert.Empty(TrainingPrescriptionValidator.Validate(result.Prescription));
    }

    [Fact]
    public void QualityReportCalculatesPrimaryVolumeAndFrequencyByWeek()
    {
        var result = TrainingProgramGenerator.Generate(Request() with { DurationWeeks = 4 }, Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var report = result.Prescription!.QualityReport;
        Assert.NotNull(report);
        Assert.Equal(4, report.WeeklyMuscleVolumes.Count);

        var weekOneQuads = report.WeeklyMuscleVolumes.Single(week => week.WeekNumber == 1)
            .Muscles.Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Quadriceps);
        var weekTwoQuads = report.WeeklyMuscleVolumes.Single(week => week.WeekNumber == 2)
            .Muscles.Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Quadriceps);
        var weekFourQuads = report.WeeklyMuscleVolumes.Single(week => week.WeekNumber == 4)
            .Muscles.Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Quadriceps);

        Assert.Equal(4, weekOneQuads.PrimarySets);
        Assert.Equal(2, weekOneQuads.PrimaryFrequencySessions);
        Assert.Equal(6, weekTwoQuads.PrimarySets);
        Assert.Equal(2, weekFourQuads.PrimarySets);
        // The expanded anatomical taxonomy intentionally reports muscles not represented by
        // this compact synthetic fixture. Volume calculations themselves must remain warning-free.
        Assert.DoesNotContain(report.Warnings, warning => warning.Contains("series primarias", StringComparison.Ordinal));
        Assert.Contains(report.Warnings, warning => warning.Contains("Adductors", StringComparison.Ordinal));
        Assert.Contains(report.Warnings, warning => warning.Contains("RotatorCuff", StringComparison.Ordinal));
    }

    [Fact]
    public void QualityReportFlagsMusclesWithoutAnyCatalogCoverage()
    {
        var result = TrainingProgramGenerator.Generate(Request(), Catalog());
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        var planWithoutCalfCoverage = result.Prescription! with
        {
            Sessions = result.Prescription.Sessions.Select(session => session with
            {
                Exercises = session.Exercises.Select(exercise => exercise with
                {
                    PrimaryMuscleGroups = exercise.PrimaryMuscleGroups.Where(muscle => muscle != TrainingMuscleGroup.Calves).ToArray(),
                    SecondaryMuscleGroups = exercise.SecondaryMuscleGroups.Where(muscle => muscle != TrainingMuscleGroup.Calves).ToArray()
                }).ToArray()
            }).ToArray(),
            QualityReport = null
        };

        var report = TrainingProgramQualityAnalyzer.Analyze(planWithoutCalfCoverage);
        Assert.Contains(report.Warnings, warning => warning.Contains("Calves", StringComparison.Ordinal));
    }

    [Fact]
    public void QualityReportFlagsVeryHighPrimarySetVolumeWithoutRejectingThePlan()
    {
        var result = TrainingProgramGenerator.Generate(Request(), Catalog());
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        var exaggeratedPlan = result.Prescription! with
        {
            Sessions = result.Prescription.Sessions.Select(session => session with
            {
                Exercises = session.Exercises.Select(exercise => exercise with
                {
                    Sets = 6,
                    PrimaryMuscleGroups = [TrainingMuscleGroup.Quadriceps],
                    SecondaryMuscleGroups = []
                }).ToArray()
            }).ToArray(),
            QualityReport = null
        };

        var report = TrainingProgramQualityAnalyzer.Analyze(exaggeratedPlan);
        Assert.Contains(report.Warnings, warning => warning.Contains("series primarias", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratorMaintainsCoreInvariantsAcrossGoalExperienceDurationAndAvailabilityMatrix()
    {
        var dayOptions = new[]
        {
            new[] { DayOfWeek.Monday, DayOfWeek.Thursday },
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }
        };
        var durations = new[] { 1, 2, 3, 4, 5, 8 };

        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var days in dayOptions)
        foreach (var weeks in durations)
        {
            var request = Request() with
            {
                Goal = goal,
                Experience = experience,
                DurationWeeks = weeks,
                TrainingDays = days,
                MaximumSessionDurationMinutes = 60
            };

            var result = TrainingProgramGenerator.Generate(request, Catalog());

            Assert.True(result.Succeeded,
                $"Goal={goal}, experiencia={experience}, días={days.Length}, semanas={weeks}: {string.Join("; ", result.Errors)}");
            var plan = result.Prescription!;
            // High-volume diagnostics are advisory and must not invalidate a structurally valid plan.
            Assert.NotNull(plan.QualityReport);
            Assert.Equal(weeks * days.Length, plan.Sessions.Count);
            Assert.Equal(weeks, plan.QualityReport!.WeeklyMuscleVolumes.Count);
            Assert.Empty(TrainingPrescriptionValidator.Validate(plan));
            Assert.Equal(
                plan.Sessions.Count,
                plan.Sessions.Select(session => (session.WeekNumber, session.Day)).Distinct().Count());
            Assert.All(plan.Sessions, session =>
            {
                Assert.InRange(session.WeekNumber, 1, weeks);
                Assert.Contains(session.Day, days);
                Assert.InRange(session.EstimatedDurationMinutes, 1, request.MaximumSessionDurationMinutes);
                Assert.NotEmpty(session.Exercises);
                Assert.All(session.Exercises, exercise =>
                {
                    Assert.Contains(exercise.ExerciseId, Catalog().Select(candidate => candidate.ExerciseId));
                    Assert.NotEmpty(exercise.PrimaryMuscleGroups);
                    Assert.InRange(exercise.Sets, 1, 4);
                    Assert.InRange(exercise.MinRepetitions, 1, exercise.MaxRepetitions);
                    Assert.NotNull(exercise.TargetRepetitionsInReserve);
                    Assert.InRange(exercise.TargetRepetitionsInReserve!.Value, 0m, 5m);
                    Assert.True(exercise.RestSeconds > 0);
                });
            });
        }
    }

    [Fact]
    public void ShortPlanDoesNotDeloadBeforeCompletingFourWeekCycle()
    {
        var result = TrainingProgramGenerator.Generate(Request() with { DurationWeeks = 3 }, Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.All(result.Prescription!.Sessions.Where(session => session.WeekNumber == 3),
            session => Assert.All(session.Exercises, exercise => Assert.Equal(3, exercise.Sets)));
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
        return Enum.GetValues<TrainingMovementPattern>().Select(pattern =>
        {
            var (primary, secondary) = MuscleMetadata(pattern);
            return new TrainingExerciseCandidate(
                pattern == TrainingMovementPattern.Squat ? "squat-basic" : $"exercise-{pattern.ToString().ToLowerInvariant()}",
                "catalog-2026-10",
                $"Ejercicio {pattern}",
                pattern,
                [],
                [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
                TrainingExperience.Beginner,
                50,
                true,
                primary,
                secondary);
        }).ToArray();
    }

    private static (IReadOnlyList<TrainingMuscleGroup> Primary, IReadOnlyList<TrainingMuscleGroup> Secondary)
        MuscleMetadata(TrainingMovementPattern pattern) => pattern switch
        {
            TrainingMovementPattern.Squat => ([TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.ErectorSpinae]),
            TrainingMovementPattern.Hinge => ([TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Quadriceps]),
            TrainingMovementPattern.HorizontalPush => ([TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Deltoids]),
            TrainingMovementPattern.VerticalPush => ([TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Chest]),
            TrainingMovementPattern.HorizontalPull => ([TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Forearms]),
            TrainingMovementPattern.VerticalPull => ([TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.UpperBack]),
            TrainingMovementPattern.SingleLeg => ([TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.Calves]),
            TrainingMovementPattern.Core => ([TrainingMuscleGroup.Abdominals], [TrainingMuscleGroup.Obliques, TrainingMuscleGroup.ErectorSpinae]),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern))
        };
}
