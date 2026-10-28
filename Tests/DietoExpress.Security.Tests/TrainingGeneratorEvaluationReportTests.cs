using System.Text.Json;
using System.Text.Json.Serialization;
using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Emits a reproducible, complete JSON corpus for human-assisted review. The report is evidence,
/// not a claim that structurally valid plans are physiologically optimal.
/// </summary>
public sealed class TrainingGeneratorEvaluationReportTests
{
    [Fact]
    public void GenerateScenarioMatrixAndWriteFullJsonReport()
    {
        var scenarios = new List<ScenarioReport>();
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var days in DaySets())
        {
            var request = new TrainingProgramGenerationRequest(
                goal,
                experience,
                new DateOnly(2026, 10, 26),
                4,
                days,
                60,
                ["barbell", "dumbbell", "pull_up_bar", "cable", "kettlebell", "ez_bar", "shoulder_press_machine", "ab_wheel"],
                []);

            var generated = TrainingProgramGenerator.Generate(request, Catalog());
            var hardViolations = new List<string>(generated.Errors);
            var metrics = new PlanMetrics(0, 0, 0, 0, 0, new Dictionary<int, int>(), new Dictionary<string, int>(),
                new Dictionary<string, IReadOnlyDictionary<int, int>>(), new Dictionary<string, IReadOnlyDictionary<int, int>>());

            if (generated.Prescription is { } plan)
            {
                hardViolations.AddRange(TrainingPrescriptionValidator.Validate(plan));
                var expectedSessions = request.DurationWeeks * request.TrainingDays.Count;
                if (plan.Sessions.Count != expectedSessions)
                    hardViolations.Add($"Se esperaban {expectedSessions} sesiones y se generaron {plan.Sessions.Count}.");
                if (plan.Sessions.Any(s => !request.TrainingDays.Contains(s.Day)))
                    hardViolations.Add("Se ha generado una sesión en un día no solicitado.");
                if (plan.Sessions.Any(s => s.EstimatedDurationMinutes > request.MaximumSessionDurationMinutes))
                    hardViolations.Add("Una sesión supera la duración máxima solicitada.");

                var allExercises = plan.Sessions.SelectMany(s => s.Exercises).ToArray();
                var duplicateIdsWithinSession = plan.Sessions.Sum(session =>
                    session.Exercises.Select(x => x.ExerciseId).Count() -
                    session.Exercises.Select(x => x.ExerciseId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                var primaryMuscleSetsPerWeek = plan.Sessions
                    .SelectMany(session => session.Exercises.SelectMany(exercise => exercise.PrimaryMuscleGroups
                        .Select(muscle => new { Muscle = muscle.ToString(), session.WeekNumber, exercise.Sets })))
                    .GroupBy(x => x.Muscle)
                    .ToDictionary(
                        muscle => muscle.Key,
                        muscle => (IReadOnlyDictionary<int, int>)muscle.GroupBy(x => x.WeekNumber)
                            .ToDictionary(week => week.Key, week => week.Sum(x => x.Sets)));

                var secondaryMuscleSetExposurePerWeek = plan.Sessions
                    .SelectMany(session => session.Exercises.SelectMany(exercise => exercise.SecondaryMuscleGroups
                        .Select(muscle => new { Muscle = muscle.ToString(), session.WeekNumber, exercise.Sets })))
                    .GroupBy(x => x.Muscle)
                    .ToDictionary(
                        muscle => muscle.Key,
                        muscle => (IReadOnlyDictionary<int, int>)muscle.GroupBy(x => x.WeekNumber)
                            .ToDictionary(week => week.Key, week => week.Sum(x => x.Sets)));

                metrics = new PlanMetrics(
                    plan.Sessions.Count,
                    allExercises.Length,
                    allExercises.Sum(x => x.Sets),
                    plan.Sessions.Count == 0 ? 0 : plan.Sessions.Max(s => s.EstimatedDurationMinutes),
                    duplicateIdsWithinSession,
                    plan.Sessions.GroupBy(s => s.WeekNumber).ToDictionary(g => g.Key, g => g.Count()),
                    allExercises.GroupBy(x => x.ExerciseId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
                    primaryMuscleSetsPerWeek,
                    secondaryMuscleSetExposurePerWeek);
            }

            scenarios.Add(new ScenarioReport(
                $"goal-{goal.ToString().ToLowerInvariant()}-experience-{experience.ToString().ToLowerInvariant()}-days-{days.Count}",
                request,
                "repdb-free-curated-v1",
                generated.Succeeded,
                hardViolations.Distinct().ToArray(),
                new[]
                {
                    "El catálogo de evaluación contiene ejercicios reales de RepDB seleccionados mediante una lista curada y revisión explícita de patrón, material y grupos musculares; no representa el catálogo completo.",
                    "Las series principales se cuentan completas; las series secundarias son exposiciones y no equivalen a series efectivas ni usan coeficientes fisiológicos.",
                    "La biblioteca completa se conserva en PostgreSQL con su payload original; solo los ejercicios incluidos en la lista curada se consideran aptos para el generador.",
                    "La duración es la estimación heurística del motor; no está contrastada con sesiones reales.",
                    "La validación estructural no demuestra idoneidad fisiológica individual."
                },
                metrics,
                generated.Prescription));
        }

        var report = new EvaluationReport(
            "training-generator-evaluation-v3",
            DateTimeOffset.UtcNow,
            "0.5.0",
            scenarios.Count,
            scenarios.Count(s => s.GenerationSucceeded),
            scenarios.Sum(s => s.HardViolations.Count),
            "Las advertencias son limitaciones conocidas, no diagnósticos clínicos ni umbrales fisiológicos.",
            scenarios);

        var configuredPath = Environment.GetEnvironmentVariable("TRAINING_GENERATOR_REPORT_PATH");
        var outputPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "training-generator-evaluation.json")
            : configuredPath;
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, JsonOptions));

