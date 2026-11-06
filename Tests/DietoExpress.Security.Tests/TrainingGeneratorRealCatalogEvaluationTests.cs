using System.Net.Http;
using System.Text.Json;
using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Runs the production RepDB import and auto-curation pipeline in an isolated PostgreSQL schema,
/// then evaluates the generator against the actual approved candidate set rather than a small fixture.
/// </summary>
public sealed class TrainingGeneratorRealCatalogEvaluationTests
{
    private readonly ITestOutputHelper _output;

    public TrainingGeneratorRealCatalogEvaluationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AutoCuratedRepDbCatalogGeneratesAcrossThe81ScenarioMatrix()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable("DIETOEXPRESS_IDEMPOTENCY_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            _output.WriteLine("Real RepDB catalog audit skipped: PostgreSQL integration connection is not configured.");
            return;
        }

        const string schema = "training_generator_real_catalog_audit";
        await using (var admin = new NpgsqlConnection(baseConnectionString))
        {
            await admin.OpenAsync();
            await using var createSchema = new NpgsqlCommand(
                $"DROP SCHEMA IF EXISTS {schema} CASCADE; CREATE SCHEMA {schema};", admin);
            await createSchema.ExecuteNonQueryAsync();
        }

        var scenarioResults = new List<RealCatalogScenarioResult>();
        ExerciseCatalogService.ExerciseCatalogReviewStats? reviewStats = null;
        var candidateCount = 0;
        var generalFitnessCandidateCount = 0;
        try
        {
            var scopedConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                SearchPath = schema
            }.ConnectionString;

            await using var connection = new NpgsqlConnection(scopedConnectionString);
            await connection.OpenAsync();
            await using (var createMigrations = new NpgsqlCommand("""
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    id VARCHAR(200) PRIMARY KEY,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                """, connection))
            {
                await createMigrations.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<angulosodbContext>()
                .UseNpgsql(scopedConnectionString)
                .Options;
            await using var context = new angulosodbContext(options);
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var catalogService = new ExerciseCatalogService(
                context,
                new FixedHttpClientFactory(httpClient),
                NullLogger<ExerciseCatalogService>.Instance);

            await catalogService.EnsureSeededAsync();
            reviewStats = await catalogService.GetReviewStatsAsync();
            var candidates = await catalogService.LoadGeneratorCandidatesAsync(ownerUserId: 0);
            candidateCount = candidates.Count;
            generalFitnessCandidateCount = candidates.Count(candidate =>
                candidate.SuitableGoals.Contains(TrainingGoal.GeneralFitness));
            Assert.True(generalFitnessCandidateCount >= 100,
                $"Only {generalFitnessCandidateCount} approved RepDB candidates are suitable for general fitness.");
            Assert.True(candidateCount >= 100,
                $"The RepDB curation pipeline returned only {candidateCount} approved generator candidates.");

            foreach (var goal in Enum.GetValues<TrainingGoal>())
            foreach (var experience in Enum.GetValues<TrainingExperience>())
            foreach (var days in DaySets())
            foreach (var durationMinutes in new[] { 35, 60, 90 })
            {
                var request = new TrainingProgramGenerationRequest(
                    goal,
                    experience,
                    new DateOnly(2026, 10, 26),
                    4,
                    days,
                    durationMinutes,
                    [],
                    [],
                    null,
                    false);
                var generated = TrainingProgramGenerator.Generate(request, candidates);
                var sessions = generated.Prescription?.Sessions ?? [];
                var maximumEstimatedMinutes = sessions.Count == 0
                    ? (int?)null
                    : sessions.Max(session => session.EstimatedDurationMinutes);
                var warnings = generated.Prescription?.QualityReport?.Warnings ?? [];
                var selectedExerciseIds = sessions.SelectMany(session => session.Exercises)
                    .Select(exercise => exercise.ExerciseId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();

                scenarioResults.Add(new RealCatalogScenarioResult(
                    $"goal-{goal.ToString().ToLowerInvariant()}-experience-{experience.ToString().ToLowerInvariant()}-days-{days.Count}-minutes-{durationMinutes}",
                    goal.ToString(),
                    experience.ToString(),
                    days.Count,
                    durationMinutes,
                    generated.Succeeded,
                    generated.Errors.ToArray(),
                    maximumEstimatedMinutes,
                    warnings.ToArray(),
                    selectedExerciseIds));
            }
        }
        finally
        {
            await using var cleanup = new NpgsqlConnection(baseConnectionString);
            await cleanup.OpenAsync();
            await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE;", cleanup);
            await dropSchema.ExecuteNonQueryAsync();
        }

        var report = new RealCatalogEvaluationReport(
            "training-generator-real-catalog-evaluation-v1",
            DateTimeOffset.UtcNow,
            ExerciseCatalogService.SourceUrl,
            reviewStats?.repDbTotal ?? 0,
            reviewStats?.repDbApproved ?? 0,
            candidateCount,
            generalFitnessCandidateCount,
            scenarioResults.Count,
            scenarioResults.Count(result => result.Succeeded),
            scenarioResults.Sum(result => result.Errors.Count),
            scenarioResults.Sum(result => result.Warnings.Count),
            scenarioResults);

        var configuredPath = Environment.GetEnvironmentVariable("TRAINING_GENERATOR_REAL_CATALOG_REPORT_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }

        _output.WriteLine(JsonSerializer.Serialize(new
        {
            report.CatalogTotal,
            report.CatalogApproved,
            report.GeneratorCandidateCount,
            report.GeneralFitnessCandidateCount,
            report.ScenarioCount,
            report.SuccessfulScenarios,
            report.GenerationErrorCount,
            report.QualityWarningCount,
            MaximumEstimatedMinutes = scenarioResults.Where(result => result.MaximumEstimatedMinutes.HasValue)
                .Select(result => result.MaximumEstimatedMinutes!.Value)
                .DefaultIfEmpty(0)
                .Max()
        }));

        Assert.Equal(81, report.ScenarioCount);
        Assert.Equal(report.ScenarioCount, report.SuccessfulScenarios);
        Assert.Equal(0, report.GenerationErrorCount);
        Assert.All(scenarioResults, result =>
        {
            Assert.NotNull(result.MaximumEstimatedMinutes);
            Assert.True(result.MaximumEstimatedMinutes <= result.RequestedDurationMinutes,
                $"{result.ScenarioId} estimated {result.MaximumEstimatedMinutes} minutes for a {result.RequestedDurationMinutes}-minute limit.");
        });
    }

    private static IReadOnlyList<DayOfWeek>[] DaySets() =>
    [
        [DayOfWeek.Monday, DayOfWeek.Thursday],
        [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday]
    ];

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed record RealCatalogScenarioResult(
        string ScenarioId,
        string Goal,
        string Experience,
        int TrainingDaysPerWeek,
        int RequestedDurationMinutes,
        bool Succeeded,
        IReadOnlyList<string> Errors,
        int? MaximumEstimatedMinutes,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> SelectedExerciseIds);

    private sealed record RealCatalogEvaluationReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string SourceUrl,
        long CatalogTotal,
        long CatalogApproved,
        int GeneratorCandidateCount,
        int GeneralFitnessCandidateCount,
        int ScenarioCount,
        int SuccessfulScenarios,
        int GenerationErrorCount,
        int QualityWarningCount,
        IReadOnlyList<RealCatalogScenarioResult> Scenarios);
}
