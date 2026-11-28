using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Exercises the automatic proposal applier against an isolated PostgreSQL schema.
/// CI supplies the database through DIETOEXPRESS_IDEMPOTENCY_TEST_CONNECTION.
/// </summary>
public sealed class TrainingAutoAdaptationPostgresIntegrationTests
{
    private readonly string? _baseConnectionString =
        Environment.GetEnvironmentVariable("DIETOEXPRESS_IDEMPOTENCY_TEST_CONNECTION");

    [Fact]
    public async Task ConcurrentAttemptsForOneProposal_ChangeFutureSessionOnlyOnce()
    {
        if (string.IsNullOrWhiteSpace(_baseConnectionString))
            return; // CI supplies PostgreSQL; local unit-only runs remain usable.

        var schema = $"p1_adaptation_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(_baseConnectionString);
        await admin.OpenAsync();

        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin))
            await createSchema.ExecuteNonQueryAsync();

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(_baseConnectionString)
            {
                SearchPath = schema
            };
            var scopedConnectionString = builder.ConnectionString;
            await CreateSchemaAsync(scopedConnectionString);

            var options = new DbContextOptionsBuilder<angulosodbContext>()
                .UseNpgsql(scopedConnectionString)
                .Options;
            await using var firstDb = new angulosodbContext(options);
            await using var secondDb = new angulosodbContext(options);
            var firstApplier = new TrainingAutoAdaptationApplier(
                firstDb, NullLogger<TrainingAutoAdaptationApplier>.Instance);
            var secondApplier = new TrainingAutoAdaptationApplier(
                secondDb, NullLogger<TrainingAutoAdaptationApplier>.Instance);

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<int> StartApplyAsync(TrainingAutoAdaptationApplier applier) => Task.Run(async () =>
            {
                await start.Task;
                return await applier.TryApplyAsync(
                    100, 1, "add_muscle_set", ["bench"], CancellationToken.None);
            });

            var first = StartApplyAsync(firstApplier);
            var second = StartApplyAsync(secondApplier);
            start.SetResult();
            var results = await Task.WhenAll(first, second);

            Assert.Equal(1, results.Count(result => result == 1));
            Assert.Equal(1, results.Count(result => result == -1));

            await using var verify = new NpgsqlConnection(scopedConnectionString);
            await verify.OpenAsync();

            await using (var proposal = new NpgsqlCommand("""
                SELECT status, reviewed_by_user_id, review_notes, applied_future_sessions
                FROM training_adaptation_proposals WHERE id = 100
                """, verify))
            await using (var reader = await proposal.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal("approved", reader.GetString(0));
                Assert.True(reader.IsDBNull(1));
                Assert.StartsWith("auto:add_muscle_set|", reader.GetString(2));
                Assert.Equal(1, reader.GetInt32(3));
            }

            await using var setsCommand = new NpgsqlCommand("""
                SELECT (exercise_prescription_data->'exercises'->0->>'sets')::integer
                FROM training_sessions WHERE id = 10
                """, verify);
            Assert.Equal(5, (int)(await setsCommand.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", admin);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    private static async Task CreateSchemaAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var schema = new NpgsqlCommand("""
            CREATE TABLE training_plans (
                id INTEGER PRIMARY KEY,
                status TEXT NOT NULL
            );
            CREATE TABLE training_adaptation_proposals (
                id BIGINT PRIMARY KEY,
                plan_id INTEGER NOT NULL,
                status TEXT NOT NULL,
                action TEXT NOT NULL,
                reviewed_by_user_id INTEGER NULL,
                reviewed_at TIMESTAMPTZ NULL,
                review_notes TEXT NULL,
                applied_future_sessions INTEGER NULL
            );
            CREATE TABLE training_sessions (
                id INTEGER PRIMARY KEY,
                plan_id INTEGER NOT NULL,
                scheduled_date DATE NOT NULL,
                status TEXT NOT NULL,
                exercise_prescription_data JSONB NOT NULL,
                exercise_prescription TEXT NULL,
                instructions TEXT NULL,
                updated_at TIMESTAMPTZ NULL
            );
            CREATE TABLE training_session_records (
                session_id INTEGER PRIMARY KEY
            );
            INSERT INTO training_plans (id, status) VALUES (1, 'active');
            INSERT INTO training_adaptation_proposals (id, plan_id, status, action)
            VALUES (100, 1, 'pending', 'add_muscle_set');
            INSERT INTO training_sessions (
                id, plan_id, scheduled_date, status, exercise_prescription_data
            ) VALUES (
                10, 1, CURRENT_DATE + 1, 'planned',
                '{"exercises":[{"exerciseId":"bench","exerciseNameSnapshot":"Press banca","order":1,"sets":4,"minRepetitions":8,"maxRepetitions":12,"targetRepetitionsInReserve":2,"restSeconds":90,"progressionRule":1,"catalogVersion":"v1"}]}'::jsonb
            );
            """, connection);
        await schema.ExecuteNonQueryAsync();
    }
}
