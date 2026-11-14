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
    public async Task AutoCuratedRepDbCatalogGeneratesAcrossThe405ScenarioMatrix()
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

            // Knee-flexion hamstring curls must be available as isolation accessories, not
            // misclassified as hip hinges where they lose to deadlift/RDL pattern selection.
            foreach (var exerciseId in new[] { "leg-curl", "seated-leg-curl", "nordic-hamstring-curl", "ball-leg-curl", "banded-standing-curl", "trx-hamstring-curl" })
            {
                var candidate = candidates.Single(item => item.ExerciseId == exerciseId);
                Assert.Equal(TrainingMovementPattern.Accessory, candidate.MovementPattern);
            }
            var reverseNordic = candidates.Single(candidate => candidate.ExerciseId == "reverse-nordic-curl");
            Assert.Equal(TrainingMovementPattern.Accessory, reverseNordic.MovementPattern);
            Assert.Contains(TrainingMuscleGroup.Quadriceps, reverseNordic.PrimaryMuscleGroups);
            Assert.Empty(reverseNordic.RequiredEquipment);
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

            foreach (var profile in ScenarioProfiles(candidates))
            foreach (var goal in Enum.GetValues<TrainingGoal>())
            foreach (var experience in Enum.GetValues<TrainingExperience>())
            foreach (var (days, split) in SupportedSplitDaySets())
            foreach (var durationMinutes in new[] { 35, 60, 90 })
            {
                var request = new TrainingProgramGenerationRequest(
                    goal,
                    experience,
                    new DateOnly(2026, 10, 26),
                    4,
                    days,
                    durationMinutes,
                    profile.AvailableEquipment,
                    profile.ExcludedExerciseIds,
                    null,
                    false,
                    split);
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
                    $"profile-{profile.Name.ToLowerInvariant()}{(split == TrainingProgramSplit.Automatic ? string.Empty : $"-split-{split.ToString().ToLowerInvariant()}")}-goal-{goal.ToString().ToLowerInvariant()}-experience-{experience.ToString().ToLowerInvariant()}-days-{days.Count}-minutes-{durationMinutes}",
                    profile.Name,
                    profile.AvailableEquipment,
                    profile.ExcludedExerciseIds,
                    goal.ToString(),
                    experience.ToString(),
                    split.ToString(),
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
            "training-generator-real-catalog-evaluation-v8",
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
            candidates.OrderBy(candidate => candidate.ExerciseId, StringComparer.Ordinal)
                .Select(candidate => new RealCatalogCandidateDiagnostic(
                    candidate.ExerciseId, candidate.MovementPattern.ToString(),
                    candidate.PrimaryMuscleGroups.Select(muscle => muscle.ToString()).ToArray(),
                    candidate.SecondaryMuscleGroups.Select(muscle => muscle.ToString()).ToArray(),
                    candidate.SuitableGoals.Select(goal => goal.ToString()).ToArray(),
                    candidate.MinimumExperience.ToString(), candidate.RequiredEquipment,
                    candidate.SelectionPriority))
                .ToArray(),
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

        Assert.Equal("training-generator-real-catalog-evaluation-v8", report.SchemaVersion);
        Assert.Equal(1485, report.ScenarioCount);
        Assert.Equal(11, scenarioResults.Select(result => (result.Split, result.TrainingDaysPerWeek)).Distinct().Count());
        Assert.Contains(scenarioResults, result => result.Split == TrainingProgramSplit.PushPullLegs.ToString() && result.TrainingDaysPerWeek == 5);
        Assert.Contains(scenarioResults, result => result.Split == TrainingProgramSplit.PushPullLegs.ToString() && result.TrainingDaysPerWeek == 6);
        Assert.All(scenarioResults, result =>
        {
            Assert.Equal(result.TrainingDaysPerWeek * 4, result.Sessions.Count);
            Assert.All(result.Sessions.GroupBy(session => session.WeekNumber), week =>
                Assert.Equal(result.TrainingDaysPerWeek, week.Count()));
        });
        Assert.Equal(report.ScenarioCount, report.SuccessfulScenarios);
        Assert.Equal(0, report.GenerationErrorCount);
        Assert.Equal(report.CatalogTotal, report.CatalogApproved);
        Assert.Equal(0, report.CatalogPending);
        Assert.Equal(0, report.CatalogRejected);
        Assert.Equal(report.CatalogTotal, report.GeneratorCandidateCount);
        Assert.Empty(report.CatalogImageMissingIds);
        var beginnerHomeScenario = scenarioResults.Single(result =>
            result.ScenarioId == "profile-homegymwithbandsandball-goal-hypertrophy-experience-beginner-days-2-minutes-90");
        // The beginner home plan already reaches the reduced secondary hamstring target
        // through its loaded hip hinge; a direct band curl must not displace quads/calves.
        Assert.Contains("dumbbell-romanian-deadlift", beginnerHomeScenario.SelectedExerciseIds);
        var intermediateHomeScenario = scenarioResults.Single(result =>
            result.ScenarioId == "profile-homegymwithbandsandball-goal-hypertrophy-experience-intermediate-days-2-minutes-90");
        // Intermediate home plans retain a loaded hinge; direct knee-flexion accessories
        // remain optional when the higher-priority muscle groups still need slots.
        Assert.Contains("deadlift", intermediateHomeScenario.SelectedExerciseIds);
        var noMachineQuadScenario = scenarioResults.Single(result =>
            result.ScenarioId == "profile-nocableormachines-goal-hypertrophy-experience-intermediate-days-2-minutes-90");
        Assert.Contains("reverse-nordic-curl", noMachineQuadScenario.SelectedExerciseIds);
        var dumbbellOnlyChestScenario = scenarioResults.Single(result =>
            result.ScenarioId == "profile-dumbbellsbenchpullup-goal-hypertrophy-experience-intermediate-days-2-minutes-90");
        Assert.Contains("db-fly", dumbbellOnlyChestScenario.SelectedExerciseIds);
        Assert.Contains(TrainingMuscleGroup.Chest, candidates.Single(candidate => candidate.ExerciseId == "db-fly").PrimaryMuscleGroups);
        Assert.DoesNotContain("db-fly", catalogSingleImageOnlyIds);

        Assert.All(scenarioResults, result =>
        {
            Assert.NotNull(result.MaximumEstimatedMinutes);
            Assert.True(result.MaximumEstimatedMinutes <= result.RequestedDurationMinutes,
                $"{result.ScenarioId} estimated {result.MaximumEstimatedMinutes} minutes for a {result.RequestedDurationMinutes}-minute limit.");
        });
    }

    private static IReadOnlyList<RealCatalogScenarioProfile> ScenarioProfiles(
        IReadOnlyList<TrainingExerciseCandidate> candidates)
    {
        var excludedFallbacks = candidates
            .Where(candidate => candidate.MovementPattern != TrainingMovementPattern.Accessory)
            .GroupBy(candidate => candidate.MovementPattern)
            .Where(group => group.Count() > 1)
            .Select(group => group
                .OrderByDescending(candidate => candidate.SelectionPriority)
                .ThenBy(candidate => candidate.ExerciseId, StringComparer.Ordinal)
                .First().ExerciseId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return
        [
            new("FullGym", ["barbell", "dumbbell", "bench", "pull_up_bar", "cable", "kettlebell", "ez_bar", "shoulder_press_machine", "ab_wheel", "machine"], []),
            new("NoCableOrMachines", ["barbell", "dumbbell", "bench", "pull_up_bar", "kettlebell", "ez_bar", "bodyweight"], []),
            new("HomeGymWithBandsAndBall", ["barbell", "dumbbell", "bench", "pull_up_bar", "kettlebell", "ez_bar", "bodyweight", "resistance-band", "stability-ball", "suspension-trainer"], []),
            new("DumbbellsBenchPullup", ["dumbbell", "bench", "pull_up_bar", "bodyweight"], []),
            new("RestrictedWithExclusions", ["barbell", "dumbbell", "bench", "pull_up_bar", "kettlebell", "bodyweight"], excludedFallbacks)
        ];
    }

    private sealed record RealCatalogScenarioProfile(
        string Name,
        IReadOnlyList<string> AvailableEquipment,
        IReadOnlyList<string> ExcludedExerciseIds);

    private static bool HasImage(JsonElement images, string key) =>
        images.ValueKind == JsonValueKind.Object &&
        images.TryGetProperty(key, out var image) &&
        image.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(image.GetString());

    // Exercise every supported split/day-count combination with the same five equipment profiles,
    // three goals, three experience levels and three session-duration budgets (1,485 scenarios total).
    private static IReadOnlyList<(IReadOnlyList<DayOfWeek> Days, TrainingProgramSplit Split)> SupportedSplitDaySets() =>
    [
        (new[] { DayOfWeek.Monday, DayOfWeek.Thursday }, TrainingProgramSplit.Automatic),
        (new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, TrainingProgramSplit.Automatic),
        (new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }, TrainingProgramSplit.Automatic),
        (new[] { DayOfWeek.Monday, DayOfWeek.Thursday }, TrainingProgramSplit.FullBody),
        (new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, TrainingProgramSplit.FullBody),
        (new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }, TrainingProgramSplit.FullBody),
        (new[] { DayOfWeek.Monday, DayOfWeek.Thursday }, TrainingProgramSplit.UpperLower),
        (new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }, TrainingProgramSplit.UpperLower),
        (new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, TrainingProgramSplit.PushPullLegs),
        (new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday }, TrainingProgramSplit.PushPullLegs),
        (new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday }, TrainingProgramSplit.PushPullLegs)
    ];

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed record RealCatalogScenarioResult(
        string ScenarioId,
        string EquipmentProfile,
        IReadOnlyList<string> AvailableEquipment,
        IReadOnlyList<string> ExcludedExerciseIds,
        string Goal,
        string Experience,
        string Split,
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

    private sealed record RealCatalogCandidateDiagnostic(
        string ExerciseId,
        string MovementPattern,
        IReadOnlyList<string> PrimaryMuscles,
        IReadOnlyList<string> SecondaryMuscles,
        IReadOnlyList<string> SuitableGoals,
        string MinimumExperience,
        IReadOnlyList<string> RequiredEquipment,
        int SelectionPriority);

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
        IReadOnlyList<RealCatalogCandidateDiagnostic> CandidateDiagnostics,
        int GeneratorCandidateCount,
        int GeneralFitnessCandidateCount,
        int ScenarioCount,
        int SuccessfulScenarios,
        int GenerationErrorCount,
        int QualityWarningCount,
        IReadOnlyList<RealCatalogScenarioResult> Scenarios);
}
