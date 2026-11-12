using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anguloso.Server.Logica;

/// <summary>
/// Exercise illustrations served from OUR backend, so client apps never contact third-party hosts.
///
/// Storage: table <c>exercise_images</c> (bytes in PostgreSQL), one row per (exercise, slot):
/// slot 0 = start/main picture, slot 1 = peak/end picture. Rows come from two sources:
///  - <c>repdb-free</c>: copied lazily, ONE image at a time, the first time a prescribed exercise needs
///    it (read-through cache) from the fixed RepDB host, using the path stored in the catalog row.
///    There is no bulk mirroring and no endpoint that lists or exports images; they are only served to
///    authenticated users inside the app, with the visible RepDB attribution (RepDB Free Tier terms:
///    in-app use, no redistribution as dataset or API).
///  - <c>custom</c>: uploaded by the owner of a professional contribution (or a superadmin), which is
///    how exercises that do not come from RepDB get pictures.
/// </summary>
public sealed class ExerciseImageService
{
    public const string RepDbImageHost = "https://exercise-dataset.com/";
    public const int MaximumImageBytes = 2 * 1024 * 1024;
    public const int MaximumSlot = 3;
    private static readonly Regex SafeRepDbPath = new(@"^/?[A-Za-z0-9][A-Za-z0-9_\-./]{0,200}\.(webp|png|jpg|jpeg)$", RegexOptions.Compiled);

    private readonly angulosodbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExerciseImageService> _logger;

