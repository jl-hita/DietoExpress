using System.Data;
using System.Text.Json;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anguloso.Server.Logica;

/// <summary>
/// Imports the RepDB free-tier catalog into PostgreSQL for in-app use.
/// The complete source record is retained as JSONB; the catalog is not exposed as a bulk-download API.
/// </summary>
public sealed class ExerciseCatalogService
{
    public const string SourceUrl = "https://exercise-dataset.com/exercises.json";
    private const string SourceName = "repdb-free";
    private readonly angulosodbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExerciseCatalogService> _logger;

    public ExerciseCatalogService(angulosodbContext db, IHttpClientFactory httpClientFactory, ILogger<ExerciseCatalogService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS exercise_catalog (
                exercise_id VARCHAR(100) PRIMARY KEY,
                source VARCHAR(40) NOT NULL,
                source_schema_version VARCHAR(40),
                source_payload JSONB NOT NULL,
                name_en VARCHAR(200) NOT NULL,
                name_de VARCHAR(200),
                name_es VARCHAR(200),
                description_en TEXT,
                description_de TEXT,
                description_es TEXT,
                instructions_en JSONB NOT NULL DEFAULT '[]'::jsonb,
                instructions_de JSONB NOT NULL DEFAULT '[]'::jsonb,
                instructions_es JSONB NOT NULL DEFAULT '[]'::jsonb,
                tips_en JSONB NOT NULL DEFAULT '[]'::jsonb,
                tips_de JSONB NOT NULL DEFAULT '[]'::jsonb,
                tips_es JSONB NOT NULL DEFAULT '[]'::jsonb,
                category VARCHAR(80),
                force_type VARCHAR(80),
                mechanic VARCHAR(80),
                difficulty VARCHAR(80),
                equipment VARCHAR(120),
                body_part VARCHAR(100),
                primary_muscles JSONB NOT NULL DEFAULT '[]'::jsonb,
                secondary_muscles JSONB NOT NULL DEFAULT '[]'::jsonb,
                goals JSONB NOT NULL DEFAULT '[]'::jsonb,
                tags JSONB NOT NULL DEFAULT '[]'::jsonb,
                met DOUBLE PRECISION,
                is_unilateral BOOLEAN,
                is_bodyweight BOOLEAN,
                images JSONB NOT NULL DEFAULT '{}'::jsonb,
                reviewed BOOLEAN NOT NULL DEFAULT FALSE,
                reviewed_movement_pattern VARCHAR(80),
                reviewed_primary_muscles JSONB,
                reviewed_secondary_muscles JSONB,
                reviewed_minimum_experience VARCHAR(40),
                reviewed_suitable_goals JSONB,
                reviewed_required_equipment JSONB,
                reviewed_at TIMESTAMPTZ,
                reviewed_by VARCHAR(120),
                source_updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_exercise_catalog_category ON exercise_catalog(category);
            CREATE INDEX IF NOT EXISTS idx_exercise_catalog_body_part ON exercise_catalog(body_part);
            CREATE INDEX IF NOT EXISTS idx_exercise_catalog_reviewed ON exercise_catalog(reviewed, reviewed_movement_pattern);
            INSERT INTO schema_migrations(id) VALUES ('exercise-catalog-repdb-v1') ON CONFLICT (id) DO NOTHING;
            """;
        await _db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    public async Task<ExerciseCatalogSyncResult?> EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var count = await _db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS \\"Value\\" FROM exercise_catalog").SingleAsync(cancellationToken);
        return count == 0 ? await SyncFromSourceAsync(cancellationToken) : null;
    }

    public async Task<ExerciseCatalogSyncResult> SyncFromSourceAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        using var response = await _httpClientFactory.CreateClient(nameof(ExerciseCatalogService))
            .GetAsync(SourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("exercises", out var exercises) || exercises.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("RepDB payload has no exercises array.");

        var schemaVersion = ReadString(root, "schema_version") ?? "unknown";
        var imported = 0;
        await using var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var exercise in exercises.EnumerateArray())
            {
                var id = ReadString(exercise, "id");
                var name = ReadString(exercise, "name_en");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("RepDB exercise is missing its stable id or English name.");

                const string upsert = """
                    INSERT INTO exercise_catalog (
                        exercise_id, source, source_schema_version, source_payload,
                        name_en, name_de, name_es, description_en, description_de, description_es,
                        instructions_en, instructions_de, instructions_es, tips_en, tips_de, tips_es,
                        category, force_type, mechanic, difficulty, equipment, body_part,
                        primary_muscles, secondary_muscles, goals, tags, met, is_unilateral, is_bodyweight, images,
                        source_updated_at, updated_at)
                    VALUES (
                        @id, @source, @schema_version, @payload::jsonb,
                        @name_en, @name_de, @name_es, @description_en, @description_de, @description_es,
                        @instructions_en::jsonb, @instructions_de::jsonb, @instructions_es::jsonb,
                        @tips_en::jsonb, @tips_de::jsonb, @tips_es::jsonb,
                        @category, @force_type, @mechanic, @difficulty, @equipment, @body_part,
                        @primary_muscles::jsonb, @secondary_muscles::jsonb, @goals::jsonb, @tags::jsonb,
                        @met, @is_unilateral, @is_bodyweight, @images::jsonb, NOW(), NOW())
                    ON CONFLICT (exercise_id) DO UPDATE SET
                        source = EXCLUDED.source,
                        source_schema_version = EXCLUDED.source_schema_version,
                        source_payload = EXCLUDED.source_payload,
                        name_en = EXCLUDED.name_en, name_de = EXCLUDED.name_de, name_es = EXCLUDED.name_es,
                        description_en = EXCLUDED.description_en, description_de = EXCLUDED.description_de,
                        description_es = EXCLUDED.description_es,
                        instructions_en = EXCLUDED.instructions_en, instructions_de = EXCLUDED.instructions_de,
                        instructions_es = EXCLUDED.instructions_es, tips_en = EXCLUDED.tips_en,
                        tips_de = EXCLUDED.tips_de, tips_es = EXCLUDED.tips_es,
                        category = EXCLUDED.category, force_type = EXCLUDED.force_type, mechanic = EXCLUDED.mechanic,
                        difficulty = EXCLUDED.difficulty, equipment = EXCLUDED.equipment, body_part = EXCLUDED.body_part,
                        primary_muscles = EXCLUDED.primary_muscles, secondary_muscles = EXCLUDED.secondary_muscles,
                        goals = EXCLUDED.goals, tags = EXCLUDED.tags, met = EXCLUDED.met,
                        is_unilateral = EXCLUDED.is_unilateral, is_bodyweight = EXCLUDED.is_bodyweight,
                        images = EXCLUDED.images, source_updated_at = NOW(), updated_at = NOW()
                    """;

                await using var command = new NpgsqlCommand(upsert, connection, transaction);
                Add(command, "id", id);
                Add(command, "source", SourceName);
                Add(command, "schema_version", schemaVersion);
                Add(command, "payload", exercise.GetRawText());
                Add(command, "name_en", name);
                Add(command, "name_de", ReadString(exercise, "name_de"));
                Add(command, "name_es", ReadString(exercise, "name_es"));
                Add(command, "description_en", ReadString(exercise, "description_en"));
                Add(command, "description_de", ReadString(exercise, "description_de"));
                Add(command, "description_es", ReadString(exercise, "description_es"));
                AddJson(command, "instructions_en", exercise, "instructions_en");
                AddJson(command, "instructions_de", exercise, "instructions_de");
                AddJson(command, "instructions_es", exercise, "instructions_es");
                AddJson(command, "tips_en", exercise, "tips_en");
                AddJson(command, "tips_de", exercise, "tips_de");
                AddJson(command, "tips_es", exercise, "tips_es");
                Add(command, "category", ReadString(exercise, "category"));
                Add(command, "force_type", ReadString(exercise, "force_type"));
                Add(command, "mechanic", ReadString(exercise, "mechanic"));
                Add(command, "difficulty", ReadString(exercise, "difficulty"));
                Add(command, "equipment", ReadString(exercise, "equipment"));
                Add(command, "body_part", ReadString(exercise, "body_part"));
                AddJson(command, "primary_muscles", exercise, "primary_muscles");
                AddJson(command, "secondary_muscles", exercise, "secondary_muscles");
                AddJson(command, "goals", exercise, "goals");
                AddJson(command, "tags", exercise, "tags");
                Add(command, "met", ReadDouble(exercise, "met"));
                Add(command, "is_unilateral", ReadBool(exercise, "is_unilateral"));
                Add(command, "is_bodyweight", ReadBool(exercise, "is_bodyweight"));
                AddJson(command, "images", exercise, "images");
                await command.ExecuteNonQueryAsync(cancellationToken);
                imported++;
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation("Imported {Count} exercises from {Source}; reviewed annotations were preserved.", imported, SourceName);
        return new ExerciseCatalogSyncResult(SourceName, schemaVersion, imported, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<ExerciseCatalogItem>> SearchAsync(string? query, string? bodyPart, string? equipment, int limit, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        limit = Math.Clamp(limit, 1, 100);
        const string sql = """
            SELECT exercise_id, name_es, name_en, category, body_part, equipment,
                   primary_muscles, secondary_muscles, goals, tags, instructions_es, images
            FROM exercise_catalog
            WHERE (@body_part IS NULL OR body_part = @body_part)
              AND (@equipment IS NULL OR equipment = @equipment)
              AND (@query IS NULL OR name_es ILIKE '%' || @query || '%' OR name_en ILIKE '%' || @query || '%'
                   OR source_payload::text ILIKE '%' || @query || '%')
            ORDER BY name_es NULLS LAST, name_en
            LIMIT @limit
            """;
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            Add(command, "body_part", string.IsNullOrWhiteSpace(bodyPart) ? null : bodyPart);
            Add(command, "equipment", string.IsNullOrWhiteSpace(equipment) ? null : equipment);
            Add(command, "query", string.IsNullOrWhiteSpace(query) ? null : query.Trim());
            Add(command, "limit", limit);
            var items = new List<ExerciseCatalogItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ExerciseCatalogItem(
                    reader.GetString(0), NullableString(reader, 1), reader.GetString(2), NullableString(reader, 3),
                    NullableString(reader, 4), NullableString(reader, 5), JsonDocument.Parse(reader.GetString(6)).RootElement.Clone(),
                    JsonDocument.Parse(reader.GetString(7)).RootElement.Clone(), JsonDocument.Parse(reader.GetString(8)).RootElement.Clone(),
                    JsonDocument.Parse(reader.GetString(9)).RootElement.Clone(), JsonDocument.Parse(reader.GetString(10)).RootElement.Clone(),
                    JsonDocument.Parse(reader.GetString(11)).RootElement.Clone()));
            }
            return items;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static void Add(NpgsqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static void AddJson(NpgsqlCommand command, string name, JsonElement parent, string property) =>
        command.Parameters.AddWithValue(name, parent.TryGetProperty(property, out var value) ? value.GetRawText() : "[]");

    private static string? ReadString(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? ReadDouble(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static bool? ReadBool(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

public sealed record ExerciseCatalogSyncResult(string Source, string SchemaVersion, int ImportedCount, DateTimeOffset SyncedAtUtc);
public sealed record ExerciseCatalogItem(
    string Id, string? NameEs, string NameEn, string? Category, string? BodyPart, string? Equipment,
    JsonElement PrimaryMuscles, JsonElement SecondaryMuscles, JsonElement Goals, JsonElement Tags,
    JsonElement InstructionsEs, JsonElement Images);
