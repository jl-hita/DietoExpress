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
                [],
                []);

            var generated = TrainingProgramGenerator.Generate(request, Catalog());
            var hardViolations = new List<string>(generated.Errors);
            var metrics = new PlanMetrics(0, 0, 0, 0, 0, new Dictionary<int, int>(), new Dictionary<string, int>());

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
                metrics = new PlanMetrics(
                    plan.Sessions.Count,
                    allExercises.Length,
                    allExercises.Sum(x => x.Sets),
                    plan.Sessions.Count == 0 ? 0 : plan.Sessions.Max(s => s.EstimatedDurationMinutes),
                    duplicateIdsWithinSession,
                    plan.Sessions.GroupBy(s => s.WeekNumber).ToDictionary(g => g.Key, g => g.Count()),
                    allExercises.GroupBy(x => x.ExerciseId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase));
            }

            scenarios.Add(new ScenarioReport(
                $"goal-{goal.ToString().ToLowerInvariant()}-experience-{experience.ToString().ToLowerInvariant()}-days-{days.Count}",
                request,
                "catalog-evaluation-v1",
                generated.Succeeded,
                hardViolations.Distinct().ToArray(),
                new[]
                {
                    "No se evalúa el volumen por músculo: el catálogo actual no declara músculos principales/secundarios.",
                    "La duración es la estimación heurística del motor; no está contrastada con sesiones reales.",
                    "La validación estructural no demuestra idoneidad fisiológica individual."
                },
                metrics,
                generated.Prescription));
        }

        var report = new EvaluationReport(
            "training-generator-evaluation-v1",
            DateTimeOffset.UtcNow,
            "0.3.0",
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
    }

    private static IReadOnlyList<DayOfWeek>[] DaySets() =>
    [
        [DayOfWeek.Monday, DayOfWeek.Thursday],
        [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday]
    ];

    private static TrainingExerciseCandidate[] Catalog() =>
        Enum.GetValues<TrainingMovementPattern>().Select(pattern => new TrainingExerciseCandidate(
            $"evaluation-{pattern.ToString().ToLowerInvariant()}",
            "catalog-evaluation-v1",
            $"Ejercicio de evaluación {pattern}",
            pattern,
            [],
            Enum.GetValues<TrainingGoal>(),
            TrainingExperience.Beginner,
            50,
            true)).ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
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
        IReadOnlyDictionary<string, int> ExerciseSelectionCounts);
}