    public ExerciseImageService(angulosodbContext db, IHttpClientFactory httpClientFactory, ILogger<ExerciseImageService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Maps catalog <c>images</c> JSON (flat.start/main/peak) to slots: 0 = start or main, 1 = peak.</summary>
    public static IReadOnlyDictionary<int, string> ResolveRepDbSlotPaths(string? imagesJson)
    {
        var result = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(imagesJson)) return result;
        try
        {
            using var document = JsonDocument.Parse(imagesJson);
            if (!document.RootElement.TryGetProperty("flat", out var flat) || flat.ValueKind != JsonValueKind.Object)
                return result;
            string? Path(string name)
            {
                if (!flat.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                    return null;
                var path = value.GetString()!;
                var normalized = path.Replace('\\\\', '/').ToLowerInvariant();
                if (!SafeRepDbPath.IsMatch(path) ||
                    normalized.Split('/').Any(segment => segment is "premium-samples" or "evaluation" or "evaluation-only"))
                    return null;
                return path;
            }
            var first = Path("start") ?? Path("main");
            if (first is not null) result[0] = first;
            var second = Path("peak");
            if (second is not null && first is not null) result[1] = second;
        }
        catch (JsonException) { }
        return result;
    }

    /// <summary>Detects an image type from its first bytes; unknown content is rejected (never trust a client content type).</summary>
    public static string? DetectImageContentType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
            data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P') return "image/webp";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "image/png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        return null;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS exercise_images (
                id BIGSERIAL PRIMARY KEY,
                exercise_id VARCHAR(100) NOT NULL,
                slot SMALLINT NOT NULL CHECK (slot BETWEEN 0 AND 3),
                content_type VARCHAR(40) NOT NULL,
                data BYTEA NOT NULL,
                byte_size INTEGER NOT NULL CHECK (byte_size > 0),
                sha256 CHAR(64) NOT NULL,
                source VARCHAR(40) NOT NULL CHECK (source IN ('repdb-free','custom')),
                uploaded_by_user_id INTEGER NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_exercise_images_slot UNIQUE (exercise_id, slot)
            );
            INSERT INTO schema_migrations(id) VALUES ('exercise-images-v1') ON CONFLICT (id) DO NOTHING;
            """;
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { if (shouldClose) await connection.CloseAsync(); }
    }

    /// <summary>Slots that can be served for an exercise: stored images plus those derivable from the catalog.</summary>
    public async Task<IReadOnlyList<int>> GetAvailableSlotsAsync(string exerciseId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var slots = new SortedSet<int>();
        await WithConnectionAsync(async connection =>
        {
            await using (var stored = new NpgsqlCommand("SELECT slot FROM exercise_images WHERE exercise_id = @id", connection))
            {
                stored.Parameters.AddWithValue("id", exerciseId);
                await using var reader = await stored.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) slots.Add(reader.GetInt16(0));
            }
            var imagesJson = await ReadCatalogImagesJsonAsync(connection, exerciseId, cancellationToken);
            foreach (var slot in ResolveRepDbSlotPaths(imagesJson).Keys) slots.Add(slot);
        });
        return slots.ToArray();
    }

    public async Task<ExerciseImage?> GetImageAsync(string exerciseId, int slot, CancellationToken cancellationToken = default)
    {
        if (slot is < 0 or > MaximumSlot || string.IsNullOrWhiteSpace(exerciseId) || exerciseId.Length > 100) return null;
        await EnsureSchemaAsync(cancellationToken);
        ExerciseImage? image = null;
        string? repDbPath = null;
        await WithConnectionAsync(async connection =>
        {
            image = await ReadStoredAsync(connection, exerciseId, slot, cancellationToken);
            if (image is null)
            {
                var imagesJson = await ReadCatalogImagesJsonAsync(connection, exerciseId, cancellationToken);
                ResolveRepDbSlotPaths(imagesJson).TryGetValue(slot, out repDbPath);
            }
        });
        if (image is not null || repDbPath is null) return image;

        // Read-through: fetch this single picture from the fixed RepDB host and keep our own copy.
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync(new Uri(new Uri(RepDbImageHost), repDbPath.TrimStart('/')),
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength > MaximumImageBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0 || bytes.Length > MaximumImageBytes) return null;
            var contentType = DetectImageContentType(bytes);
            if (contentType is null) return null;
            await StoreAsync(exerciseId, slot, bytes, contentType, "repdb-free", null, overwrite: false, cancellationToken);
            return new ExerciseImage(bytes, contentType, "repdb-free");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "No se pudo copiar la imagen {ExerciseId}/{Slot} desde la fuente.", exerciseId, slot);
            return null;
        }
    }

    /// <summary>Stores an uploaded picture for an exercise. Returns an error message or null on success.</summary>
    public async Task<string?> SaveCustomImageAsync(
        string exerciseId, int slot, byte[] data, int userId, CancellationToken cancellationToken = default)
    {
        if (slot is < 0 or > MaximumSlot) return $"La posición de la imagen debe estar entre 0 y {MaximumSlot}.";
        if (data.Length == 0 || data.Length > MaximumImageBytes) return "La imagen debe pesar entre 1 byte y 2 MB.";
        var contentType = DetectImageContentType(data);
        if (contentType is null) return "Formato no admitido. Usa WebP, PNG o JPEG.";
        await EnsureSchemaAsync(cancellationToken);
        await StoreAsync(exerciseId, slot, data, contentType, "custom", userId, overwrite: true, cancellationToken);
        return null;
    }

    // Curated SVG is accepted only from the trusted deployment seed, never from user uploads.
    private static bool IsSafeCuratedSvg(ReadOnlySpan<byte> data)
    {
        try
        {
            var svg = System.Text.Encoding.UTF8.GetString(data);
            return svg.StartsWith("<svg ", StringComparison.Ordinal) &&
                svg.Contains("xmlns=\"http://www.w3.org/2000/svg\"", StringComparison.Ordinal) &&
                !Regex.IsMatch(svg, @"<\s*(script|foreignObject|iframe|object|embed)\b|\bon[a-z]+\s*=|(?:href|src)\s*=|url\s*\(|javascript:|<!DOCTYPE|<!ENTITY", RegexOptions.IgnoreCase);
        }
        catch { return false; }
    }

    // ---- Deployment seed (curated illustrations are versioned; RepDB bulk exports remain excluded) ----

    public sealed record SeedRow(string ExerciseId, int Slot, string Source, string Sha256, string DataBase64);

    /// <summary>Validates one JSON line of the seed: size, magic bytes and checksum must all match.</summary>
    public static bool TryParseSeedLine(string line, out SeedRow? row, out byte[] data)
    {
        row = null;
        data = [];
        try
        {
            var parsed = JsonSerializer.Deserialize<SeedRow>(line);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ExerciseId) || parsed.ExerciseId.Length > 100 ||
                parsed.Slot is < 0 or > MaximumSlot || parsed.Source is not ("repdb-free" or "custom"))
                return false;
            var bytes = Convert.FromBase64String(parsed.DataBase64);
            if (bytes.Length == 0 || bytes.Length > MaximumImageBytes) return false;
            var isRaster = DetectImageContentType(bytes) is not null;
            var isSafeSvg = IsSafeCuratedSvg(bytes);
            if (!isRaster && !isSafeSvg) return false;
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(actualHash, parsed.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
            row = parsed;
            data = bytes;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException) { return false; }
    }

    /// <summary>
    /// Imports a deployment seed (gzip or plain JSONL). Curated custom rows are upserted only over
    /// previous seed-managed custom rows; RepDB cache rows and professional uploads are immutable here.
    /// </summary>
    public async Task<(int Imported, int Skipped)> ImportSeedAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return (0, 0);
        await EnsureSchemaAsync(cancellationToken);
        int imported = 0, skipped = 0;
        await using var file = File.OpenRead(path);
        Stream input = file;
        if (file.Length >= 2)
        {
            var header = new byte[2];
            _ = await file.ReadAsync(header, cancellationToken);
            file.Position = 0;
            if (header[0] == 0x1f && header[1] == 0x8b)
                input = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        }
        using var reader = new StreamReader(input);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!TryParseSeedLine(line, out var row, out var bytes) || row is null || row.Source != "custom") { skipped++; continue; }
            var type = DetectImageContentType(bytes) ?? "image/svg+xml";
            var catalogApproved = false;
            await WithConnectionAsync(async connection =>
            {
                await using var check = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM exercise_catalog WHERE exercise_id = @id AND review_status = 'approved')", connection);
                check.Parameters.AddWithValue("id", row.ExerciseId);
                catalogApproved = (bool)(await check.ExecuteScalarAsync(cancellationToken) ?? false);
            });
            if (!catalogApproved) { skipped++; continue; }
            var changed = false;
            await WithConnectionAsync(async connection =>
            {
                await using var command = new NpgsqlCommand("""
                    INSERT INTO exercise_images(exercise_id, slot, content_type, data, byte_size, sha256, source, uploaded_by_user_id)
                    VALUES (@id, @slot, @type, @data, @size, @hash, 'custom', NULL)
                    ON CONFLICT (exercise_id, slot) DO UPDATE SET
                        content_type = EXCLUDED.content_type, data = EXCLUDED.data, byte_size = EXCLUDED.byte_size,
                        sha256 = EXCLUDED.sha256, source = 'custom', uploaded_by_user_id = NULL, created_at = NOW()
                    WHERE exercise_images.source = 'custom'
                      AND exercise_images.uploaded_by_user_id IS NULL
                      AND exercise_images.sha256 <> EXCLUDED.sha256
                    """, connection);
                command.Parameters.AddWithValue("id", row.ExerciseId);
                command.Parameters.AddWithValue("slot", (short)row.Slot);
                command.Parameters.AddWithValue("type", type);
                command.Parameters.AddWithValue("data", bytes);
                command.Parameters.AddWithValue("size", bytes.Length);
                command.Parameters.AddWithValue("hash", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
                changed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
            });
            if (changed) imported++;
        }
        return (imported, skipped);
    }

    /// <summary>Writes every stored picture as a gzip JSON-lines seed (for superadmins preparing a deployment).</summary>
    public async Task<int> ExportSeedAsync(Stream output, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var count = 0;
        await using var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true);
        await using var writer = new StreamWriter(gzip, new System.Text.UTF8Encoding(false));
        await WithConnectionAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT exercise_id, slot, source, sha256, data FROM exercise_images ORDER BY exercise_id, slot", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new SeedRow(reader.GetString(0), reader.GetInt16(1), reader.GetString(2), reader.GetString(3).Trim(),
                    Convert.ToBase64String((byte[])reader[4]));
                await writer.WriteLineAsync(JsonSerializer.Serialize(row));
                count++;
            }
        });
        await writer.FlushAsync(cancellationToken);
        return count;
    }

    /// <summary>
    /// Copies, one at a time and politely, every RepDB picture the catalog points to and we do not have
    /// yet. Runs in the background after start-up; aborts after 10 consecutive failures (host down).
    /// </summary>
    public async Task<(int Copied, int Failed)> PrefetchMissingAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var pending = new List<(string Id, int Slot)>();
        await WithConnectionAsync(async connection =>
        {
            var stored = new HashSet<(string, int)>();
            await using (var storedCommand = new NpgsqlCommand("SELECT exercise_id, slot FROM exercise_images", connection))
            await using (var storedReader = await storedCommand.ExecuteReaderAsync(cancellationToken))
                while (await storedReader.ReadAsync(cancellationToken)) stored.Add((storedReader.GetString(0), storedReader.GetInt16(1)));
            await using var catalog = new NpgsqlCommand(
                "SELECT exercise_id, images::text FROM exercise_catalog WHERE source = 'repdb-free' ORDER BY exercise_id", connection);
            await using var reader = await catalog.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                foreach (var slot in ResolveRepDbSlotPaths(reader.GetString(1)).Keys)
                    if (!stored.Contains((reader.GetString(0), slot))) pending.Add((reader.GetString(0), slot));
        });

        int copied = 0, failed = 0, consecutiveFailures = 0;
        foreach (var (id, slot) in pending)
        {
            if (cancellationToken.IsCancellationRequested || consecutiveFailures >= 10) break;
            var image = await GetImageAsync(id, slot, cancellationToken);
            if (image is null) { failed++; consecutiveFailures++; }
            else { copied++; consecutiveFailures = 0; }
            await Task.Delay(100, CancellationToken.None);
        }
        return (copied, failed);
    }

    /// <summary>Approved catalog exercises that have no picture at all (stored or derivable). For the superadmin to complete.</summary>
    public async Task<IReadOnlyList<(string ExerciseId, string Name, string Source)>> GetExercisesWithoutImagesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var result = new List<(string, string, string)>();
        await WithConnectionAsync(async connection =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT c.exercise_id, COALESCE(c.name_es, c.name_en), c.source,
                       CASE WHEN c.source = 'repdb-free' THEN c.images::text END
                FROM exercise_catalog c
                WHERE c.review_status = 'approved'
                  AND NOT EXISTS (SELECT 1 FROM exercise_images i WHERE i.exercise_id = c.exercise_id)
                ORDER BY c.exercise_id
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                if (ResolveRepDbSlotPaths(reader.IsDBNull(3) ? null : reader.GetString(3)).Count == 0)
                    result.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        });
        return result;
    }

    /// <summary>A superadmin can manage any catalog exercise; others only the contributions they own.</summary>
    public async Task<bool> CanManageAsync(string exerciseId, int userId, bool isSuperAdmin, CancellationToken cancellationToken = default)
    {
        var allowed = false;
        await WithConnectionAsync(async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT owner_user_id FROM exercise_catalog WHERE exercise_id = @id", connection);
            command.Parameters.AddWithValue("id", exerciseId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return;
            allowed = isSuperAdmin || (!reader.IsDBNull(0) && reader.GetInt32(0) == userId);
        });
        return allowed;
    }

    public async Task<bool> DeleteImageAsync(string exerciseId, int slot, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var deleted = 0;
        await WithConnectionAsync(async connection =>
        {
            await using var command = new NpgsqlCommand("DELETE FROM exercise_images WHERE exercise_id = @id AND slot = @slot", connection);
            command.Parameters.AddWithValue("id", exerciseId);
            command.Parameters.AddWithValue("slot", (short)slot);
            deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        });
        return deleted > 0;
    }

    private async Task StoreAsync(
        string exerciseId, int slot, byte[] data, string contentType, string source, int? userId, bool overwrite,
        CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var sql = overwrite
            ? """
              INSERT INTO exercise_images(exercise_id, slot, content_type, data, byte_size, sha256, source, uploaded_by_user_id)
              VALUES (@id, @slot, @type, @data, @size, @hash, @source, @user)
              ON CONFLICT (exercise_id, slot) DO UPDATE SET content_type = EXCLUDED.content_type, data = EXCLUDED.data,
                  byte_size = EXCLUDED.byte_size, sha256 = EXCLUDED.sha256, source = EXCLUDED.source,
                  uploaded_by_user_id = EXCLUDED.uploaded_by_user_id, created_at = NOW()
              """
            : """
              INSERT INTO exercise_images(exercise_id, slot, content_type, data, byte_size, sha256, source, uploaded_by_user_id)
              VALUES (@id, @slot, @type, @data, @size, @hash, @source, @user)
              ON CONFLICT (exercise_id, slot) DO NOTHING
              """;
        await WithConnectionAsync(async connection =>
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", exerciseId);
            command.Parameters.AddWithValue("slot", (short)slot);
            command.Parameters.AddWithValue("type", contentType);
            command.Parameters.AddWithValue("data", data);
            command.Parameters.AddWithValue("size", data.Length);
            command.Parameters.AddWithValue("hash", hash);
            command.Parameters.AddWithValue("source", source);
            command.Parameters.AddWithValue("user", (object?)userId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        });
    }

    private static async Task<ExerciseImage?> ReadStoredAsync(
        NpgsqlConnection connection, string exerciseId, int slot, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT data, content_type, source FROM exercise_images WHERE exercise_id = @id AND slot = @slot", connection);
        command.Parameters.AddWithValue("id", exerciseId);
        command.Parameters.AddWithValue("slot", (short)slot);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ExerciseImage((byte[])reader[0], reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static async Task<string?> ReadCatalogImagesJsonAsync(
        NpgsqlConnection connection, string exerciseId, CancellationToken cancellationToken)
    {
        // Only RepDB rows carry source image paths; custom contributions have their own uploaded rows.
        await using var command = new NpgsqlCommand(
            "SELECT images::text FROM exercise_catalog WHERE exercise_id = @id AND source = 'repdb-free'", connection);
        command.Parameters.AddWithValue("id", exerciseId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    private async Task WithConnectionAsync(Func<NpgsqlConnection, Task> action)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync();
        try { await action(connection); }
        finally { if (shouldClose) await connection.CloseAsync(); }
    }
}

public sealed record ExerciseImage(byte[] Data, string ContentType, string Source = "custom");
