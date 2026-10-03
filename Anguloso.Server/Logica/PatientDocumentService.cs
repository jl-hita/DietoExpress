using System.Security.Cryptography;
using Npgsql;

namespace Anguloso.Server.Logica;

/// <summary>
/// Crea las copias de los documentos obligatorios de un paciente al finalizar su alta.
/// Los archivos se mantienen en almacenamiento privado; nunca se exponen como recursos estáticos.
/// </summary>
public sealed class PatientDocumentService
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private const long MaxFileSize = 20 * 1024 * 1024;

    public PatientDocumentService(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
    }

    public async Task<int> CreateRequiredDocumentsAsync(int tenantId, int clientId, int? userId, bool forClientCreation = true, CancellationToken cancellationToken = default)
    {
        var created = 0;
        var root = GetStorageRoot();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var templatesCommand = new NpgsqlCommand("""
                SELECT id, name, document_type, version, requires_signature, storage_key, file_name, mime_type, file_size
                FROM document_templates
                WHERE tenant_id=@tenant AND is_active=true
                  AND storage_key IS NOT NULL
                  AND (CASE WHEN @clientCreation THEN is_required_on_client_creation ELSE is_required_before_consultation END)=true
                ORDER BY LOWER(name), version DESC, id DESC;
                """, connection, transaction);
            templatesCommand.Parameters.AddWithValue("tenant", tenantId);
            templatesCommand.Parameters.AddWithValue("clientCreation", forClientCreation);

            await using var reader = await templatesCommand.ExecuteReaderAsync(cancellationToken);
            var templates = new List<TemplateRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                templates.Add(new TemplateRow(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                    reader.GetBoolean(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? "application/pdf" : reader.GetString(7), reader.GetInt64(8)));
            }
            await reader.DisposeAsync();

            foreach (var template in templates)
            {
                if (template.FileSize <= 0 || template.FileSize > MaxFileSize) continue;
                var source = GetSafeTemplatePath(template.StorageKey, tenantId, root);
                if (source == null || !File.Exists(source)) continue;

                var storageKey = $"{tenantId}/{clientId}/{Guid.NewGuid():N}.pdf";
                var destination = GetSafeDocumentPath(storageKey, tenantId, clientId, root);
                if (destination == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                File.Copy(source, destination, overwrite: false);
                var hash = await ComputeSha256Async(destination, cancellationToken);

                await using var insert = new NpgsqlCommand("""
                    INSERT INTO patient_documents
                        (tenant_id, client_id, document_template_id, name, document_type, status,
                         version, requires_signature, storage_key, original_file_name, mime_type,
                         file_size, sha256, created_by_user_id)
                    SELECT @tenant, @client, @template, @name, @type, @status, @version,
                           @requires, @storage, @filename, @mime, @size, @sha, @user
                    WHERE NOT EXISTS (
                        SELECT 1 FROM patient_documents
                        WHERE tenant_id=@tenant AND client_id=@client
                          AND document_template_id=@template AND version=@version
                          AND revoked_at IS NULL
                    );
                    """, connection, transaction);
                insert.Parameters.AddWithValue("tenant", tenantId);
                insert.Parameters.AddWithValue("client", clientId);
                insert.Parameters.AddWithValue("template", template.Id);
                insert.Parameters.AddWithValue("name", template.Name);
                insert.Parameters.AddWithValue("type", template.DocumentType);
                insert.Parameters.AddWithValue("status", template.RequiresSignature ? "pending" : "available");
                insert.Parameters.AddWithValue("version", template.Version);
                insert.Parameters.AddWithValue("requires", template.RequiresSignature);
                insert.Parameters.AddWithValue("storage", storageKey);
                insert.Parameters.AddWithValue("filename", template.FileName ?? $"{template.Name}.pdf");
                insert.Parameters.AddWithValue("mime", template.MimeType);
                insert.Parameters.AddWithValue("size", new FileInfo(destination).Length);
                insert.Parameters.AddWithValue("sha", hash);
                insert.Parameters.AddWithValue("user", (object?)userId ?? DBNull.Value);
                var affected = await insert.ExecuteNonQueryAsync(cancellationToken);

                if (affected > 0)
                {
                    created++;
                    await using var audit = new NpgsqlCommand("""
                        INSERT INTO patient_document_events
                            (tenant_id, patient_document_id, client_id, event_type, details)
                        SELECT tenant_id, id, client_id, 'created',
                               'Documento obligatorio generado automáticamente al crear el paciente.'
                        FROM patient_documents
                        WHERE tenant_id=@tenant AND client_id=@client
                          AND document_template_id=@template AND version=@version
                        ORDER BY id DESC LIMIT 1;
                        """, connection, transaction);
                    audit.Parameters.AddWithValue("tenant", tenantId);
                    audit.Parameters.AddWithValue("client", clientId);
                    audit.Parameters.AddWithValue("template", template.Id);
                    audit.Parameters.AddWithValue("version", template.Version);
                    await audit.ExecuteNonQueryAsync(cancellationToken);
                }
                else
                {
                    try { File.Delete(destination); } catch { }
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return created;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GetPendingSignatureDocumentsAsync(int tenantId, int clientId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT name
            FROM patient_documents
            WHERE tenant_id=@tenant AND client_id=@client
              AND revoked_at IS NULL
              AND requires_signature=true
              AND status='pending'
            ORDER BY created_at, id;
            """, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("client", clientId);
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }

    private string GetStorageRoot()
    {
        var configured = _configuration["DIETOEXPRESS_DOCUMENTS_PATH"];
        var root = string.IsNullOrWhiteSpace(configured) ? "/var/lib/dietoexpress/documents" : configured;
        Directory.CreateDirectory(root);
        return Path.GetFullPath(root);
    }

    private static string? GetSafeTemplatePath(string key, int tenantId, string root)
    {
        var prefix = $"{tenantId}/templates/";
        if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Contains("..") || Path.IsPathRooted(key)) return null;
        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : null;
    }

    private static string? GetSafeDocumentPath(string key, int tenantId, int clientId, string root)
    {
        var prefix = $"{tenantId}/{clientId}/";
        if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Contains("..") || Path.IsPathRooted(key)) return null;
        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : null;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0) sha.AppendData(buffer, 0, read);
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed record TemplateRow(long Id, string Name, string DocumentType, int Version, bool RequiresSignature,
        string StorageKey, string? FileName, string MimeType, long FileSize);
}
