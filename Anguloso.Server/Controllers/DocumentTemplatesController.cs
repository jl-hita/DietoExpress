using System.Security.Cryptography;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

[Route("api/document-templates")]
[ApiController]
[Authorize(Policy = "Professional")]
public class DocumentTemplatesController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private const long MaxFileSize = 20 * 1024 * 1024;

    public DocumentTemplatesController(IConfiguration configuration) => _configuration = configuration;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return Forbid();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, name, description, document_type, version, is_active,
                   is_required_on_client_creation, is_required_before_consultation, requires_signature, file_name, mime_type,
                   file_size, created_at, updated_at
            FROM document_templates
            WHERE tenant_id=@tenant
            ORDER BY is_active DESC, name, version DESC;
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new {
                id = reader.GetInt64(0),
                name = reader.GetString(1),
                description = reader.IsDBNull(2) ? null : reader.GetString(2),
                documentType = reader.GetString(3),
                version = reader.GetInt32(4),
                isActive = reader.GetBoolean(5),
                requiredOnClientCreation = reader.GetBoolean(6),
                requiredBeforeConsultation = reader.GetBoolean(7),
                requiresSignature = reader.GetBoolean(8),
                fileName = reader.IsDBNull(9) ? null : reader.GetString(9),
                mimeType = reader.IsDBNull(10) ? null : reader.GetString(10),
                fileSize = reader.IsDBNull(11) ? 0 : reader.GetInt64(11),
                createdAt = reader.GetDateTime(12),
                updatedAt = reader.GetDateTime(13)
            });
        }
        return Ok(result);
    }

    [HttpPost]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Create(IFormFile file, [FromForm] string name,
        [FromForm] string? description = null, [FromForm] string? documentType = null,
        [FromForm] bool requiredOnClientCreation = false, [FromForm] bool requiredBeforeConsultation = false, [FromForm] bool requiresSignature = false,
        CancellationToken cancellationToken = default)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        var userId = AuthHelpers.GetUserId(User);
        if (!tenantId.HasValue || !userId.HasValue) return Forbid();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) return BadRequest("El nombre es obligatorio y no puede superar 200 caracteres.");
        if (file == null || file.Length == 0) return BadRequest("Debes seleccionar un PDF.");
        if (file.Length > MaxFileSize) return BadRequest("El documento no puede superar los 20 MB.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Solo se admiten documentos PDF.");
        if (!await LooksLikePdfAsync(file, cancellationToken)) return BadRequest("El archivo no parece un PDF válido.");

        var version = 1;
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        // Serialize version creation per tenant + logical template name. MAX(version)+1 alone
        // is racy when two professionals upload the same template concurrently.
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, tx))
        {
            lockCommand.Parameters.AddWithValue(
                "lock_key",
                $"{tenantId.Value}:document-template:{name.Trim().ToLowerInvariant()}");
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var versionCommand = new NpgsqlCommand("SELECT COALESCE(MAX(version),0)+1 FROM document_templates WHERE tenant_id=@tenant AND LOWER(name)=LOWER(@name);", connection, tx))
        {
            versionCommand.Parameters.AddWithValue("tenant", tenantId.Value);
            versionCommand.Parameters.AddWithValue("name", name.Trim());
            version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));
        }

        // A logical template has one active version. Deactivate the previous active version
        // inside the same transaction before publishing the new one.
        await using (var deactivateCommand = new NpgsqlCommand("""
            UPDATE document_templates
            SET is_active=false, updated_at=NOW()
            WHERE tenant_id=@tenant AND LOWER(name)=LOWER(@name) AND is_active=true;
            """, connection, tx))
        {
            deactivateCommand.Parameters.AddWithValue("tenant", tenantId.Value);
            deactivateCommand.Parameters.AddWithValue("name", name.Trim());
            await deactivateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var storageKey = $"{tenantId.Value}/templates/{Guid.NewGuid():N}.pdf";
        var root = GetStorageRoot();
        var path = Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var input = file.OpenReadStream())
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await input.CopyToAsync(output, cancellationToken);

        var hash = await ComputeSha256Async(path, cancellationToken);
        try
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO document_templates
                    (tenant_id,name,description,document_type,file_name,storage_key,mime_type,file_size,sha256,
                     version,is_active,is_required_on_client_creation,is_required_before_consultation,requires_signature,created_by_user_id)
                VALUES (@tenant,@name,@description,@type,@filename,@storage,@mime,@size,@sha,@version,
                        true,@required,@requiredConsultation,@signature,@user)
                RETURNING id;
                """, connection, tx);
            insert.Parameters.AddWithValue("tenant", tenantId.Value);
            insert.Parameters.AddWithValue("name", name.Trim());
            insert.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
            insert.Parameters.AddWithValue("type", string.IsNullOrWhiteSpace(documentType) ? "other" : documentType.Trim().ToLowerInvariant());
            insert.Parameters.AddWithValue("filename", Path.GetFileName(file.FileName));
            insert.Parameters.AddWithValue("storage", storageKey);
            insert.Parameters.AddWithValue("mime", "application/pdf");
            insert.Parameters.AddWithValue("size", file.Length);
            insert.Parameters.AddWithValue("sha", hash);
            insert.Parameters.AddWithValue("version", version);
            insert.Parameters.AddWithValue("required", requiredOnClientCreation);
            insert.Parameters.AddWithValue("requiredConsultation", requiredBeforeConsultation);
            insert.Parameters.AddWithValue("signature", requiresSignature);
            insert.Parameters.AddWithValue("user", userId.Value);
            var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            await tx.CommitAsync(cancellationToken);
            return Ok(new { id, version, sha256 = hash });
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            try { System.IO.File.Delete(path); } catch { }
            throw;
        }
    }

    [HttpPatch("{id:long}/active")]
    public async Task<IActionResult> SetActive(long id, [FromBody] SetActiveRequest request, CancellationToken cancellationToken)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return Forbid();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        if (request.Active)
        {
            // Reuse the same tenant + logical-name lock as version creation so two
            // concurrent activations cannot leave two versions active.
            await using var lockCommand = new NpgsqlCommand("""
                SELECT pg_advisory_xact_lock(
                    hashtextextended(
                        @lock_key,
                        0));
                """, connection, tx);
            lockCommand.Parameters.AddWithValue(
                "lock_key",
                $"{tenantId.Value}:document-template-active:{id}");
            // Locking by id alone would not protect sibling versions. Resolve the
            // logical name first and then acquire the stable name-based lock.
            await lockCommand.DisposeAsync();

            await using var nameCommand = new NpgsqlCommand(
                "SELECT name FROM document_templates WHERE id=@id AND tenant_id=@tenant;",
                connection, tx);
            nameCommand.Parameters.AddWithValue("id", id);
            nameCommand.Parameters.AddWithValue("tenant", tenantId.Value);
            var templateName = Convert.ToString(await nameCommand.ExecuteScalarAsync(cancellationToken));
            if (string.IsNullOrWhiteSpace(templateName))
            {
                await tx.RollbackAsync(cancellationToken);
                return NotFound();
            }

            await using var logicalLock = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));",
                connection, tx);
            logicalLock.Parameters.AddWithValue(
                "lock_key",
                $"{tenantId.Value}:document-template:{templateName.Trim().ToLowerInvariant()}");
            await logicalLock.ExecuteNonQueryAsync(cancellationToken);

            await using var deactivate = new NpgsqlCommand("""
                UPDATE document_templates
                SET is_active=false, updated_at=NOW()
                WHERE tenant_id=@tenant AND LOWER(name)=LOWER(@name)
                  AND is_active=true AND id<>@id;
                """, connection, tx);
            deactivate.Parameters.AddWithValue("tenant", tenantId.Value);
            deactivate.Parameters.AddWithValue("name", templateName);
            deactivate.Parameters.AddWithValue("id", id);
            await deactivate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand("UPDATE document_templates SET is_active=@active, updated_at=NOW() WHERE id=@id AND tenant_id=@tenant;", connection, tx);
        command.Parameters.AddWithValue("active", request.Active);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", tenantId.Value);
        var count = await command.ExecuteNonQueryAsync(cancellationToken);
        if (count == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return NotFound();
        }

        await tx.CommitAsync(cancellationToken);
        return NoContent();
    }

    public sealed record SetActiveRequest(bool Active);

    private string ConnectionString => _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection no está configurada.");

    private string GetStorageRoot()
    {
        var configured = _configuration["DIETOEXPRESS_DOCUMENTS_PATH"];
        var root = string.IsNullOrWhiteSpace(configured) ? "/var/lib/dietoexpress/documents" : configured;
        Directory.CreateDirectory(root);
        return Path.GetFullPath(root);
    }

    private static async Task<bool> LooksLikePdfAsync(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[5];
        var read = await stream.ReadAsync(header, ct);
        return read == 5 && System.Text.Encoding.ASCII.GetString(header) == "%PDF-";
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0) sha.AppendData(buffer, 0, read);
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