        Assert.Equal(27, report.ScenarioCount);
        Assert.Equal(report.ScenarioCount, report.SuccessfulScenarios);
        Assert.Equal(0, report.HardViolationCount);
        Assert.All(scenarios, scenario => Assert.All(
            scenario.Plan!.Sessions.SelectMany(session => session.Exercises),
            exercise =>
            {
                Assert.NotEmpty(exercise.PrimaryMuscleGroups);
                Assert.NotNull(exercise.SecondaryMuscleGroups);
            }));
        Assert.Contains(scenarios[0].Metrics.PrimaryMuscleSetsPerWeek,
            muscle => muscle.Key == nameof(TrainingMuscleGroup.Chest) && muscle.Value[1] > 0);
        Assert.Contains(scenarios[0].Metrics.SecondaryMuscleSetExposurePerWeek,
            muscle => muscle.Key == nameof(TrainingMuscleGroup.Triceps) && muscle.Value[1] > 0);
        Assert.Contains(scenarios, scenario =>
            scenario.Request.Goal == TrainingGoal.Hypertrophy &&
            scenario.Request.Experience == TrainingExperience.Advanced &&
            scenario.Request.TrainingDays.Count >= 3 &&
            scenario.Metrics.PrimaryMuscleSetsPerWeek.TryGetValue(nameof(TrainingMuscleGroup.Glutes), out var weeks) &&
            (weeks.TryGetValue(2, out var week2) && week2 > 20 ||
             weeks.TryGetValue(3, out var week3) && week3 > 20));
    }

    private static IReadOnlyList<DayOfWeek>[] DaySets() =>
    [
        [DayOfWeek.Monday, DayOfWeek.Thursday],
        [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday]
    ];

    private static TrainingExerciseCandidate[] Catalog()
    {
        var goals = Enum.GetValues<TrainingGoal>();
        return
        [
            Candidate("squat", "Sentadilla trasera con barra", TrainingMovementPattern.Squat, ["barbell"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Hamstrings]),
            Candidate("goblet-squat", "Sentadilla goblet", TrainingMovementPattern.Squat, ["kettlebell"], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.Hamstrings]),
            Candidate("ez-bar-romanian-deadlift", "Peso muerto rumano con barra EZ", TrainingMovementPattern.Hinge, ["ez_bar"], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings], [TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Forearms, TrainingMuscleGroup.UpperBack]),
            Candidate("romanian-deadlift", "Peso muerto rumano", TrainingMovementPattern.Hinge, ["barbell"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings], [TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Forearms]),
            Candidate("push-up", "Flexiones", TrainingMovementPattern.HorizontalPush, [], TrainingExperience.Beginner, 100,
                [TrainingMuscleGroup.Chest, TrainingMuscleGroup.Triceps], [TrainingMuscleGroup.Deltoids]),
            Candidate("bench-press", "Press de banca con barra", TrainingMovementPattern.HorizontalPush, ["barbell"], TrainingExperience.Intermediate, 95,
                [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.Triceps]),
            Candidate("lat-pulldown", "Jalón al pecho", TrainingMovementPattern.VerticalPull, ["cable"], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.UpperBack]),
            Candidate("pull-up", "Dominadas", TrainingMovementPattern.VerticalPull, ["pull_up_bar"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.UpperBack]),
            Candidate("seated-cable-row", "Remo sentado en polea", TrainingMovementPattern.HorizontalPull, ["cable"], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Deltoids]),
            Candidate("bent-over-db-row", "Remo inclinado con mancuernas", TrainingMovementPattern.HorizontalPull, ["dumbbell"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Deltoids]),
            Candidate("machine-shoulder-press", "Press de hombros en máquina", TrainingMovementPattern.VerticalPush, ["shoulder_press_machine"], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Triceps]),
            Candidate("ohp", "Press militar con barra", TrainingMovementPattern.VerticalPush, ["barbell"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Triceps]),
            Candidate("step-ups", "Subidas a cajón", TrainingMovementPattern.SingleLeg, [], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.Calves, TrainingMuscleGroup.Hamstrings]),
            Candidate("walking-lunge", "Zancadas caminando", TrainingMovementPattern.SingleLeg, [], TrainingExperience.Intermediate, 90,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.Calves]),
            Candidate("plank", "Plancha", TrainingMovementPattern.Core, [], TrainingExperience.Beginner, 90,
                [TrainingMuscleGroup.Abdominals], [TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Obliques]),
            Candidate("ab-wheel-rollout", "Rueda abdominal", TrainingMovementPattern.Core, ["ab_wheel"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Abdominals], [TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.Lats, TrainingMuscleGroup.Obliques])
        ];

        TrainingExerciseCandidate Candidate(
            string id, string name, TrainingMovementPattern pattern, string[] equipment,
            TrainingExperience minimumExperience, int priority,
            TrainingMuscleGroup[] primary, TrainingMuscleGroup[] secondary) =>
            new(id, "repdb-free-curated-v1", name, pattern, equipment, goals, minimumExperience, priority, true, primary, secondary);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record EvaluationReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string EngineVersion,
        int ScenarioCount,
        int SuccessfulScenarios,
        int HardViolationCount,
        string Interpretation,
        IReadOnlyList<ScenarioReport> Scenarios);

    private sealed record ScenarioReport(
        string ScenarioId,
        TrainingProgramGenerationRequest Request,
        string EvaluationCatalogVersion,
        bool GenerationSucceeded,
        IReadOnlyList<string> HardViolations,
        IReadOnlyList<string> Limitations,
        PlanMetrics Metrics,
        TrainingProgramPrescription? Plan);

    private sealed record PlanMetrics(
        int SessionCount,
        int ExercisePrescriptionCount,
        int TotalPrescribedSets,
        int MaximumEstimatedSessionMinutes,
        int DuplicateExerciseIdsWithinSessions,
        IReadOnlyDictionary<int, int> SessionsPerWeek,
        IReadOnlyDictionary<string, int> ExerciseSelectionCounts,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> PrimaryMuscleSetsPerWeek,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> SecondaryMuscleSetExposurePerWeek);
}
