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
                author VARCHAR(120) NOT NULL DEFAULT 'superadmin',
                owner_user_id INTEGER,
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
                review_status VARCHAR(20) NOT NULL DEFAULT 'pending',
                review_notes TEXT,
                reviewed_movement_pattern VARCHAR(80),
                reviewed_primary_muscles JSONB,
                reviewed_secondary_muscles JSONB,
                reviewed_minimum_experience VARCHAR(40),
                reviewed_suitable_goals JSONB,
                reviewed_required_equipment JSONB,
                reviewed_selection_priority INTEGER NOT NULL DEFAULT 50,
                reviewed_at TIMESTAMPTZ,
                reviewed_by VARCHAR(120),
                source_updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS author VARCHAR(120) NOT NULL DEFAULT 'superadmin';
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS owner_user_id INTEGER;
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS owner_movement_pattern VARCHAR(80);
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS owner_primary_muscles JSONB;
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS owner_secondary_muscles JSONB;
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS owner_minimum_experience VARCHAR(40);
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS owner_suitable_goals JSONB;
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS owner_required_equipment JSONB;
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS owner_selection_priority INTEGER NOT NULL DEFAULT 50;
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS reviewed_selection_priority INTEGER NOT NULL DEFAULT 50;
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS review_status VARCHAR(20) NOT NULL DEFAULT 'pending';
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS review_notes TEXT;
            UPDATE exercise_catalog SET reviewed = FALSE, reviewed_movement_pattern = NULL, reviewed_primary_muscles = NULL, reviewed_secondary_muscles = NULL, reviewed_minimum_experience = NULL, reviewed_suitable_goals = NULL, reviewed_required_equipment = NULL, reviewed_at = NULL, reviewed_by = NULL, review_status = 'pending' WHERE reviewed_by = 'DietoExpress curated metadata v1';
            CREATE INDEX IF NOT EXISTS idx_exercise_catalog_category ON exercise_catalog(category);
            CREATE INDEX IF NOT EXISTS idx_exercise_catalog_body_part ON exercise_catalog(body_part);
            CREATE INDEX IF NOT EXISTS idx_exercise_catalog_reviewed ON exercise_catalog(reviewed, reviewed_movement_pattern);
            INSERT INTO schema_migrations(id) VALUES ('exercise-catalog-repdb-v1') ON CONFLICT (id) DO NOTHING;
            """;
        // Execute DDL through the provider connection directly. EF Core's ExecuteSqlRaw
        // treats braces in SQL as composite-format placeholders; JSONB defaults such as '{}'
        // therefore throw FormatException before PostgreSQL sees the statement.
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    public async Task<ExerciseCatalogSyncResult?> EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var count = await _db.Database.SqlQueryRaw<long>("""SELECT COUNT(*) AS "Value" FROM exercise_catalog""").SingleAsync(cancellationToken);
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
        var reviewedCount = 0;
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
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
            await ResetLegacyAutoReviewsAsync(connection, transaction, cancellationToken);
            await using var reviewedCountCommand = new NpgsqlCommand("SELECT COUNT(*) FROM exercise_catalog WHERE reviewed = TRUE", connection, transaction);
            reviewedCount = Convert.ToInt32(await reviewedCountCommand.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation("Imported {Count} exercises from {Source}; {ReviewedCount} exercises have curated review metadata.", imported, SourceName, reviewedCount);
        return new ExerciseCatalogSyncResult(SourceName, schemaVersion, imported, reviewedCount, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<ExerciseCatalogItem>> SearchAsync(string? query, string? bodyPart, string? equipment, int limit, CancellationToken cancellationToken = default, string? reviewStatus = null, int? ownerUserId = null, bool includeAllOwners = false)
    {
        await EnsureSchemaAsync(cancellationToken);
        limit = Math.Clamp(limit, 1, 100);
        const string sql = """
            SELECT exercise_id, name_es, name_en, category, body_part, equipment,
                   primary_muscles, secondary_muscles, goals, tags, instructions_es, images, source, review_status,
                   reviewed, reviewed_movement_pattern, reviewed_primary_muscles, reviewed_secondary_muscles,
                   reviewed_minimum_experience, reviewed_suitable_goals, reviewed_required_equipment, reviewed_selection_priority, review_notes, reviewed_at, reviewed_by, source_payload ->> 'contributor' AS contributor
            FROM exercise_catalog
            WHERE (CAST(@body_part AS varchar) IS NULL OR body_part = @body_part)
              AND (CAST(@equipment AS varchar) IS NULL OR equipment = @equipment)
              AND (CAST(@review_status AS varchar) IS NULL OR review_status = @review_status)
              AND (@include_all_owners OR author = 'superadmin' OR review_status = 'approved'
                   OR (CAST(@owner_user_id AS integer) IS NOT NULL AND owner_user_id = @owner_user_id AND review_status = 'pending'))
              AND (CAST(@query AS text) IS NULL OR name_es ILIKE '%' || @query || '%' OR name_en ILIKE '%' || @query || '%'
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
            Add(command, "review_status", string.IsNullOrWhiteSpace(reviewStatus) ? null : reviewStatus);
            Add(command, "owner_user_id", ownerUserId);
            Add(command, "include_all_owners", includeAllOwners);
            Add(command, "limit", limit);
            var items = new List<ExerciseCatalogItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ExerciseCatalogItem(
                    reader.GetString(0), NullableString(reader, 1), reader.GetString(2), NullableString(reader, 3),
                    NullableString(reader, 4), NullableString(reader, 5), ParseElement(reader.GetString(6)),
                    ParseElement(reader.GetString(7)), ParseElement(reader.GetString(8)),
                    ParseElement(reader.GetString(9)), ParseElement(reader.GetString(10)),
                    ParseElement(reader.GetString(11)), reader.GetString(12), reader.GetString(13), reader.GetBoolean(14),
                    NullableString(reader, 15), NullableJson(reader, 16), NullableJson(reader, 17), NullableString(reader, 18),
                    NullableJson(reader, 19), NullableJson(reader, 20), reader.GetInt32(21), NullableString(reader, 22),
                    reader.IsDBNull(23) ? null : reader.GetDateTime(23), NullableString(reader, 24), NullableString(reader, 25)));
            }
            return items;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }




    public async Task<bool> SaveReviewAsync(string id, ExerciseReviewRequest review, string reviewer, CancellationToken cancellationToken = default)
    {
        if (review.Status is not ("approved" or "rejected" or "pending")) throw new ArgumentException("Estado de revisión no válido.");
        if (review.Status == "approved" && (string.IsNullOrWhiteSpace(review.MovementPattern) || review.PrimaryMuscles.Length == 0 || string.IsNullOrWhiteSpace(review.MinimumExperience) || review.SuitableGoals.Length == 0))
            throw new ArgumentException("Para aprobar, indica patrón, músculos principales, nivel y al menos un objetivo.");
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            const string sql = """
                UPDATE exercise_catalog SET review_status = @status, reviewed = (@status = 'approved'),
                    reviewed_movement_pattern = @pattern, reviewed_primary_muscles = @primary::jsonb,
                    reviewed_secondary_muscles = @secondary::jsonb, reviewed_minimum_experience = @experience,
                    reviewed_suitable_goals = @goals::jsonb, reviewed_required_equipment = @equipment::jsonb,
                    reviewed_selection_priority = @priority, review_notes = @notes, reviewed_at = NOW(),
                    reviewed_by = @reviewer, updated_at = NOW() WHERE exercise_id = @id
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            Add(command, "id", id); Add(command, "status", review.Status); Add(command, "pattern", review.MovementPattern);
            Add(command, "primary", JsonSerializer.Serialize(review.PrimaryMuscles ?? []));
            Add(command, "secondary", JsonSerializer.Serialize(review.SecondaryMuscles ?? []));
            Add(command, "experience", review.MinimumExperience);
            Add(command, "goals", JsonSerializer.Serialize(review.SuitableGoals ?? []));
            Add(command, "equipment", JsonSerializer.Serialize(review.RequiredEquipment ?? []));
            Add(command, "priority", Math.Clamp(review.SelectionPriority, 0, 100));
            Add(command, "notes", review.Notes); Add(command, "reviewer", reviewer);
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        finally { if (shouldClose) await connection.CloseAsync(); }
    }

    public async Task<string> AddProfessionalContributionAsync(string name, string? description, string? equipment, string? bodyPart, string contributor, int contributorUserId, ExerciseContributionRequest contribution, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) throw new ArgumentException("El nombre es obligatorio y no puede superar 200 caracteres.");
        await EnsureSchemaAsync(cancellationToken);
        var id = "professional-" + Guid.NewGuid().ToString("N");
        ValidateContributionMetadata(contribution);
        var payload = JsonSerializer.Serialize(new { name = name.Trim(), description, equipment, bodyPart, contributor, movementPattern = contribution.MovementPattern, primaryMuscles = contribution.PrimaryMuscles, secondaryMuscles = contribution.SecondaryMuscles, minimumExperience = contribution.MinimumExperience, suitableGoals = contribution.SuitableGoals, requiredEquipment = contribution.RequiredEquipment, selectionPriority = contribution.SelectionPriority });
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            const string sql = """
                INSERT INTO exercise_catalog (exercise_id, source, author, owner_user_id, owner_movement_pattern, owner_primary_muscles, owner_secondary_muscles, owner_minimum_experience, owner_suitable_goals, owner_required_equipment, owner_selection_priority, source_payload, name_en, description_en, equipment, body_part, review_status)
                VALUES (@id, 'professional-contribution', @author, @owner_user_id, @owner_pattern, @owner_primary::jsonb, @owner_secondary::jsonb, @owner_experience, @owner_goals::jsonb, @owner_equipment::jsonb, @owner_priority, @payload::jsonb, @name, @description, @equipment, @body_part, 'pending')
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            Add(command, "id", id); Add(command, "author", contributor); Add(command, "owner_user_id", contributorUserId);
            Add(command, "owner_pattern", contribution.MovementPattern); Add(command, "owner_primary", JsonSerializer.Serialize(contribution.PrimaryMuscles ?? []));
            Add(command, "owner_secondary", JsonSerializer.Serialize(contribution.SecondaryMuscles ?? [])); Add(command, "owner_experience", contribution.MinimumExperience);
            Add(command, "owner_goals", JsonSerializer.Serialize(contribution.SuitableGoals ?? [])); Add(command, "owner_equipment", JsonSerializer.Serialize(contribution.RequiredEquipment ?? []));
            Add(command, "owner_priority", Math.Clamp(contribution.SelectionPriority, 0, 100));
            Add(command, "payload", payload); Add(command, "name", name.Trim());
            Add(command, "description", description); Add(command, "equipment", equipment); Add(command, "body_part", bodyPart);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        finally { if (shouldClose) await connection.CloseAsync(); }
    }

    public async Task<IReadOnlyList<TrainingExerciseCandidate>> LoadGeneratorCandidatesAsync(int ownerUserId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            SELECT exercise_id, COALESCE(name_es, name_en), review_status,
                   CASE WHEN review_status = 'approved' THEN reviewed_movement_pattern ELSE owner_movement_pattern END,
                   CASE WHEN review_status = 'approved' THEN COALESCE(reviewed_primary_muscles, '[]'::jsonb) ELSE COALESCE(owner_primary_muscles, '[]'::jsonb) END::text,
                   CASE WHEN review_status = 'approved' THEN COALESCE(reviewed_secondary_muscles, '[]'::jsonb) ELSE COALESCE(owner_secondary_muscles, '[]'::jsonb) END::text,
                   CASE WHEN review_status = 'approved' THEN reviewed_minimum_experience ELSE owner_minimum_experience END,
                   CASE WHEN review_status = 'approved' THEN COALESCE(reviewed_suitable_goals, '[]'::jsonb) ELSE COALESCE(owner_suitable_goals, '[]'::jsonb) END::text,
                   CASE WHEN review_status = 'approved' THEN COALESCE(reviewed_required_equipment, '[]'::jsonb) ELSE COALESCE(owner_required_equipment, '[]'::jsonb) END::text,
                   CASE WHEN review_status = 'approved' THEN reviewed_selection_priority ELSE COALESCE(owner_selection_priority, 50) END,
                   source, owner_user_id
            FROM exercise_catalog
            WHERE review_status = 'approved'
               OR (review_status = 'pending' AND owner_user_id = @owner_user_id)
            ORDER BY exercise_id
            """;
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            Add(command, "owner_user_id", ownerUserId);
            var result = new List<TrainingExerciseCandidate>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var pattern = NullableString(reader, 3);
                var experience = NullableString(reader, 6);
                if (!Enum.TryParse<TrainingMovementPattern>(pattern, true, out var parsedPattern) ||
                    !Enum.TryParse<TrainingExperience>(experience, true, out var parsedExperience))
                    continue;
                var primary = ParseEnumArray<TrainingMuscleGroup>(reader.GetString(4));
                var secondary = ParseEnumArray<TrainingMuscleGroup>(reader.GetString(5));
                var goals = ParseEnumArray<TrainingGoal>(reader.GetString(7));
                var equipment = ParseStringArray(reader.GetString(8));
                if (primary.Length == 0 || goals.Length == 0) continue;
                // Only approved records use superadmin metadata; pending records use contributor-supplied
                // metadata and are included only for their own authenticated owner.
                result.Add(new TrainingExerciseCandidate(
                    reader.GetString(0), "exercise-catalog-db-v1", reader.GetString(1), parsedPattern,
                    equipment, goals, parsedExperience, reader.GetInt32(9),
                    reader.GetString(2) == "approved", primary, secondary,
                    reader.GetString(2) == "pending" && !reader.IsDBNull(11) && reader.GetInt32(11) == ownerUserId));
            }
            return result;
        }
        finally { if (shouldClose) await connection.CloseAsync(); }
    }

    private static void ValidateContributionMetadata(ExerciseContributionRequest contribution)
    {
        if (string.IsNullOrWhiteSpace(contribution.MovementPattern) ||
            !Enum.TryParse<TrainingMovementPattern>(contribution.MovementPattern, true, out _))
            throw new ArgumentException("Indica un patrón de movimiento válido para poder usar el ejercicio en el generador.");
        if (contribution.PrimaryMuscles is null || contribution.PrimaryMuscles.Length == 0 ||
            contribution.PrimaryMuscles.Any(x => !Enum.TryParse<TrainingMuscleGroup>(x, true, out _)))
            throw new ArgumentException("Indica al menos un grupo muscular principal válido.");
        if (contribution.SecondaryMuscles is null ||
            contribution.SecondaryMuscles.Any(x => !Enum.TryParse<TrainingMuscleGroup>(x, true, out _)))
            throw new ArgumentException("Los grupos musculares secundarios deben ser válidos, aunque estén vacíos.");
        if (string.IsNullOrWhiteSpace(contribution.MinimumExperience) ||
            !Enum.TryParse<TrainingExperience>(contribution.MinimumExperience, true, out _))
            throw new ArgumentException("Indica un nivel de experiencia mínimo válido.");
        if (contribution.SuitableGoals is null || contribution.SuitableGoals.Length == 0 ||
            contribution.SuitableGoals.Any(x => !Enum.TryParse<TrainingGoal>(x, true, out _)))
            throw new ArgumentException("Indica al menos un objetivo compatible válido.");
        if (contribution.RequiredEquipment is null ||
            contribution.RequiredEquipment.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("El material requerido debe ser una lista válida, aunque esté vacía.");
    }

    private static T[] ParseEnumArray<T>(string json) where T : struct, Enum
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Select(value => Enum.TryParse<T>(value, true, out var parsed) ? (T?)parsed : null)
                .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static string[] ParseStringArray(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static async Task ResetLegacyAutoReviewsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        // These legacy flags were assigned from hard-coded metadata, not from human review.
        const string sql = """
            UPDATE exercise_catalog SET reviewed = FALSE, review_status = 'pending',
                reviewed_movement_pattern = NULL, reviewed_primary_muscles = NULL, reviewed_secondary_muscles = NULL,
                reviewed_minimum_experience = NULL, reviewed_suitable_goals = NULL, reviewed_required_equipment = NULL,
                reviewed_selection_priority = 50, reviewed_at = NULL, reviewed_by = NULL, review_notes = NULL, updated_at = NOW()
            WHERE reviewed_by = 'DietoExpress curated metadata v1'
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Add(NpgsqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static void AddJson(NpgsqlCommand command, string name, JsonElement parent, string property) =>
        command.Parameters.AddWithValue(name, parent.TryGetProperty(property, out var value) ? value.GetRawText() : property == "images" ? "{}" : "[]");

    private static string? ReadString(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? ReadDouble(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static bool? ReadBool(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement? NullableJson(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : ParseElement(reader.GetString(ordinal));

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

public sealed record ExerciseCatalogSyncResult(string Source, string SchemaVersion, int ImportedCount, int ReviewedCount, DateTimeOffset SyncedAtUtc);
public sealed record ExerciseCatalogItem(
    string Id, string? NameEs, string NameEn, string? Category, string? BodyPart, string? Equipment,
    JsonElement PrimaryMuscles, JsonElement SecondaryMuscles, JsonElement Goals, JsonElement Tags,
    JsonElement InstructionsEs, JsonElement Images, string Source, string ReviewStatus, bool Reviewed,
    string? MovementPattern, JsonElement? ReviewedPrimaryMuscles, JsonElement? ReviewedSecondaryMuscles,
    string? MinimumExperience, JsonElement? SuitableGoals, JsonElement? RequiredEquipment,
    int SelectionPriority, string? ReviewNotes, DateTime? ReviewedAt, string? ReviewedBy, string? Contributor);

public sealed record ExerciseReviewRequest(string Status, string? MovementPattern, string[] PrimaryMuscles, string[] SecondaryMuscles, string MinimumExperience, string[] SuitableGoals, string[] RequiredEquipment, int SelectionPriority, string? Notes);
public sealed record ExerciseContributionRequest(string Name, string? Description, string? Equipment, string? BodyPart, string? MovementPattern, string[]? PrimaryMuscles, string[]? SecondaryMuscles, string? MinimumExperience, string[]? SuitableGoals, string[]? RequiredEquipment, int SelectionPriority = 50);
