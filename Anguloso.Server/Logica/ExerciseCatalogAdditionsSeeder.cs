using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Logica;

/// <summary>
/// Ejercicios básicos que el catálogo RepDB no incluye, versionados en <c>ExerciseCuration/exercise-catalog-additions.json</c>.
/// Se insertan en <c>exercise_catalog</c> con <c>source = 'dietoexpress-curated'</c> (las importaciones y limpiezas de RepDB solo
/// tocan <c>repdb-free</c>, así que no los borran). El upsert es idempotente: inserta los que faltan y actualiza los de versión
/// inferior, pero nunca pisa una fila cuyo revisor ya no sea el de este seeder (revisión humana posterior).
/// </summary>
public static class ExerciseCatalogAdditionsSeeder
{
    public const string FileName = "exercise-catalog-additions.json";
    public const string Source = "dietoexpress-curated";
    public const string ReviewerPrefix = "DietoExpress curated additions v";

    public sealed class Entry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("nameEs")] public string NameEs { get; set; } = "";
        [JsonPropertyName("nameEn")] public string NameEn { get; set; } = "";
        [JsonPropertyName("descriptionEs")] public string DescriptionEs { get; set; } = "";
        [JsonPropertyName("instructionsEs")] public string[] InstructionsEs { get; set; } = Array.Empty<string>();
        [JsonPropertyName("tipsEs")] public string[] TipsEs { get; set; } = Array.Empty<string>();
        [JsonPropertyName("forceType")] public string ForceType { get; set; } = "";
        [JsonPropertyName("mechanic")] public string Mechanic { get; set; } = "";
        [JsonPropertyName("difficulty")] public string Difficulty { get; set; } = "";
        [JsonPropertyName("equipment")] public string? Equipment { get; set; }
        [JsonPropertyName("bodyPart")] public string BodyPart { get; set; } = "";
        [JsonPropertyName("met")] public double Met { get; set; } = 3.5;
        [JsonPropertyName("isUnilateral")] public bool IsUnilateral { get; set; }
        [JsonPropertyName("isBodyweight")] public bool IsBodyweight { get; set; }
        [JsonPropertyName("movementPattern")] public string MovementPattern { get; set; } = "";
        [JsonPropertyName("primaryMuscles")] public string[] PrimaryMuscles { get; set; } = Array.Empty<string>();
        [JsonPropertyName("secondaryMuscles")] public string[] SecondaryMuscles { get; set; } = Array.Empty<string>();
        [JsonPropertyName("minimumExperience")] public string MinimumExperience { get; set; } = "";
        [JsonPropertyName("suitableGoals")] public string[] SuitableGoals { get; set; } = Array.Empty<string>();
        [JsonPropertyName("requiredEquipment")] public string[] RequiredEquipment { get; set; } = Array.Empty<string>();
        [JsonPropertyName("selectionPriority")] public int SelectionPriority { get; set; } = 50;
    }

    public sealed record Result(int Inserted, int Updated, int Skipped);

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "ExerciseCuration", FileName);

    /// <summary>Devuelve el motivo si la entrada rompe el contrato del generador; null si es válida.</summary>
    public static string? Validate(Entry e)
    {
        if (string.IsNullOrWhiteSpace(e.Id) || e.Id.Length > 100 || e.Id.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))) return "id inválido (minúsculas, dígitos y guiones)";
        if (e.Version < 1) return "versión inválida";
        if (string.IsNullOrWhiteSpace(e.NameEs) || string.IsNullOrWhiteSpace(e.NameEn)) return "nombres vacíos";
        if (e.InstructionsEs.Length < 3 || e.InstructionsEs.Any(string.IsNullOrWhiteSpace)) return "necesita al menos 3 instrucciones";
        if (!Enum.TryParse<TrainingMovementPattern>(e.MovementPattern, true, out _)) return $"patrón '{e.MovementPattern}' inválido";
        if (!Enum.TryParse<TrainingExperience>(e.MinimumExperience, true, out _)) return $"experiencia '{e.MinimumExperience}' inválida";
        if (e.PrimaryMuscles.Length == 0) return "primaryMuscles vacío";
        foreach (var m in e.PrimaryMuscles.Concat(e.SecondaryMuscles))
            if (!Enum.TryParse<TrainingMuscleGroup>(m, true, out _)) return $"músculo '{m}' inválido";
        if (e.SuitableGoals.Length == 0) return "suitableGoals vacío";
        foreach (var g in e.SuitableGoals)
            if (!Enum.TryParse<TrainingGoal>(g, true, out _)) return $"objetivo '{g}' inválido";
        if (e.RequiredEquipment.Any(string.IsNullOrWhiteSpace)) return "requiredEquipment con valores vacíos";
        if (e.SelectionPriority is < 0 or > 100) return "prioridad fuera de 0-100";
        return null;
    }

    /// <summary>Número de ejercicios de este seeder ya presentes en la BBDD.</summary>
    public static async Task<int> CountExistingAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM exercise_catalog WHERE source = @source", connection);
            command.Parameters.AddWithValue("source", Source);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally { if (shouldClose) await connection.CloseAsync(); }
    }

    public static async Task<Result> UpsertAsync(NpgsqlConnection connection, ILogger logger, string? path = null, CancellationToken cancellationToken = default)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) { logger.LogWarning("No se encontró {Path}; se omiten los ejercicios adicionales.", path); return new Result(0, 0, 0); }

        List<Entry> entries;
        await using (var stream = File.OpenRead(path))
            entries = await JsonSerializer.DeserializeAsync<List<Entry>>(stream, cancellationToken: cancellationToken) ?? new();

        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        int inserted = 0, updated = 0, skipped = 0;
        try
        {
            foreach (var e in entries)
            {
                var error = Validate(e);
                if (error != null) { logger.LogWarning("Ejercicio adicional {Id} ignorado: {Error}", e.Id, error); skipped++; continue; }

                var payload = JsonSerializer.Serialize(new { curated = true, origin = "DietoExpress", id = e.Id, version = e.Version });
                await using var command = new NpgsqlCommand(
                    "INSERT INTO exercise_catalog (exercise_id, source, author, source_schema_version, source_payload, name_en, name_es, description_es, " +
                    "instructions_es, tips_es, category, force_type, mechanic, difficulty, equipment, body_part, primary_muscles, secondary_muscles, goals, tags, " +
                    "met, is_unilateral, is_bodyweight, reviewed, review_status, review_notes, reviewed_movement_pattern, reviewed_primary_muscles, " +
                    "reviewed_secondary_muscles, reviewed_minimum_experience, reviewed_suitable_goals, reviewed_required_equipment, reviewed_selection_priority, " +
                    "reviewed_at, reviewed_by) VALUES (@id, @source, 'DietoExpress', @schema, @payload::jsonb, @name_en, @name_es, @description, " +
                    "@instructions::jsonb, @tips::jsonb, 'strength', @force, @mechanic, @difficulty, @equipment, @body_part, @primary::jsonb, @secondary::jsonb, @goals::jsonb, '[]'::jsonb, " +
                    "@met, @unilateral, @bodyweight, TRUE, 'approved', @notes, @pattern, @primary::jsonb, @secondary::jsonb, @experience, @goals::jsonb, @required::jsonb, @priority, " +
                    "NOW(), @reviewer) " +
                    "ON CONFLICT (exercise_id) DO UPDATE SET " +
                    "source_payload = EXCLUDED.source_payload, name_en = EXCLUDED.name_en, name_es = EXCLUDED.name_es, description_es = EXCLUDED.description_es, " +
                    "instructions_es = EXCLUDED.instructions_es, tips_es = EXCLUDED.tips_es, force_type = EXCLUDED.force_type, mechanic = EXCLUDED.mechanic, " +
                    "difficulty = EXCLUDED.difficulty, equipment = EXCLUDED.equipment, body_part = EXCLUDED.body_part, primary_muscles = EXCLUDED.primary_muscles, " +
                    "secondary_muscles = EXCLUDED.secondary_muscles, goals = EXCLUDED.goals, met = EXCLUDED.met, is_unilateral = EXCLUDED.is_unilateral, " +
                    "is_bodyweight = EXCLUDED.is_bodyweight, reviewed_movement_pattern = EXCLUDED.reviewed_movement_pattern, " +
                    "reviewed_primary_muscles = EXCLUDED.reviewed_primary_muscles, reviewed_secondary_muscles = EXCLUDED.reviewed_secondary_muscles, " +
                    "reviewed_minimum_experience = EXCLUDED.reviewed_minimum_experience, reviewed_suitable_goals = EXCLUDED.reviewed_suitable_goals, " +
                    "reviewed_required_equipment = EXCLUDED.reviewed_required_equipment, reviewed_selection_priority = EXCLUDED.reviewed_selection_priority, " +
                    "review_notes = EXCLUDED.review_notes, reviewed_at = NOW(), reviewed_by = EXCLUDED.reviewed_by, updated_at = NOW() " +
                    "WHERE exercise_catalog.source = @source " +
                    "AND exercise_catalog.reviewed_by ~ '^DietoExpress curated additions v[0-9]+$' " +
                    "AND substring(exercise_catalog.reviewed_by from '[0-9]+$')::int < @version " +
                    "RETURNING (xmax = 0)", connection);
                command.Parameters.AddWithValue("id", e.Id);
                command.Parameters.AddWithValue("source", Source);
                command.Parameters.AddWithValue("schema", "dietoexpress-additions-v" + e.Version);
                command.Parameters.AddWithValue("payload", NpgsqlDbType.Text, payload);
                command.Parameters.AddWithValue("name_en", e.NameEn);
                command.Parameters.AddWithValue("name_es", e.NameEs);
                command.Parameters.AddWithValue("description", e.DescriptionEs);
                command.Parameters.AddWithValue("instructions", NpgsqlDbType.Text, JsonSerializer.Serialize(e.InstructionsEs));
                command.Parameters.AddWithValue("tips", NpgsqlDbType.Text, JsonSerializer.Serialize(e.TipsEs));
                command.Parameters.AddWithValue("force", e.ForceType);
                command.Parameters.AddWithValue("mechanic", e.Mechanic);
                command.Parameters.AddWithValue("difficulty", e.Difficulty);
                command.Parameters.AddWithValue("equipment", NpgsqlDbType.Text, (object?)e.Equipment ?? DBNull.Value);
                command.Parameters.AddWithValue("body_part", e.BodyPart);
                command.Parameters.AddWithValue("primary", NpgsqlDbType.Text, JsonSerializer.Serialize(e.PrimaryMuscles));
                command.Parameters.AddWithValue("secondary", NpgsqlDbType.Text, JsonSerializer.Serialize(e.SecondaryMuscles));
                command.Parameters.AddWithValue("goals", NpgsqlDbType.Text, JsonSerializer.Serialize(e.SuitableGoals));
                command.Parameters.AddWithValue("met", e.Met);
                command.Parameters.AddWithValue("unilateral", e.IsUnilateral);
                command.Parameters.AddWithValue("bodyweight", e.IsBodyweight);
                command.Parameters.AddWithValue("notes", "Ejercicio básico añadido por DietoExpress: no figura en el catálogo RepDB.");
                command.Parameters.AddWithValue("pattern", e.MovementPattern);
                command.Parameters.AddWithValue("experience", e.MinimumExperience);
                command.Parameters.AddWithValue("required", NpgsqlDbType.Text, JsonSerializer.Serialize(e.RequiredEquipment));
                command.Parameters.AddWithValue("priority", e.SelectionPriority);
                command.Parameters.AddWithValue("reviewer", ReviewerPrefix + e.Version);
                command.Parameters.AddWithValue("version", e.Version);

                var wasInsert = await command.ExecuteScalarAsync(cancellationToken);
                if (wasInsert is null || wasInsert is DBNull) skipped++;       // existe y no procede actualizar
                else if ((bool)wasInsert) inserted++;
                else updated++;
            }
        }
        finally { if (shouldClose) await connection.CloseAsync(); }

        return new Result(inserted, updated, skipped);
    }
}
