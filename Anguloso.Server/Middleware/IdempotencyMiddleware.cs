using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Anguloso.Server.Middleware;

/// <summary>
/// Idempotency contract for write requests that opt in with Idempotency-Key.
/// The action and its idempotency result share one PostgreSQL transaction.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private const int MaxKeyLength = 200;
    private readonly RequestDelegate _next;
    public IdempotencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, angulosodbContext db)
    {
        var request = context.Request;
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>() is null ||
            !IsWriteMethod(request.Method))
        {
            await _next(context);
            return;
        }

        if (!request.Headers.TryGetValue("Idempotency-Key", out var values))
        {
            await _next(context);
            return;
        }

        if (values.Count != 1)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "invalid_idempotency_key",
                message = "La cabecera Idempotency-Key debe aparecer una sola vez."
            });
            return;
        }

        var key = values[0]?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength ||
            key.Any(char.IsControl))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "invalid_idempotency_key",
                message = $"La cabecera Idempotency-Key debe contener entre 1 y {MaxKeyLength} caracteres válidos."
            });
            return;
        }

        request.EnableBuffering();
        byte[] requestBody;
        await using (var buffer = new MemoryStream())
        {
            await request.Body.CopyToAsync(buffer, context.RequestAborted);
            requestBody = buffer.ToArray();
        }
        request.Body.Position = 0;

        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? "anonymous";
        var scope = $"{subject}\n{request.Method.ToUpperInvariant()}\n{request.PathBase}{request.Path}{request.QueryString}";
        var fingerprintInput = Encoding.UTF8.GetBytes(scope + "\n" + (request.ContentType ?? string.Empty) + "\n" + Convert.ToBase64String(requestBody));
        var fingerprint = Convert.ToHexString(SHA256.HashData(fingerprintInput));
        var scopeHash = SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\n" + key));
        var lockKey = BitConverter.ToInt64(scopeHash, 0);

        await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var pgTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@lock_key)", connection, pgTransaction))
        {
            lockCommand.Parameters.AddWithValue("lock_key", lockKey);
            await lockCommand.ExecuteNonQueryAsync(context.RequestAborted);
        }

        // Las claves no caducan automáticamente: una outbox offline puede reintentarse semanas o meses después.

        await using (var lookup = new NpgsqlCommand("""
            SELECT request_hash, response_status, response_content_type, response_location, response_body
            FROM api_idempotency_records
            WHERE scope_key = @scope AND idempotency_key = @key
            """, connection, pgTransaction))
        {
            lookup.Parameters.AddWithValue("scope", scope);
            lookup.Parameters.AddWithValue("key", key);
            await using var reader = await lookup.ExecuteReaderAsync(context.RequestAborted);
            if (await reader.ReadAsync(context.RequestAborted))
            {
                var storedFingerprint = reader.GetString(0);
                var status = reader.GetInt32(1);
                var contentType = reader.IsDBNull(2) ? null : reader.GetString(2);
                var location = reader.IsDBNull(3) ? null : reader.GetString(3);
                var body = reader.IsDBNull(4) ? Array.Empty<byte>() : (byte[])reader[4];
                await reader.CloseAsync();

                if (!CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(storedFingerprint),
                        Encoding.ASCII.GetBytes(fingerprint)))
                {
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "idempotency_key_reused",
                        message = "La misma Idempotency-Key no puede reutilizarse con una petición diferente."
                    });
                    return;
                }

                context.Response.StatusCode = status;
                if (!string.IsNullOrWhiteSpace(contentType))
                    context.Response.ContentType = contentType;
                if (!string.IsNullOrWhiteSpace(location))
                    context.Response.Headers["Location"] = location;
                context.Response.Headers["Idempotency-Replayed"] = "true";
                if (body.Length > 0)
                    await context.Response.Body.WriteAsync(body, context.RequestAborted);
                await transaction.CommitAsync(context.RequestAborted);
                return;
            }
        }

        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;
        try
        {
            await _next(context);
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                var responseBody = responseBuffer.ToArray();
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO api_idempotency_records
                        (scope_key, idempotency_key, request_hash, response_status, response_content_type, response_location, response_body, created_at)
                    VALUES (@scope, @key, @hash, @status, @content_type, @location, @body, NOW())
                    """, connection, pgTransaction);
                insert.Parameters.AddWithValue("scope", scope);
                insert.Parameters.AddWithValue("key", key);
                insert.Parameters.AddWithValue("hash", fingerprint);
                insert.Parameters.AddWithValue("status", context.Response.StatusCode);
                insert.Parameters.AddWithValue("content_type", (object?)context.Response.ContentType ?? DBNull.Value);
                var locationHeader = context.Response.Headers["Location"].ToString();
                insert.Parameters.AddWithValue("location", locationHeader.Length > 0 ? locationHeader : DBNull.Value);
                insert.Parameters.AddWithValue("body", responseBody);
                await insert.ExecuteNonQueryAsync(context.RequestAborted);
                await transaction.CommitAsync(context.RequestAborted);
            }
            else
            {
                await transaction.RollbackAsync(context.RequestAborted);
            }

            context.Response.Body = originalBody;
            responseBuffer.Position = 0;
            await responseBuffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        catch
        {
            context.Response.Body = originalBody;
            throw;
        }
    }

    private static bool IsWriteMethod(string method) =>
        HttpMethods.IsPost(method) ||
        HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) ||
        HttpMethods.IsDelete(method);
}
