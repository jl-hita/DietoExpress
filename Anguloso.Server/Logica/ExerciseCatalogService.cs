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
                metadata_enrichment_notes TEXT,
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
            ALTER TABLE exercise_catalog ADD COLUMN IF NOT EXISTS metadata_enrichment_notes TEXT;
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

        // The import itself preprocesses RepDB records before inserting them. Run the guarded
        // one-time pass afterwards as a migration for catalogs imported by an older version.
        var bootstrapCompleted = await _db.Database.SqlQueryRaw<bool>(
            "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = 'exercise-catalog-repdb-spanish-reset-v2') AS \"Value\"")
            .SingleAsync(cancellationToken);
        ExerciseCatalogSyncResult? result;
        if (!bootstrapCompleted)
        {
            result = await SyncFromSourceAsync(cancellationToken, replaceExisting: true, markBootstrapComplete: true);
        }
        else
        {
            var count = await _db.Database.SqlQueryRaw<long>("""SELECT COUNT(*) AS "Value" FROM exercise_catalog""").SingleAsync(cancellationToken);
            result = count == 0 ? await SyncFromSourceAsync(cancellationToken) : null;
        }

        await EnsureRepDbQualityCurationAsync(cancellationToken);
        await EnsureRepDbMetadataEnrichmentAsync(cancellationToken);
        await EnsureRepDbInitialReviewAsync(cancellationToken);
        return result;
    }

    public async Task<ExerciseCatalogSyncResult> SyncFromSourceAsync(
        CancellationToken cancellationToken = default,
        bool replaceExisting = false,
        bool markBootstrapComplete = false)
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
            if (replaceExisting)
            {
                await using var deleteCommand = new NpgsqlCommand("DELETE FROM exercise_catalog", connection, transaction);
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var exercise in exercises.EnumerateArray())
            {
                var id = ReadString(exercise, "id");
                var name = ReadString(exercise, "name_en");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("RepDB exercise is missing its stable id or English name.");

                var sourceCategory = ReadString(exercise, "category");
                var equipment = ReadString(exercise, "equipment");
                var variationGroup = ReadString(exercise, "variation_group");
                if (RepDbExerciseCuration.ShouldExclude(id, sourceCategory, equipment, variationGroup))
                    continue;

                var category = RepDbExerciseCuration.CurateCategory(id, sourceCategory);
                var bodyPart = ReadString(exercise, "body_part");
                var goalsJson = RepDbExerciseCuration.CurateGoalsJson(id, category, JsonText(exercise, "goals"));
                var primaryMusclesJson = RepDbExerciseCuration.CurateMusclesJson(id, primary: true, JsonText(exercise, "primary_muscles"));
                var secondaryMusclesJson = RepDbExerciseCuration.CurateMusclesJson(id, primary: false, JsonText(exercise, "secondary_muscles"));
                var nameEs = ReadString(exercise, "name_es");
                var descriptionEs = ReadString(exercise, "description_es");
                var difficulty = ReadString(exercise, "difficulty");
                var instructionsEsJson = JsonText(exercise, "instructions_es");
                var tagsJson = JsonText(exercise, "tags");
                var generatedDescription = string.IsNullOrWhiteSpace(descriptionEs);
                var generatedDifficulty = string.IsNullOrWhiteSpace(difficulty);
                if (generatedDescription)
                    descriptionEs = RepDbExerciseAutoReviewer.GenerateDescriptionEs(nameEs, name, equipment, bodyPart, primaryMusclesJson);
                if (generatedDifficulty)
                    difficulty = RepDbExerciseAutoReviewer.InferDifficulty(null, nameEs, name, equipment,
                        ReadString(exercise, "mechanic"), ReadString(exercise, "force_type"), bodyPart,
                        tagsJson, instructionsEsJson);
                var enrichmentNotes = generatedDescription || generatedDifficulty
                    ? string.Join("; ", new[]
                    {
                        generatedDescription && !string.IsNullOrWhiteSpace(descriptionEs) ? "Descripción en español generada automáticamente" : null,
                        generatedDifficulty && !string.IsNullOrWhiteSpace(difficulty) ? "Dificultad estimada automáticamente" : null
                    }.Where(x => x is not null))
                    : null;

                var metadata = RepDbExerciseAutoReviewer.TryBuild(
                    id, nameEs ?? "", name,
                    descriptionEs,
                    instructionsEsJson,
                    difficulty, equipment,
                    ReadString(exercise, "force_type"), ReadString(exercise, "mechanic"),
                    bodyPart,
                    primaryMusclesJson, secondaryMusclesJson,
                    goalsJson, tagsJson, ReadBool(exercise, "is_bodyweight"));

                const string upsert = """
                    INSERT INTO exercise_catalog (
                        exercise_id, source, source_schema_version, source_payload,
                        name_en, name_de, name_es, description_en, description_de, description_es, metadata_enrichment_notes,
                        instructions_en, instructions_de, instructions_es, tips_en, tips_de, tips_es,
                        category, force_type, mechanic, difficulty, equipment, body_part,
                        primary_muscles, secondary_muscles, goals, tags, met, is_unilateral, is_bodyweight, images,
                        reviewed, review_status, review_notes, reviewed_movement_pattern,
                        reviewed_primary_muscles, reviewed_secondary_muscles, reviewed_minimum_experience,
                        reviewed_suitable_goals, reviewed_required_equipment, reviewed_selection_priority,
                        reviewed_at, reviewed_by, source_updated_at, updated_at)
                    VALUES (
                        @id, @source, @schema_version, @payload::jsonb,
                        @name_en, @name_de, @name_es, @description_en, @description_de, @description_es, @metadata_enrichment_notes,
                        @instructions_en::jsonb, @instructions_de::jsonb, @instructions_es::jsonb,
                        @tips_en::jsonb, @tips_de::jsonb, @tips_es::jsonb,
                        @category, @force_type, @mechanic, @difficulty, @equipment, @body_part,
                        @primary_muscles::jsonb, @secondary_muscles::jsonb, @goals::jsonb, @tags::jsonb,
                        @met, @is_unilateral, @is_bodyweight, @images::jsonb,
                        @auto_reviewed, CASE WHEN @auto_reviewed THEN 'approved' ELSE 'pending' END,
                        @review_notes, @reviewed_pattern, @reviewed_primary::jsonb, @reviewed_secondary::jsonb,
                        @reviewed_experience, @reviewed_goals::jsonb, @reviewed_equipment::jsonb,
                        @reviewed_priority, CASE WHEN @auto_reviewed THEN NOW() ELSE NULL END,
                        CASE WHEN @auto_reviewed THEN 'RepDB automatic metadata v2' ELSE NULL END, NOW(), NOW())
                    ON CONFLICT (exercise_id) DO UPDATE SET
                        source = EXCLUDED.source,
                        source_schema_version = EXCLUDED.source_schema_version,
                        source_payload = EXCLUDED.source_payload,
                        name_en = EXCLUDED.name_en, name_de = EXCLUDED.name_de, name_es = EXCLUDED.name_es,
                        description_en = EXCLUDED.description_en, description_de = EXCLUDED.description_de,
                        description_es = EXCLUDED.description_es,
                        metadata_enrichment_notes = COALESCE(EXCLUDED.metadata_enrichment_notes, exercise_catalog.metadata_enrichment_notes),
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
                Add(command, "description_es", descriptionEs);
                Add(command, "metadata_enrichment_notes", enrichmentNotes);
                AddJson(command, "instructions_en", exercise, "instructions_en");
                AddJson(command, "instructions_de", exercise, "instructions_de");
                AddJson(command, "instructions_es", exercise, "instructions_es");
                AddJson(command, "tips_en", exercise, "tips_en");
                AddJson(command, "tips_de", exercise, "tips_de");
                AddJson(command, "tips_es", exercise, "tips_es");
                Add(command, "category", category);
                Add(command, "force_type", ReadString(exercise, "force_type"));
                Add(command, "mechanic", ReadString(exercise, "mechanic"));
                Add(command, "difficulty", difficulty);
                Add(command, "equipment", equipment);
                Add(command, "body_part", ReadString(exercise, "body_part"));
                Add(command, "primary_muscles", primaryMusclesJson);
                Add(command, "secondary_muscles", secondaryMusclesJson);
                Add(command, "goals", goalsJson);
                AddJson(command, "tags", exercise, "tags");
                Add(command, "met", ReadDouble(exercise, "met"));
                Add(command, "is_unilateral", ReadBool(exercise, "is_unilateral"));
                Add(command, "is_bodyweight", ReadBool(exercise, "is_bodyweight"));
                AddJson(command, "images", exercise, "images");
                Add(command, "auto_reviewed", metadata is not null);
                Add(command, "review_notes", metadata?.Notes);
                Add(command, "reviewed_pattern", metadata?.MovementPattern);
                Add(command, "reviewed_primary", metadata is null ? null : JsonSerializer.Serialize(metadata.PrimaryMuscles));
                Add(command, "reviewed_secondary", metadata is null ? null : JsonSerializer.Serialize(metadata.SecondaryMuscles));
                Add(command, "reviewed_experience", metadata?.MinimumExperience);
                Add(command, "reviewed_goals", metadata is null ? null : JsonSerializer.Serialize(metadata.SuitableGoals));
                Add(command, "reviewed_equipment", metadata is null ? null : JsonSerializer.Serialize(metadata.RequiredEquipment));
                Add(command, "reviewed_priority", metadata?.SelectionPriority ?? 50);
                await command.ExecuteNonQueryAsync(cancellationToken);
                imported++;
            }
            await ResetLegacyAutoReviewsAsync(connection, transaction, cancellationToken);
            if (markBootstrapComplete)
            {
                await using var markerCommand = new NpgsqlCommand(
                    "INSERT INTO schema_migrations(id) VALUES ('exercise-catalog-repdb-spanish-reset-v2') ON CONFLICT (id) DO NOTHING",
                    connection, transaction);
                await markerCommand.ExecuteNonQueryAsync(cancellationToken);
            }
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



    /// <summary>
    /// Applies explicit quality curation to catalogs that were imported before the curation rules existed.
    /// The source payload remains intact for traceability; only the normalized catalog fields are curated.
    /// </summary>
    private async Task EnsureRepDbQualityCurationAsync(CancellationToken cancellationToken)
    {
        // Versioned separately so previously imported catalogs receive the expanded curation rules.
        const string marker = "exercise-catalog-repdb-quality-curation-v2";
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var check = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @marker)", connection, transaction))
            {
                Add(check, "marker", marker);
                if (Convert.ToBoolean(await check.ExecuteScalarAsync(cancellationToken)))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
            }

            await using (var delete = new NpgsqlCommand("""
                DELETE FROM exercise_catalog
                WHERE source = 'repdb-free'
                  AND owner_user_id IS NULL
                  AND (
                    exercise_id = ANY(@excluded_ids)
                    OR source_payload->>'category' IN ('olympic', 'plyometrics')
                    OR source_payload->>'equipment' = 'bosu_ball'
                    OR EXISTS (
                        SELECT 1
                        FROM jsonb_each(@preferred_variants::jsonb) AS vg(group_name, keep_ids)
                        WHERE source_payload->>'variation_group' = vg.group_name
                          AND NOT (vg.keep_ids ? exercise_catalog.exercise_id)
                    )
                  )
                """, connection, transaction))
            {
                Add(delete, "excluded_ids", RepDbExerciseCuration.ExcludedIds);
                Add(delete, "preferred_variants", JsonSerializer.Serialize(RepDbExerciseCuration.PreferredIdsByVariationGroup));
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            // Restore missing normalized fields from the original RepDB payload only when blank.
            // This is deterministic source recovery, not generated metadata, and never overwrites non-empty edits.
            const string repairSql = """
                UPDATE exercise_catalog
                SET name_es = COALESCE(NULLIF(BTRIM(name_es), ''), NULLIF(source_payload->>'name_es', '')),
                    description_es = COALESCE(NULLIF(BTRIM(description_es), ''), NULLIF(source_payload->>'description_es', '')),
                    description_en = COALESCE(NULLIF(BTRIM(description_en), ''), NULLIF(source_payload->>'description_en', '')),
                    difficulty = COALESCE(NULLIF(BTRIM(difficulty), ''), NULLIF(source_payload->>'difficulty', '')),
                    category = COALESCE(NULLIF(BTRIM(category), ''), NULLIF(source_payload->>'category', '')),
                    force_type = COALESCE(NULLIF(BTRIM(force_type), ''), NULLIF(source_payload->>'force_type', '')),
                    mechanic = COALESCE(NULLIF(BTRIM(mechanic), ''), NULLIF(source_payload->>'mechanic', '')),
                    equipment = COALESCE(NULLIF(BTRIM(equipment), ''), NULLIF(source_payload->>'equipment', '')),
                    body_part = COALESCE(NULLIF(BTRIM(body_part), ''), NULLIF(source_payload->>'body_part', '')),
                    primary_muscles = CASE WHEN primary_muscles = '[]'::jsonb
                        THEN COALESCE(source_payload->'primary_muscles', '[]'::jsonb) ELSE primary_muscles END,
                    secondary_muscles = CASE WHEN secondary_muscles = '[]'::jsonb
                        THEN COALESCE(source_payload->'secondary_muscles', '[]'::jsonb) ELSE secondary_muscles END,
                    goals = CASE WHEN goals = '[]'::jsonb
                        THEN COALESCE(source_payload->'goals', '[]'::jsonb) ELSE goals END,
                    tags = CASE WHEN tags = '[]'::jsonb
                        THEN COALESCE(source_payload->'tags', '[]'::jsonb) ELSE tags END,
                    instructions_es = CASE
                        WHEN jsonb_typeof(instructions_es) <> 'array' OR jsonb_array_length(instructions_es) < 2
                        THEN CASE WHEN jsonb_typeof(source_payload->'instructions_es') = 'array'
                            THEN source_payload->'instructions_es' ELSE instructions_es END
                        ELSE instructions_es END,
                    tips_es = CASE WHEN tips_es = '[]'::jsonb
                        THEN COALESCE(source_payload->'tips_es', '[]'::jsonb) ELSE tips_es END,
                    images = CASE WHEN images = '{}'::jsonb
                        THEN COALESCE(source_payload->'images', '{}'::jsonb) ELSE images END,
                    met = COALESCE(met, NULLIF(source_payload->>'met', '')::double precision),
                    is_unilateral = COALESCE(is_unilateral, NULLIF(source_payload->>'is_unilateral', '')::boolean),
                    is_bodyweight = COALESCE(is_bodyweight, NULLIF(source_payload->>'is_bodyweight', '')::boolean),
                    updated_at = NOW()
                WHERE source = 'repdb-free' AND owner_user_id IS NULL
                """;
            await using (var repair = new NpgsqlCommand(repairSql, connection, transaction))
                await repair.ExecuteNonQueryAsync(cancellationToken);

            const string updateSql = """
                UPDATE exercise_catalog
                SET category = CASE
                        WHEN exercise_id = 'extended-side-angle' THEN 'stretching'
                        ELSE category
                    END,
                    primary_muscles = CASE
                        WHEN exercise_id = 'sit-ups' THEN '["rectus_abdominis"]'::jsonb
                        ELSE primary_muscles
                    END,
                    secondary_muscles = CASE
                        WHEN exercise_id = 'sit-ups' THEN '["hip_flexors"]'::jsonb
                        ELSE secondary_muscles
                    END,
                    goals = CASE
                        WHEN category = 'cardio' THEN COALESCE(
                            (SELECT jsonb_agg(to_jsonb(g.goal))
                             FROM jsonb_array_elements_text(exercise_catalog.goals) AS g(goal)
                             WHERE lower(g.goal) NOT IN ('hypertrophy', 'strength')),
                            '["endurance"]'::jsonb)
                        WHEN exercise_id = 'extended-side-angle' THEN '["mobility"]'::jsonb
                        ELSE goals
                    END,
                    review_status = CASE
                        WHEN reviewed_by = 'RepDB automatic metadata v2'
                         AND (category = 'cardio' OR exercise_id IN ('extended-side-angle', 'dead-hang', 'sit-ups'))
                        THEN 'pending' ELSE review_status
                    END,
                    reviewed = CASE
                        WHEN reviewed_by = 'RepDB automatic metadata v2'
                         AND (category = 'cardio' OR exercise_id IN ('extended-side-angle', 'dead-hang', 'sit-ups'))
                        THEN FALSE ELSE reviewed
                    END,
                    reviewed_movement_pattern = CASE
                        WHEN reviewed_by = 'RepDB automatic metadata v2'
                         AND (category = 'cardio' OR exercise_id IN ('extended-side-angle', 'dead-hang', 'sit-ups'))
                        THEN NULL ELSE reviewed_movement_pattern
                    END,
                    reviewed_primary_muscles = CASE
                        WHEN reviewed_by = 'RepDB automatic metadata v2'
                         AND (category = 'cardio' OR exercise_id IN ('extended-side-angle', 'double-kettlebell-clean',
                             'bird-dog-hold', 'dead-hang', 'sit-ups'))
                        THEN NULL ELSE reviewed_primary_muscles
                    END,
                    reviewed_secondary_muscles = CASE
                        WHEN reviewed_by = 'RepDB automatic metadata v2'
                         AND (category = 'cardio' OR exercise_id IN ('extended-side-angle', 'double-kettlebell-clean',
                             'bird-dog-hold', 'dead-hang', 'sit-ups'))
                        THEN NULL ELSE reviewed_secondary_muscles
                    END,
                    reviewed_minimum_experience = CASE
                        WHEN reviewed_by = 'RepDB automatic metadata v2'
                         AND (category = 'cardio' OR exercise_id IN ('extended-side-angle', 'double-kettlebell-clean',
                             'bird-dog-hold', 'dead-hang', 'sit-ups'))
                        THEN NULL ELSE reviewed_minimum_experience
                    END,
                    reviewed_suitable_goals = CASE
                        WHEN reviewed_by = 'RepDB automatic metadata v2'
                         AND (category = 'cardio' OR exercise_id IN ('extended-side-angle', 'double-kettlebell-clean',
                             'bird-dog-hold', 'dead-hang', 'sit-ups'))
                        THEN NULL ELSE reviewed_suitable_goals
                    END,
                    reviewed_required_equipment = CASE
                        WHEN reviewed_by = 'RepDB automatic metadata v2'
                         AND (category = 'cardio' OR exercise_id IN ('extended-side-angle', 'double-kettlebell-clean',
                             'bird-dog-hold', 'dead-hang', 'sit-ups'))
                        THEN NULL ELSE reviewed_required_equipment
                    END,
                    reviewed_at = CASE
                        WHEN reviewed_by = 'RepDB automatic metadata v2'
                         AND (category = 'cardio' OR exercise_id IN ('extended-side-angle', 'double-kettlebell-clean',
                             'bird-dog-hold', 'dead-hang', 'sit-ups'))
                        THEN NULL ELSE reviewed_at
                    END,
                    reviewed_by = CASE
                        WHEN reviewed_by = 'RepDB automatic metadata v2'
                         AND (category = 'cardio' OR exercise_id IN ('extended-side-angle', 'double-kettlebell-clean',
                             'bird-dog-hold', 'dead-hang', 'sit-ups'))
                        THEN NULL ELSE reviewed_by
                    END,
                    updated_at = NOW()
                WHERE source = 'repdb-free'
                  AND (category = 'cardio'
                    OR exercise_id IN ('extended-side-angle', 'sit-ups', 'dead-hang'))
                """;
            await using (var update = new NpgsqlCommand(updateSql, connection, transaction))
                await update.ExecuteNonQueryAsync(cancellationToken);

            await using (var mark = new NpgsqlCommand("INSERT INTO schema_migrations(id) VALUES (@marker) ON CONFLICT (id) DO NOTHING", connection, transaction))
            {
                Add(mark, "marker", marker);
                await mark.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Applied RepDB quality curation v2; pruned redundant/specialist movements and corrected known metadata.");
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Fills missing Spanish descriptions and difficulty estimates for every imported RepDB row.
    /// Only blank values are changed; source-provided and manually edited values are preserved.
    /// </summary>
    private async Task EnsureRepDbMetadataEnrichmentAsync(CancellationToken cancellationToken)
    {
        const string marker = "exercise-catalog-repdb-description-difficulty-v2";
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var check = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @marker)", connection, transaction))
            {
                Add(check, "marker", marker);
                if (Convert.ToBoolean(await check.ExecuteScalarAsync(cancellationToken)))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
            }

            await using (var hasRows = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM exercise_catalog WHERE source = 'repdb-free')", connection, transaction))
            {
                if (!Convert.ToBoolean(await hasRows.ExecuteScalarAsync(cancellationToken)))
                {
                    // Keep the marker unconsumed until the initial RepDB import has completed.
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
            }

            var rows = new List<(string Id, string? NameEs, string NameEn, string? DescriptionEs,
                string? Difficulty, string? Equipment, string? BodyPart, string PrimaryMuscles,
                string InstructionsEs, string? Mechanic, string? ForceType, string Tags)>();
            const string selectSql = """
                SELECT exercise_id, name_es, name_en, description_es, difficulty, equipment, body_part,
                       primary_muscles::text, instructions_es::text, mechanic, force_type, tags::text
                FROM exercise_catalog
                WHERE source = 'repdb-free'
                  AND (NULLIF(BTRIM(description_es), '') IS NULL OR NULLIF(BTRIM(difficulty), '') IS NULL)
                """;
            await using (var select = new NpgsqlCommand(selectSql, connection, transaction))
            await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add((reader.GetString(0), NullableString(reader, 1), reader.GetString(2),
                        NullableString(reader, 3), NullableString(reader, 4), NullableString(reader, 5),
                        NullableString(reader, 6), reader.GetString(7), reader.GetString(8),
                        NullableString(reader, 9), NullableString(reader, 10), reader.GetString(11)));
                }
            }

            var enriched = 0;
            foreach (var row in rows)
            {
                var generatedDescription = string.IsNullOrWhiteSpace(row.DescriptionEs);
                var generatedDifficulty = string.IsNullOrWhiteSpace(row.Difficulty);
                var description = generatedDescription
                    ? RepDbExerciseAutoReviewer.GenerateDescriptionEs(row.NameEs, row.NameEn, row.Equipment, row.BodyPart, row.PrimaryMuscles)
                    : null;
                var difficulty = generatedDifficulty
                    ? RepDbExerciseAutoReviewer.InferDifficulty(null, row.NameEs, row.NameEn, row.Equipment,
                        row.Mechanic, row.ForceType, row.BodyPart, row.Tags, row.InstructionsEs)
                    : null;
                if (description is null && difficulty is null) continue;

                var notes = string.Join("; ", new[]
                {
                    description is not null ? "Descripción en español generada automáticamente" : null,
                    difficulty is not null ? "Dificultad estimada automáticamente" : null
                }.Where(x => x is not null));
                const string updateSql = """
                    UPDATE exercise_catalog
                    SET description_es = COALESCE(NULLIF(BTRIM(description_es), ''), @description),
                        difficulty = COALESCE(NULLIF(BTRIM(difficulty), ''), @difficulty),
                        metadata_enrichment_notes = CASE
                            WHEN NULLIF(metadata_enrichment_notes, '') IS NULL THEN @notes
                            WHEN @notes IS NULL OR metadata_enrichment_notes LIKE '%' || @notes || '%' THEN metadata_enrichment_notes
                            ELSE metadata_enrichment_notes || '; ' || @notes
                        END,
                        updated_at = NOW()
                    WHERE exercise_id = @id AND source = 'repdb-free'
                    """;
                await using var update = new NpgsqlCommand(updateSql, connection, transaction);
                Add(update, "id", row.Id);
                Add(update, "description", description);
                Add(update, "difficulty", difficulty);
                Add(update, "notes", string.IsNullOrWhiteSpace(notes) ? null : notes);
                enriched += await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var mark = new NpgsqlCommand("INSERT INTO schema_migrations(id) VALUES (@marker) ON CONFLICT (id) DO NOTHING", connection, transaction))
            {
                Add(mark, "marker", marker);
                await mark.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("RepDB metadata enrichment populated missing descriptions/difficulty for {Count} exercises.", enriched);
        }
        catch
        {
            throw;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Enriches the already-imported RepDB catalog once. Only untouched pending RepDB rows are eligible;
    /// the marker and updates commit atomically and subsequent starts never overwrite human reviews.
    /// </summary>
    private async Task EnsureRepDbInitialReviewAsync(CancellationToken cancellationToken)
    {
        const string marker = "exercise-catalog-repdb-auto-review-v2";
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var check = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @marker)", connection, transaction))
            {
                Add(check, "marker", marker);
                if (Convert.ToBoolean(await check.ExecuteScalarAsync(cancellationToken)))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
            }

            await using (var hasRepDb = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM exercise_catalog WHERE source = 'repdb-free')", connection, transaction))
            {
                if (!Convert.ToBoolean(await hasRepDb.ExecuteScalarAsync(cancellationToken)))
                {
                    // On a fresh database the catalog has not been imported yet. Do not consume
                    // the one-time review marker before EnsureSeededAsync performs the RepDB bootstrap.
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
            }

            var updates = new List<(string Id, RepDbExerciseAutoReviewer.ReviewMetadata Metadata)>();
            const string selectSql = """
                SELECT exercise_id, name_es, name_en, description_es, instructions_es::text, difficulty,
                       equipment, force_type, mechanic, body_part, primary_muscles::text,
                       secondary_muscles::text, goals::text, tags::text, is_bodyweight
                FROM exercise_catalog
                WHERE source = 'repdb-free' AND review_status = 'pending' AND reviewed = FALSE
                  AND reviewed_at IS NULL AND reviewed_by IS NULL
                """;
            await using (var select = new NpgsqlCommand(selectSql, connection, transaction))
            await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var metadata = RepDbExerciseAutoReviewer.TryBuild(
                        reader.GetString(0), NullableString(reader, 1), reader.GetString(2), NullableString(reader, 3),
                        reader.GetString(4), NullableString(reader, 5), NullableString(reader, 6), NullableString(reader, 7),
                        NullableString(reader, 8), NullableString(reader, 9), reader.GetString(10), reader.GetString(11),
                        reader.GetString(12), reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetBoolean(14));
                    if (metadata is not null) updates.Add((reader.GetString(0), metadata));
                }
            }

            const string updateSql = """
                UPDATE exercise_catalog SET
                    review_status = 'approved', reviewed = TRUE,
                    reviewed_movement_pattern = @pattern,
                    reviewed_primary_muscles = @primary::jsonb,
                    reviewed_secondary_muscles = @secondary::jsonb,
                    reviewed_minimum_experience = @experience,
                    reviewed_suitable_goals = @goals::jsonb,
                    reviewed_required_equipment = @equipment::jsonb,
                    reviewed_selection_priority = @priority,
                    review_notes = @notes, reviewed_at = NOW(),
                    reviewed_by = 'RepDB automatic metadata v2', updated_at = NOW()
                WHERE exercise_id = @id AND source = 'repdb-free'
                  AND review_status = 'pending' AND reviewed = FALSE
                  AND reviewed_at IS NULL AND reviewed_by IS NULL
                """;
            foreach (var item in updates)
            {
                await using var update = new NpgsqlCommand(updateSql, connection, transaction);
                Add(update, "id", item.Id);
                Add(update, "pattern", item.Metadata.MovementPattern);
                Add(update, "primary", JsonSerializer.Serialize(item.Metadata.PrimaryMuscles));
                Add(update, "secondary", JsonSerializer.Serialize(item.Metadata.SecondaryMuscles));
                Add(update, "experience", item.Metadata.MinimumExperience);
                Add(update, "goals", JsonSerializer.Serialize(item.Metadata.SuitableGoals));
                Add(update, "equipment", JsonSerializer.Serialize(item.Metadata.RequiredEquipment));
                Add(update, "priority", item.Metadata.SelectionPriority);
                Add(update, "notes", item.Metadata.Notes);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var mark = new NpgsqlCommand("INSERT INTO schema_migrations(id) VALUES (@marker) ON CONFLICT (id) DO NOTHING", connection, transaction))
            {
                Add(mark, "marker", marker);
                await mark.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("RepDB automatic review enriched and approved {ApprovedCount} high-confidence exercises; ambiguous records remain pending.", updates.Count);
        }
        catch
        {
            // If anything fails, the transaction rolls back and the pass can be safely retried.
            throw;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    public sealed record ExerciseCatalogReviewStats(
        long total, long pending, long approved, long rejected,
        long repDbTotal, long repDbPending, long repDbApproved, long repDbRejected,
        long repDbAutoApprovedV2, long repDbAutoEnriched,
        long pendingMissingSpanishName, long pendingMissingDescription,
        long pendingMissingDifficulty, long pendingMissingInstructions,
        DateTimeOffset? lastSourceUpdate);

    /// <summary>Returns whole-catalog review counts, independent of the paginated review queue.</summary>
    public async Task<ExerciseCatalogReviewStats> GetReviewStatsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE review_status = 'pending') AS pending,
                COUNT(*) FILTER (WHERE review_status = 'approved') AS approved,
                COUNT(*) FILTER (WHERE review_status = 'rejected') AS rejected,
                COUNT(*) FILTER (WHERE source = 'repdb-free') AS repdb_total,
                COUNT(*) FILTER (WHERE source = 'repdb-free' AND review_status = 'pending') AS repdb_pending,
                COUNT(*) FILTER (WHERE source = 'repdb-free' AND review_status = 'approved') AS repdb_approved,
                COUNT(*) FILTER (WHERE source = 'repdb-free' AND review_status = 'rejected') AS repdb_rejected,
                COUNT(*) FILTER (WHERE source = 'repdb-free' AND reviewed_by = 'RepDB automatic metadata v2') AS repdb_auto_approved_v2,
                COUNT(*) FILTER (WHERE source = 'repdb-free' AND metadata_enrichment_notes IS NOT NULL AND metadata_enrichment_notes <> '') AS repdb_auto_enriched,
                COUNT(*) FILTER (WHERE source = 'repdb-free' AND review_status = 'pending' AND NULLIF(BTRIM(name_es), '') IS NULL) AS pending_missing_spanish_name,
                COUNT(*) FILTER (WHERE source = 'repdb-free' AND review_status = 'pending' AND NULLIF(BTRIM(description_es), '') IS NULL) AS pending_missing_description,
                COUNT(*) FILTER (WHERE source = 'repdb-free' AND review_status = 'pending' AND NULLIF(BTRIM(difficulty), '') IS NULL) AS pending_missing_difficulty,
                COUNT(*) FILTER (WHERE source = 'repdb-free' AND review_status = 'pending' AND
                    (CASE WHEN jsonb_typeof(instructions_es) = 'array' THEN jsonb_array_length(instructions_es) < 2 ELSE TRUE END)) AS pending_missing_instructions,
                MAX(source_updated_at) FILTER (WHERE source = 'repdb-free') AS last_source_update
            FROM exercise_catalog
            """;
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return new ExerciseCatalogReviewStats(0,0,0,0,0,0,0,0,0,0,0,0,0,0,null);
            return new ExerciseCatalogReviewStats(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
                reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7),
                reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10), reader.GetInt64(11),
                reader.GetInt64(12), reader.GetInt64(13),
                reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14));
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Returns every pending row with the full normalized metadata and original source payload.
    /// This is intentionally separate from the paginated UI DTO so batch audits do not lose fields.
    /// </summary>
    public async Task<IReadOnlyList<ExerciseCatalogExportItem>> ExportReviewQueueAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            SELECT exercise_id, name_es, name_en, description_es, description_en, category, force_type, mechanic,
                   difficulty, equipment, body_part, primary_muscles::text, secondary_muscles::text, goals::text,
                   tags::text, instructions_es::text, tips_es::text, images::text, met, is_unilateral, is_bodyweight,
                   source, review_status, reviewed, reviewed_movement_pattern, reviewed_primary_muscles::text,
                   reviewed_secondary_muscles::text, reviewed_minimum_experience, reviewed_suitable_goals::text,
                   reviewed_required_equipment::text, reviewed_selection_priority, review_notes, reviewed_at, reviewed_by,
                   source_payload->>'contributor', source_payload::text
            FROM exercise_catalog
            WHERE review_status = 'pending'
            ORDER BY source, name_es NULLS LAST, name_en
            """;
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            var items = new List<ExerciseCatalogExportItem>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ExerciseCatalogExportItem(
                    reader.GetString(0), NullableString(reader, 1), reader.GetString(2), NullableString(reader, 3),
                    NullableString(reader, 4), NullableString(reader, 5), NullableString(reader, 6), NullableString(reader, 7),
                    NullableString(reader, 8), NullableString(reader, 9), NullableString(reader, 10),
                    ParseElement(reader.GetString(11)), ParseElement(reader.GetString(12)), ParseElement(reader.GetString(13)),
                    ParseElement(reader.GetString(14)), ParseElement(reader.GetString(15)), ParseElement(reader.GetString(16)),
                    ParseElement(reader.GetString(17)), reader.IsDBNull(18) ? null : reader.GetDouble(18),
                    reader.IsDBNull(19) ? null : reader.GetBoolean(19), reader.IsDBNull(20) ? null : reader.GetBoolean(20),
                    reader.GetString(21), reader.GetString(22), reader.GetBoolean(23), NullableString(reader, 24),
                    NullableJson(reader, 25), NullableJson(reader, 26), NullableString(reader, 27), NullableJson(reader, 28),
                    NullableJson(reader, 29), reader.GetInt32(30), NullableString(reader, 31),
                    reader.IsDBNull(32) ? null : reader.GetDateTime(32), NullableString(reader, 33),
                    NullableString(reader, 34), ParseElement(reader.GetString(35))));
            }
            return items;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    public async Task<IReadOnlyList<ExerciseCatalogItem>> SearchAsync(string? query, string? bodyPart, string? equipment, int limit, CancellationToken cancellationToken = default, string? reviewStatus = null, int? ownerUserId = null, bool includeAllOwners = false, int maxLimit = 100)
    {
        await EnsureSchemaAsync(cancellationToken);
        limit = Math.Clamp(limit, 1, Math.Clamp(maxLimit, 1, 10000));
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

    private static string JsonText(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetRawText() : "[]";

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
public sealed record ExerciseCatalogExportItem(
    string Id, string? NameEs, string NameEn, string? DescriptionEs, string? DescriptionEn,
    string? Category, string? ForceType, string? Mechanic, string? Difficulty, string? Equipment, string? BodyPart,
    JsonElement PrimaryMuscles, JsonElement SecondaryMuscles, JsonElement Goals, JsonElement Tags,
    JsonElement InstructionsEs, JsonElement TipsEs, JsonElement Images, double? Met, bool? IsUnilateral, bool? IsBodyweight,
    string Source, string ReviewStatus, bool Reviewed, string? MovementPattern,
    JsonElement? ReviewedPrimaryMuscles, JsonElement? ReviewedSecondaryMuscles, string? MinimumExperience,
    JsonElement? SuitableGoals, JsonElement? RequiredEquipment, int SelectionPriority, string? ReviewNotes,
    DateTime? ReviewedAt, string? ReviewedBy, string? Contributor, JsonElement SourcePayload);
public sealed record ExerciseCatalogItem(
    string Id, string? NameEs, string NameEn, string? Category, string? BodyPart, string? Equipment,
    JsonElement PrimaryMuscles, JsonElement SecondaryMuscles, JsonElement Goals, JsonElement Tags,
    JsonElement InstructionsEs, JsonElement Images, string Source, string ReviewStatus, bool Reviewed,
    string? MovementPattern, JsonElement? ReviewedPrimaryMuscles, JsonElement? ReviewedSecondaryMuscles,
    string? MinimumExperience, JsonElement? SuitableGoals, JsonElement? RequiredEquipment,
    int SelectionPriority, string? ReviewNotes, DateTime? ReviewedAt, string? ReviewedBy, string? Contributor);

public sealed record ExerciseReviewRequest(string Status, string? MovementPattern, string[] PrimaryMuscles, string[] SecondaryMuscles, string MinimumExperience, string[] SuitableGoals, string[] RequiredEquipment, int SelectionPriority, string? Notes);
public sealed record ExerciseContributionRequest(string Name, string? Description, string? Equipment, string? BodyPart, string? MovementPattern, string[]? PrimaryMuscles, string[]? SecondaryMuscles, string? MinimumExperience, string[]? SuitableGoals, string[]? RequiredEquipment, int SelectionPriority = 50);
