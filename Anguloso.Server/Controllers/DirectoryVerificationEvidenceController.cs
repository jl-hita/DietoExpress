using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Cryptography;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Evidencia privada de identidad y titulación usada por el flujo de verificación del directorio.
/// Los archivos nunca se sirven desde wwwroot ni mediante URLs públicas.
/// </summary>
[ApiController]
[Route("api/directory-verification/evidence")]
[Authorize]
public class DirectoryVerificationEvidenceController : ControllerBase
{
    private const long MaxFileSize = 20 * 1024 * 1024;
    private static readonly string[] AllowedMimeTypes = ["application/pdf", "image/jpeg", "image/png"];
    private readonly IConfiguration _configuration;
    private readonly angulosodbContext _context;
    private readonly IAuditLogService _audit;

    public DirectoryVerificationEvidenceController(
        IConfiguration configuration,
        angulosodbContext context,
        IAuditLogService audit)
    {
        _configuration = configuration;
        _context = context;
        _audit = audit;
    }

    [HttpGet("mine")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (!userId.HasValue) return Forbid();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT id, evidence_type, original_file_name, mime_type, file_size, sha256,
                   status, review_note, uploaded_at, reviewed_at
            FROM directory_verification_evidence
            WHERE user_id=@user AND revoked_at IS NULL
            ORDER BY evidence_type, uploaded_at DESC;
            """, connection);
        command.Parameters.AddWithValue("user", userId.Value);

        return Ok(await ReadEvidenceAsync(command, ct));
    }

    [HttpPost("mine")]
    [Authorize(Policy = "Professional")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> UploadMine(
        IFormFile file,
        [FromForm] string evidenceType,
        CancellationToken ct)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (!userId.HasValue) return Forbid();

        var type = evidenceType?.Trim().ToLowerInvariant();
        if (type is not ("identity" or "qualification"))
            return BadRequest("El tipo de evidencia debe ser identity o qualification.");

        var validation = ValidateFile(file);
        if (validation != null) return BadRequest(validation);

        var user = await _context.users.FindAsync([userId.Value], ct);
        if (user == null || user.role != "nutritionist" || user.archived_at != null)
            return Forbid();

        var storageKey = $"{userId.Value}/directory-verification/{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        var path = GetStoragePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string hash;
        try
        {
            await using var input = file.OpenReadStream();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                sha.AppendData(buffer, 0, read);
            }
            hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(ct);

            await using (var revoke = new NpgsqlCommand("""
                UPDATE directory_verification_evidence
                SET revoked_at=NOW()
                WHERE user_id=@user AND evidence_type=@type AND revoked_at IS NULL;
                """, connection, tx))
            {
                revoke.Parameters.AddWithValue("user", userId.Value);
                revoke.Parameters.AddWithValue("type", type);
                await revoke.ExecuteNonQueryAsync(ct);
            }

            await using var insert = new NpgsqlCommand("""
                INSERT INTO directory_verification_evidence
                    (user_id, evidence_type, original_file_name, mime_type, file_size, storage_key, sha256, status, uploaded_at)
                VALUES (@user,@type,@name,@mime,@size,@storage,@sha,'pending',NOW())
                RETURNING id;
                """, connection, tx);
            insert.Parameters.AddWithValue("user", userId.Value);
            insert.Parameters.AddWithValue("type", type);
            insert.Parameters.AddWithValue("name", Path.GetFileName(file.FileName));
            insert.Parameters.AddWithValue("mime", file.ContentType);
            insert.Parameters.AddWithValue("size", file.Length);
            insert.Parameters.AddWithValue("storage", storageKey);
            insert.Parameters.AddWithValue("sha", hash);
            var id = Convert.ToInt64(await insert.ExecuteScalarAsync(ct));
            await tx.CommitAsync(ct);

            user.directory_publication_status = "pending";
            user.directory_verified_at = null;
            user.directory_verified_by_user_id = null;
            await _context.SaveChangesAsync(ct);

            await _audit.LogAccessAsync("PUBLIC_DIRECTORY_EVIDENCE_UPLOADED", "user", userId.Value.ToString(),
                details: $"evidenceType={type}; evidenceId={id}; sha256={hash}");

            return Ok(new { id, status = "pending", sha256 = hash });
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (!userId.HasValue) return Forbid();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT user_id, original_file_name, mime_type, storage_key, status
            FROM directory_verification_evidence
            WHERE id=@id AND revoked_at IS NULL;
            """, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return NotFound();

        var ownerId = reader.GetInt32(0);
        var isSuperAdmin = User.IsInRole("superadmin");
        if (!isSuperAdmin && ownerId != userId.Value) return Forbid();

        var name = reader.GetString(1);
        var mime = reader.GetString(2);
        var path = GetStoragePath(reader.GetString(3));
        await reader.CloseAsync();

        if (!System.IO.File.Exists(path)) return NotFound();
        await _audit.LogAccessAsync("PUBLIC_DIRECTORY_EVIDENCE_ACCESSED", "user", ownerId.ToString(),
            details: $"evidenceId={id}; by={userId.Value}");
        return PhysicalFile(path, mime, Path.GetFileName(name), enableRangeProcessing: true);
    }

