using System.Security.Cryptography;
using System.Text;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/legal-documents")]
public sealed class LegalDocumentsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public LegalDocumentsController(IConfiguration configuration) => _configuration = configuration;

    /// <summary>
    /// Returns the currently published platform documents. This endpoint is deliberately
    /// independent from tenant patient-document templates.
    /// </summary>
    [HttpGet("current")]
    [AllowAnonymous]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT ON (document_key)
                   document_key, version, title, document_type, content,
                   effective_from, sha256, published_at
            FROM legal_documents
            WHERE status='published'
            ORDER BY document_key, version DESC;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new
            {
                key = reader.GetString(0),
                version = reader.GetInt32(1),
                title = reader.GetString(2),
                documentType = reader.GetString(3),
                content = reader.GetString(4),
                effectiveFrom = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5),
                sha256 = reader.GetString(6),
                publishedAt = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7)
            });
        }

        return Ok(result);
    }


    [HttpGet("admin")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> AdminList(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("superadmin")) return Forbid();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, document_key, version, title, document_type, status,
                   effective_from, sha256, created_at, published_at
            FROM legal_documents
            ORDER BY document_key, version DESC;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new
            {
                id = reader.GetInt64(0),
                key = reader.GetString(1),
                version = reader.GetInt32(2),
                title = reader.GetString(3),
                documentType = reader.GetString(4),
                status = reader.GetString(5),
                effectiveFrom = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6),
                sha256 = reader.GetString(7),
                createdAt = reader.GetDateTime(8),
                publishedAt = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9)
            });
        }
        return Ok(result);
    }

    [HttpPost("admin")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> CreateAdmin([FromBody] UpsertLegalDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("superadmin")) return Forbid();
        if (request == null || string.IsNullOrWhiteSpace(request.DocumentKey) ||
            string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Clave, título y contenido son obligatorios.");

        var key = request.DocumentKey.Trim().ToLowerInvariant();
        var type = string.IsNullOrWhiteSpace(request.DocumentType) ? "legal" : request.DocumentType.Trim().ToLowerInvariant();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using var versionCommand = new NpgsqlCommand(
            "SELECT COALESCE(MAX(version),0)+1 FROM legal_documents WHERE document_key=@key;",
            connection, tx);
        versionCommand.Parameters.AddWithValue("key", key);
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Content))).ToLowerInvariant();

        await using var insert = new NpgsqlCommand("""
            INSERT INTO legal_documents
                (document_key, version, title, document_type, content, status,
                 effective_from, sha256, created_at, published_at)
            VALUES
                (@key,@version,@title,@type,@content,@status,@effective,@sha,NOW(),
                 CASE WHEN @status='published' THEN NOW() ELSE NULL END)
            RETURNING id;
            """, connection, tx);
        insert.Parameters.AddWithValue("key", key);
        insert.Parameters.AddWithValue("version", version);
        insert.Parameters.AddWithValue("title", request.Title.Trim());
        insert.Parameters.AddWithValue("type", type);
        insert.Parameters.AddWithValue("content", request.Content);
        insert.Parameters.AddWithValue("status", string.Equals(request.Status, "published", StringComparison.OrdinalIgnoreCase) ? "published" : "draft");
        insert.Parameters.AddWithValue("effective", (object?)request.EffectiveFrom ?? DBNull.Value);
        insert.Parameters.AddWithValue("sha", hash);

        var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        await tx.CommitAsync(cancellationToken);
        return Ok(new { id, key, version, sha256 = hash });
    }

    [HttpPost("accept")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> Accept([FromBody] AcceptLegalDocumentRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.DocumentKey))
            return BadRequest("Documento legal no válido.");

        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!userId.HasValue) return Unauthorized();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = new NpgsqlCommand("""
            SELECT id, document_key, version, sha256
            FROM legal_documents
            WHERE document_key=@key
              AND status='published'
              AND (@version IS NULL OR version=@version)
            ORDER BY version DESC
            LIMIT 1;
            """, connection, tx);
        command.Parameters.AddWithValue("key", request.DocumentKey.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("version", (object?)request.Version ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Conflict("No existe una versión publicada y vigente de ese documento.");
        var documentId = reader.GetInt64(0);
        var key = reader.GetString(1);
        var version = reader.GetInt32(2);
        var sha256 = reader.GetString(3);
        await reader.CloseAsync();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        if (userAgent.Length > 500) userAgent = userAgent[..500];

        await using var insert = new NpgsqlCommand("""
            INSERT INTO legal_acceptances
                (user_id, tenant_id, legal_document_id, document_key, document_version,
                 document_sha256, accepted_at, ip_address, user_agent, context)
            VALUES
                (@user,@tenant,@document,@key,@version,@sha,NOW(),@ip,@ua,@context)
            ON CONFLICT (user_id, legal_document_id, document_version, context) DO NOTHING;
            """, connection, tx);
        insert.Parameters.AddWithValue("user", userId.Value);
        insert.Parameters.AddWithValue("tenant", (object?)tenantId ?? DBNull.Value);
        insert.Parameters.AddWithValue("document", documentId);
        insert.Parameters.AddWithValue("key", key);
        insert.Parameters.AddWithValue("version", version);
        insert.Parameters.AddWithValue("sha", sha256);
        insert.Parameters.AddWithValue("ip", (object?)ip ?? DBNull.Value);
        insert.Parameters.AddWithValue("ua", (object?)userAgent ?? DBNull.Value);
        insert.Parameters.AddWithValue("context", string.IsNullOrWhiteSpace(request.Context) ? "signup" : request.Context.Trim().ToLowerInvariant());

        await insert.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return Ok(new { key, version, sha256, acceptedAt = DateTime.UtcNow });
    }

    [HttpGet("acceptances")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> Acceptances(CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (!userId.HasValue) return Unauthorized();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT document_key, document_version, document_sha256, accepted_at, context
            FROM legal_acceptances
            WHERE user_id=@user
            ORDER BY accepted_at DESC;
            """, connection);
        command.Parameters.AddWithValue("user", userId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new
            {
                key = reader.GetString(0),
                version = reader.GetInt32(1),
                sha256 = reader.GetString(2),
                acceptedAt = reader.GetDateTime(3),
                context = reader.GetString(4)
            });
        }

        return Ok(result);
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
}

public sealed record UpsertLegalDocumentRequest(
    string DocumentKey,
    string Title,
    string Content,
    string? DocumentType = null,
    string? Status = null,
    DateTime? EffectiveFrom = null);

public sealed record AcceptLegalDocumentRequest(
    string DocumentKey,
    int? Version = null,
    string? Context = null);
