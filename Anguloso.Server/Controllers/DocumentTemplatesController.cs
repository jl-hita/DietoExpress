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
                   is_required_on_client_creation, requires_signature, file_name, mime_type,
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
                requiresSignature = reader.GetBoolean(7),
                fileName = reader.IsDBNull(8) ? null : reader.GetString(8),
                mimeType = reader.IsDBNull(9) ? null : reader.GetString(9),
                fileSize = reader.IsDBNull(10) ? 0 : reader.GetInt64(10),
                createdAt = reader.GetDateTime(11),
                updatedAt = reader.GetDateTime(12)
            });
        }
        return Ok(result);
    }

    [HttpPost]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Create(IFormFile file, [FromForm] string name,
        [FromForm] string? description = null, [FromForm] string? documentType = null,
        [FromForm] bool requiredOnClientCreation = false, [FromForm] bool requiresSignature = false,
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

        await using (var versionCommand = new NpgsqlCommand("SELECT COALESCE(MAX(version),0)+1 FROM document_templates WHERE tenant_id=@tenant AND name=@name;", connection, tx))
        {
            versionCommand.Parameters.AddWithValue("tenant", tenantId.Value);
            versionCommand.Parameters.AddWithValue("name", name.Trim());
            version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));
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
                     version,is_active,is_required_on_client_creation,requires_signature,created_by_user_id)
                VALUES (@tenant,@name,@description,@type,@filename,@storage,@mime,@size,@sha,@version,
                        true,@required,@signature,@user)
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
    public async Task<IActionResult> SetActive(long id, [FromBody] bool active, CancellationToken cancellationToken)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return Forbid();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE document_templates SET is_active=@active, updated_at=NOW() WHERE id=@id AND tenant_id=@tenant;", connection);
        command.Parameters.AddWithValue("active", active);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", tenantId.Value);
        var count = await command.ExecuteNonQueryAsync(cancellationToken);
        return count == 0 ? NotFound() : NoContent();
    }

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
