using System.Security.Claims;
using System.Text;
using Anguloso.Server.Middleware;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Exercises the actual middleware against PostgreSQL. CI supplies an isolated database
/// through DIETOEXPRESS_IDEMPOTENCY_TEST_CONNECTION.
/// </summary>
public sealed class IdempotencyPostgresIntegrationTests
{
    private readonly string? _connectionString =
        Environment.GetEnvironmentVariable("DIETOEXPRESS_IDEMPOTENCY_TEST_CONNECTION");

    [Fact]
    public async Task LostResponse_RetryReplaysStoredResponseWithoutRepeatingSideEffect()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return; // The PR workflow always supplies PostgreSQL; local unit-only runs remain usable.

        var key = Guid.NewGuid().ToString("N");
        await EnsureSchemaAsync();

        await InvokeAsync(key, """{"value":"same"}""");
        var replay = await InvokeAsync(key, """{"value":"same"}""");

        Assert.Equal(StatusCodes.Status200OK, replay.StatusCode);
        Assert.Equal("true", replay.Replayed);
        Assert.Equal("""{"created":true}""", replay.Body);
        Assert.Equal(1, await CountEffectsAsync(key));
    }

    [Fact]
    public async Task SameKeyWithDifferentPayload_ReturnsConflictWithoutRepeatingSideEffect()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return;

        var key = Guid.NewGuid().ToString("N");
        await EnsureSchemaAsync();

        await InvokeAsync(key, """{"value":"first"}""");
        var changed = await InvokeAsync(key, """{"value":"different"}""");

        Assert.Equal(StatusCodes.Status409Conflict, changed.StatusCode);
        Assert.Equal(1, await CountEffectsAsync(key));
    }

    [Fact]
    public async Task ConcurrentRequestsWithSameKey_ProduceOneSideEffect()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return;

        var key = Guid.NewGuid().ToString("N");
        await EnsureSchemaAsync();

        var results = await Task.WhenAll(
            InvokeAsync(key, """{"value":"same"}"""),
            InvokeAsync(key, """{"value":"same"}"""));

        Assert.All(results, result => Assert.Equal(StatusCodes.Status200OK, result.StatusCode));
        Assert.Equal(1, results.Count(result => result.Replayed == "true"));
        Assert.Equal(1, await CountEffectsAsync(key));
    }

    private async Task EnsureSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS api_idempotency_records (
                id BIGSERIAL PRIMARY KEY,
                scope_key TEXT NOT NULL,
                idempotency_key VARCHAR(200) NOT NULL,
                request_hash CHAR(64) NOT NULL,
                response_status INTEGER NOT NULL,
                response_content_type VARCHAR(250),
                response_location TEXT,
                response_body BYTEA NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_api_idempotency_scope_key UNIQUE (scope_key, idempotency_key)
            );
            CREATE TABLE IF NOT EXISTS idempotency_test_effects (
                id BIGSERIAL PRIMARY KEY,
                test_key TEXT NOT NULL
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(int StatusCode, string? Replayed, string Body)> InvokeAsync(
        string key, string requestJson)
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using var db = new angulosodbContext(options);
        var middleware = new IdempotencyMiddleware(async context =>
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var transaction = (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction();
            await using var effect = new NpgsqlCommand(
                "INSERT INTO idempotency_test_effects (test_key) VALUES (@key)",
                connection, transaction);
            effect.Parameters.AddWithValue("key", key);
            await effect.ExecuteNonQueryAsync(context.RequestAborted);

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"created":true}""", context.RequestAborted);
        });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/integration/idempotency";
        context.Request.ContentType = "application/json";
        context.Request.Headers["Idempotency-Key"] = key;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "integration-test"),
            new Claim("tenant_id", "integration-test"),
            new Claim("clientId", "integration-test")
        }, "integration-test"));
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new IdempotentAttribute()),
            "idempotency-integration-test"));
        context.RequestServices = new ServiceCollection()
            .AddSingleton(db)
            .BuildServiceProvider();

        await middleware.InvokeAsync(context, db);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode,
            context.Response.Headers["Idempotency-Replayed"].ToString(),
            await reader.ReadToEndAsync());
    }

    private async Task<long> CountEffectsAsync(string key)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM idempotency_test_effects WHERE test_key = @key", connection);
        command.Parameters.AddWithValue("key", key);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
