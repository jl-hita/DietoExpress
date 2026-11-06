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
        var catalogReviewExceptions = new List<RealCatalogReviewException>();
        var catalogImageMissingIds = new List<string>();
        var catalogSingleImageOnlyIds = new List<string>();
        ExerciseCatalogService.ExerciseCatalogReviewStats? reviewStats = null;
        IReadOnlyList<TrainingExerciseCandidate> candidates = Array.Empty<TrainingExerciseCandidate>();
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

            // Simulate a database that already contains v2 automatic approvals without the
            // GeneralFitness goal, then verify the additive migration updates those rows safely.
            await using (var removeGoal = new NpgsqlCommand("""
                UPDATE exercise_catalog
                SET reviewed_suitable_goals = COALESCE(reviewed_suitable_goals, '[]'::jsonb) - 'GeneralFitness'
                WHERE source = 'repdb-free' AND owner_user_id IS NULL
                  AND review_status = 'approved' AND reviewed = TRUE
                  AND reviewed_by LIKE 'RepDB automatic metadata%'
                """, connection))
            {
                var changed = await removeGoal.ExecuteNonQueryAsync();
                Assert.True(changed >= 50, $"Expected at least 50 automatic RepDB approvals to migrate from the curated catalog, found {changed}.");
            }
            await using (var removeMarker = new NpgsqlCommand(
                "DELETE FROM schema_migrations WHERE id = 'exercise-catalog-repdb-general-fitness-goal-v1'", connection))
            {
                await removeMarker.ExecuteNonQueryAsync();
            }

            await catalogService.EnsureSeededAsync();
            reviewStats = await catalogService.GetReviewStatsAsync();
            candidates = await catalogService.LoadGeneratorCandidatesAsync(ownerUserId: 0);
            candidateCount = candidates.Count;
            await using (var reviewAudit = new NpgsqlCommand("""
                SELECT exercise_id, review_status,
                       COALESCE(review_notes, ''),
                       COALESCE(metadata_enrichment_notes, ''),
                       COALESCE(name_es, ''),
                       COALESCE(difficulty, ''),
                       COALESCE(equipment, '')
                FROM exercise_catalog
                WHERE source = 'repdb-free' AND review_status <> 'approved'
                ORDER BY exercise_id
                """, connection))
            await using (var reader = await reviewAudit.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    catalogReviewExceptions.Add(new RealCatalogReviewException(
                        reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
                }
            }
            await using (var imageAudit = new NpgsqlCommand("""
                SELECT exercise_id, COALESCE(images, '{}'::jsonb)::text
                FROM exercise_catalog
                WHERE source = 'repdb-free'
                ORDER BY exercise_id
                """, connection))
            await using (var reader = await imageAudit.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var exerciseId = reader.GetString(0);
                    using var images = JsonDocument.Parse(reader.GetString(1));
                    var root = images.RootElement;
                    var flat = root.ValueKind == JsonValueKind.Object &&
                               root.TryGetProperty("flat", out var flatImages)
                        ? flatImages
                        : root;
                    var hasStart = HasImage(flat, "start");
                    var hasPeak = HasImage(flat, "peak");
                    var hasMain = HasImage(flat, "main");
                    if (!hasStart && !hasPeak && !hasMain)
                        catalogImageMissingIds.Add(exerciseId);
                    else if (!hasStart || !hasPeak)
                        catalogSingleImageOnlyIds.Add(exerciseId);
                }
            }
            generalFitnessCandidateCount = candidates.Count(candidate =>
                candidate.SuitableGoals.Contains(TrainingGoal.GeneralFitness));
            Assert.True(generalFitnessCandidateCount >= 50,
                $"Only {generalFitnessCandidateCount} approved curated RepDB candidates are suitable for general fitness.");
            Assert.True(candidateCount >= 50,
                $"The curated RepDB catalog returned only {candidateCount} approved generator candidates.");

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

                var primaryMuscleSetsPerWeek = sessions
                    .SelectMany(session => session.Exercises.SelectMany(exercise => exercise.PrimaryMuscleGroups
                        .Select(muscle => new { Muscle = muscle.ToString(), session.WeekNumber, exercise.Sets })))
                    .GroupBy(item => item.Muscle)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyDictionary<int, int>)group.GroupBy(item => item.WeekNumber)
                            .ToDictionary(week => week.Key, week => week.Sum(item => item.Sets)));
                var secondaryMuscleSetExposurePerWeek = sessions
                    .SelectMany(session => session.Exercises.SelectMany(exercise => exercise.SecondaryMuscleGroups
                        .Select(muscle => new { Muscle = muscle.ToString(), session.WeekNumber, exercise.Sets })))
                    .GroupBy(item => item.Muscle)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyDictionary<int, int>)group.GroupBy(item => item.WeekNumber)
                            .ToDictionary(week => week.Key, week => week.Sum(item => item.Sets)));
                var sessionDetails = sessions.Select(session => new RealCatalogSessionResult(
                    session.WeekNumber,
                    session.Day.ToString(),
                    session.Title,
                    session.EstimatedDurationMinutes,
                    session.Exercises.Select(exercise => new RealCatalogExerciseResult(
                        exercise.ExerciseId,
                        exercise.ExerciseNameSnapshot,
                        exercise.Sets,
                        exercise.MinRepetitions,
                        exercise.MaxRepetitions,
                        exercise.PrimaryMuscleGroups.Select(muscle => muscle.ToString()).ToArray(),
                        exercise.SecondaryMuscleGroups.Select(muscle => muscle.ToString()).ToArray())).ToArray()))
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
                    selectedExerciseIds,
                    primaryMuscleSetsPerWeek,
                    secondaryMuscleSetExposurePerWeek,
                    sessionDetails));
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
            "training-generator-real-catalog-evaluation-v4",
            DateTimeOffset.UtcNow,
            ExerciseCatalogService.SourceUrl,
            reviewStats?.repDbTotal ?? 0,
            reviewStats?.repDbApproved ?? 0,
            reviewStats?.repDbPending ?? 0,
            reviewStats?.repDbRejected ?? 0,
            catalogReviewExceptions,
            catalogImageMissingIds,
            catalogSingleImageOnlyIds,
            candidates.Select(candidate => candidate.ExerciseId).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
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
            report.CatalogPending,
            report.CatalogRejected,
            report.CatalogReviewExceptions,
            report.CatalogImageMissingIds,
            report.CatalogSingleImageOnlyIds,
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

        Assert.Equal("training-generator-real-catalog-evaluation-v4", report.SchemaVersion);
        Assert.Equal(81, report.ScenarioCount);
        Assert.Equal(report.ScenarioCount, report.SuccessfulScenarios);
        Assert.Equal(0, report.GenerationErrorCount);
        Assert.Equal(report.CatalogTotal, report.CatalogApproved);
        Assert.Equal(0, report.CatalogPending);
        Assert.Equal(0, report.CatalogRejected);
        Assert.Equal(report.CatalogTotal, report.GeneratorCandidateCount);
        Assert.Empty(report.CatalogImageMissingIds);
        Assert.All(scenarioResults, result =>
        {
            Assert.NotNull(result.MaximumEstimatedMinutes);
            Assert.True(result.MaximumEstimatedMinutes <= result.RequestedDurationMinutes,
                $"{result.ScenarioId} estimated {result.MaximumEstimatedMinutes} minutes for a {result.RequestedDurationMinutes}-minute limit.");
        });
    }

    private static bool HasImage(JsonElement images, string key) =>
        images.ValueKind == JsonValueKind.Object &&
        images.TryGetProperty(key, out var image) &&
        image.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(image.GetString());

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
        IReadOnlyList<string> SelectedExerciseIds,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> PrimaryMuscleSetsPerWeek,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> SecondaryMuscleSetExposurePerWeek,
        IReadOnlyList<RealCatalogSessionResult> Sessions);

    private sealed record RealCatalogSessionResult(
        int WeekNumber,
        string Day,
        string Title,
        int EstimatedDurationMinutes,
        IReadOnlyList<RealCatalogExerciseResult> Exercises);

    private sealed record RealCatalogExerciseResult(
        string ExerciseId,
        string ExerciseName,
        int Sets,
        int MinRepetitions,
        int MaxRepetitions,
        IReadOnlyList<string> PrimaryMuscles,
        IReadOnlyList<string> SecondaryMuscles);

    private sealed record RealCatalogReviewException(
        string ExerciseId,
        string ReviewStatus,
        string ReviewNotes,
        string MetadataEnrichmentNotes,
        string NameEs,
        string Difficulty,
        string Equipment);

    private sealed record RealCatalogEvaluationReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string SourceUrl,
        long CatalogTotal,
        long CatalogApproved,
        long CatalogPending,
        long CatalogRejected,
        IReadOnlyList<RealCatalogReviewException> CatalogReviewExceptions,
        IReadOnlyList<string> CatalogImageMissingIds,
        IReadOnlyList<string> CatalogSingleImageOnlyIds,
        IReadOnlyList<string> CandidateIds,
        int GeneratorCandidateCount,
        int GeneralFitnessCandidateCount,
        int ScenarioCount,
        int SuccessfulScenarios,
        int GenerationErrorCount,
        int QualityWarningCount,
        IReadOnlyList<RealCatalogScenarioResult> Scenarios);
}