    [HttpGet("admin")]
    [Authorize(Roles = "superadmin")]
    public async Task<IActionResult> AdminList([FromQuery] string? status, CancellationToken ct)
    {
        var normalized = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        if (normalized is not null && normalized is not ("pending" or "approved" or "rejected"))
            return BadRequest("Estado de evidencia no válido.");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT e.id, e.user_id, u.username, u.full_name, e.evidence_type,
                   e.original_file_name, e.mime_type, e.file_size, e.sha256, e.status,
                   e.review_note, e.uploaded_at, e.reviewed_at
            FROM directory_verification_evidence e
            JOIN users u ON u.id=e.user_id
            WHERE e.revoked_at IS NULL AND (@status IS NULL OR e.status=@status)
            ORDER BY CASE WHEN e.status='pending' THEN 0 ELSE 1 END, e.uploaded_at;
            """, connection);
        command.Parameters.AddWithValue("status", (object?)normalized ?? DBNull.Value);

        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new {
                id=reader.GetInt64(0), userId=reader.GetInt32(1), username=reader.GetString(2),
                fullName=reader.IsDBNull(3) ? null : reader.GetString(3), evidenceType=reader.GetString(4),
                originalFileName=reader.GetString(5), mimeType=reader.GetString(6), fileSize=reader.GetInt64(7),
                sha256=reader.GetString(8), status=reader.GetString(9),
                reviewNote=reader.IsDBNull(10) ? null : reader.GetString(10),
                uploadedAt=reader.GetDateTime(11), reviewedAt=reader.IsDBNull(12) ? null : reader.GetDateTime(12)
            });
        }
        return Ok(rows);
    }

    [HttpPut("admin/{id:long}")]
    [Authorize(Roles = "superadmin")]
    public async Task<IActionResult> Review(long id, [FromBody] EvidenceReviewRequest request, CancellationToken ct)
    {
        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not ("approved" or "rejected"))
            return BadRequest("El estado debe ser approved o rejected.");

        var note = request.Note?.Trim();
        if (note?.Length > 2000) return BadRequest("La nota no puede superar los 2000 caracteres.");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        int userId;
        string type;
        await using (var find = new NpgsqlCommand("""
            SELECT user_id, evidence_type FROM directory_verification_evidence
            WHERE id=@id AND revoked_at IS NULL FOR UPDATE;
            """, connection, tx))
        {
            find.Parameters.AddWithValue("id", id);
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return NotFound();
            userId = reader.GetInt32(0);
            type = reader.GetString(1);
        }

        await using (var update = new NpgsqlCommand("""
            UPDATE directory_verification_evidence
            SET status=@status, review_note=@note, reviewed_at=NOW(), reviewed_by_user_id=@reviewer
            WHERE id=@id;
            """, connection, tx))
        {
            update.Parameters.AddWithValue("status", status);
            update.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);
            update.Parameters.AddWithValue("reviewer", AuthHelpers.GetUserId(User) ?? 0);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        await _audit.LogAccessAsync("PUBLIC_DIRECTORY_EVIDENCE_REVIEWED", "user", userId.ToString(),
            details: $"evidenceId={id}; type={type}; status={status}");

        return Ok(new { id, userId, evidenceType = type, status, reviewNote = note });
    }

    private string ConnectionString => _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection no está configurada.");

    private string GetStoragePath(string storageKey)
    {
        var root = _configuration["DIETOEXPRESS_DOCUMENTS_PATH"];
        if (string.IsNullOrWhiteSpace(root)) root = "/var/lib/dietoexpress/documents";
        var fullRoot = Path.GetFullPath(root);
        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ruta de almacenamiento no válida.");
        return path;
    }

    private static string? ValidateFile(IFormFile file)
    {
        if (file == null || file.Length == 0) return "Debes seleccionar un archivo.";
        if (file.Length > MaxFileSize) return "El archivo no puede superar los 20 MB.";
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedMimeTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase) ||
            !new[] { ".pdf", ".jpg", ".jpeg", ".png" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return "Solo se admiten PDF, JPG, JPEG o PNG.";
        return null;
    }

    private static async Task<List<object>> ReadEvidenceAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new {
                id=reader.GetInt64(0), evidenceType=reader.GetString(1), originalFileName=reader.GetString(2),
                mimeType=reader.GetString(3), fileSize=reader.GetInt64(4), sha256=reader.GetString(5),
                status=reader.GetString(6), reviewNote=reader.IsDBNull(7) ? null : reader.GetString(7),
                uploadedAt=reader.GetDateTime(8), reviewedAt=reader.IsDBNull(9) ? null : reader.GetDateTime(9)
            });
        return rows;
    }

    private static void TryDelete(string path)
    {
        try { System.IO.File.Delete(path); } catch { }
    }
}

public sealed record EvidenceReviewRequest(string Status, string? Note);
