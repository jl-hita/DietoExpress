using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Logica;

/// <summary>
/// Aplica al arrancar las correcciones versionadas de <c>ExerciseCuration/exercise-catalog-corrections.json</c> sobre
/// <c>exercise_catalog</c>. Idempotente (marca la versión en <c>reviewed_by</c>) y conservadora: solo reescribe filas sin
/// revisar, con revisión automática o de curación, o cuyos metadatos no son válidos para el generador. Una revisión
/// humana válida nunca se pisa. Los campos nulos de una corrección conservan el valor actual.
/// </summary>
public static class ExerciseCatalogCorrectionsSeeder
{
    public const string FileName = "exercise-catalog-corrections.json";
    public const string ReviewerPrefix = "DietoExpress exercise corrections v";
    private static readonly string[] AutomaticReviewers =
        { "RepDB automatic metadata v2", "DietoExpress generator curation v3" };

    public sealed class Entry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("nameEs")] public string? NameEs { get; set; }
        [JsonPropertyName("reviewStatus")] public string? ReviewStatus { get; set; }
        [JsonPropertyName("movementPattern")] public string? MovementPattern { get; set; }
        [JsonPropertyName("primaryMuscles")] public string[]? PrimaryMuscles { get; set; }
        [JsonPropertyName("secondaryMuscles")] public string[]? SecondaryMuscles { get; set; }
        [JsonPropertyName("minimumExperience")] public string? MinimumExperience { get; set; }
        [JsonPropertyName("suitableGoals")] public string[]? SuitableGoals { get; set; }
        [JsonPropertyName("requiredEquipment")] public string[]? RequiredEquipment { get; set; }
        [JsonPropertyName("selectionPriority")] public int? SelectionPriority { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; } = "";
    }

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "ExerciseCuration", FileName);

    /// <summary>Devuelve el motivo si la entrada rompe el contrato del generador; null si es válida.</summary>
    public static string? Validate(Entry e)
    {
        if (string.IsNullOrWhiteSpace(e.Id)) return "id vacío";
        if (e.Version < 1) return "versión inválida";
        if (e.ReviewStatus is not null && e.ReviewStatus is not ("approved" or "pending" or "rejected")) return "estado de revisión inválido";
        if (e.MovementPattern is not null && !Enum.TryParse<TrainingMovementPattern>(e.MovementPattern, true, out _)) return $"patrón '{e.MovementPattern}' inválido";
        if (e.MinimumExperience is not null && !Enum.TryParse<TrainingExperience>(e.MinimumExperience, true, out _)) return $"experiencia '{e.MinimumExperience}' inválida";
        foreach (var m in (e.PrimaryMuscles ?? Array.Empty<string>()).Concat(e.SecondaryMuscles ?? Array.Empty<string>()))
            if (!Enum.TryParse<TrainingMuscleGroup>(m, true, out _)) return $"músculo '{m}' inválido";
        foreach (var g in e.SuitableGoals ?? Array.Empty<string>())
            if (!Enum.TryParse<TrainingGoal>(g, true, out _)) return $"objetivo '{g}' inválido";
        if (e.PrimaryMuscles is { Length: 0 }) return "primaryMuscles vacío";
        if (e.SuitableGoals is { Length: 0 }) return "suitableGoals vacío";
        if (e.RequiredEquipment?.Any(string.IsNullOrWhiteSpace) == true) return "requiredEquipment con valores vacíos";
        if (e.ReviewStatus == "approved" && (e.MovementPattern is null || e.PrimaryMuscles is null || e.MinimumExperience is null || e.SuitableGoals is null || e.RequiredEquipment is null || e.SelectionPriority is null))
            return "una aprobación necesita todos los metadatos";
        return null;
    }

    public static async Task<int> EnsureAppliedAsync(NpgsqlConnection connection, ILogger logger, string? path = null, CancellationToken cancellationToken = default)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) { logger.LogWarning("No se encontró {Path}; se omiten las correcciones del catálogo de ejercicios.", path); return 0; }

        List<Entry> entries;
        await using (var stream = File.OpenRead(path))
            entries = await JsonSerializer.DeserializeAsync<List<Entry>>(stream, cancellationToken: cancellationToken) ?? new();

        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        var applied = 0;
        try
        {
            foreach (var e in entries)
            {
                var error = Validate(e);
                if (error != null) { logger.LogWarning("Corrección de ejercicio {Id} ignorada: {Error}", e.Id, error); continue; }

                string? reviewer, primary, secondary, goals, pattern;
                await using (var read = new NpgsqlCommand(
                    "SELECT reviewed_by, reviewed_movement_pattern, reviewed_primary_muscles::text, reviewed_secondary_muscles::text, reviewed_suitable_goals::text " +
                    "FROM exercise_catalog WHERE exercise_id = @id AND source = 'repdb-free' AND owner_user_id IS NULL", connection))
                {
                    read.Parameters.AddWithValue("id", e.Id);
                    await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken)) continue;
                    reviewer = reader.IsDBNull(0) ? null : reader.GetString(0);
                    pattern = reader.IsDBNull(1) ? null : reader.GetString(1);
                    primary = reader.IsDBNull(2) ? null : reader.GetString(2);
                    secondary = reader.IsDBNull(3) ? null : reader.GetString(3);
                    goals = reader.IsDBNull(4) ? null : reader.GetString(4);
                }

                if (reviewer != null && reviewer.StartsWith(ReviewerPrefix, StringComparison.Ordinal) &&
                    int.TryParse(reviewer[ReviewerPrefix.Length..], out var appliedVersion) && appliedVersion >= e.Version)
                    continue; // ya aplicada

                var overwritable = reviewer == null || AutomaticReviewers.Contains(reviewer) ||
                                   reviewer.StartsWith(ReviewerPrefix, StringComparison.Ordinal) ||
                                   !MetadataIsValidForGenerator(pattern, primary, secondary, goals);
                if (!overwritable)
                {
                    logger.LogInformation("Corrección de ejercicio {Id} omitida: tiene una revisión humana válida ({Reviewer}).", e.Id, reviewer);
                    continue;
                }

                await using var update = new NpgsqlCommand(
                    "UPDATE exercise_catalog SET " +
                    "name_es = COALESCE(@name, name_es), " +
                    "review_status = COALESCE(@status, review_status), " +
                    "reviewed = (COALESCE(@status, review_status) = 'approved'), " +
                    "reviewed_movement_pattern = COALESCE(@pattern, reviewed_movement_pattern), " +
                    "reviewed_primary_muscles = COALESCE(@primary::jsonb, reviewed_primary_muscles), " +
                    "reviewed_secondary_muscles = COALESCE(@secondary::jsonb, reviewed_secondary_muscles), " +
                    "reviewed_minimum_experience = COALESCE(@experience, reviewed_minimum_experience), " +
                    "reviewed_suitable_goals = COALESCE(@goals::jsonb, reviewed_suitable_goals), " +
                    "reviewed_required_equipment = COALESCE(@equipment::jsonb, reviewed_required_equipment), " +
                    "reviewed_selection_priority = COALESCE(@priority, reviewed_selection_priority), " +
                    "review_notes = @note, reviewed_at = NOW(), reviewed_by = @reviewer, updated_at = NOW() " +
                    "WHERE exercise_id = @id AND source = 'repdb-free' AND owner_user_id IS NULL", connection);
                update.Parameters.AddWithValue("id", e.Id);
                update.Parameters.AddWithValue("name", NpgsqlDbType.Text, (object?)e.NameEs ?? DBNull.Value);
                update.Parameters.AddWithValue("status", NpgsqlDbType.Text, (object?)e.ReviewStatus ?? DBNull.Value);
                update.Parameters.AddWithValue("pattern", NpgsqlDbType.Text, (object?)e.MovementPattern ?? DBNull.Value);
                update.Parameters.AddWithValue("primary", NpgsqlDbType.Text, Json(e.PrimaryMuscles));
                update.Parameters.AddWithValue("secondary", NpgsqlDbType.Text, Json(e.SecondaryMuscles));
                update.Parameters.AddWithValue("experience", NpgsqlDbType.Text, (object?)e.MinimumExperience ?? DBNull.Value);
                update.Parameters.AddWithValue("goals", NpgsqlDbType.Text, Json(e.SuitableGoals));
                update.Parameters.AddWithValue("equipment", NpgsqlDbType.Text, Json(e.RequiredEquipment));
                update.Parameters.AddWithValue("priority", NpgsqlDbType.Integer, (object?)e.SelectionPriority ?? DBNull.Value);
                update.Parameters.AddWithValue("note", string.IsNullOrWhiteSpace(e.Note) ? "Corrección DietoExpress del catálogo de ejercicios." : e.Note);
                update.Parameters.AddWithValue("reviewer", ReviewerPrefix + e.Version);
                applied += await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally { if (shouldClose) await connection.CloseAsync(); }

        logger.LogInformation("Correcciones del catálogo de ejercicios: {Entries} entradas; {Applied} aplicadas.", entries.Count, applied);
        return applied;
    }

    private static object Json(string[]? values) => values is null ? DBNull.Value : JsonSerializer.Serialize(values);

    /// <summary>¿Parsearía el generador estos metadatos? Si no, la fila queda excluida en silencio y conviene corregirla.</summary>
    public static bool MetadataIsValidForGenerator(string? pattern, string? primaryJson, string? secondaryJson, string? goalsJson)
    {
        if (!Enum.TryParse<TrainingMovementPattern>(pattern, true, out _)) return false;
        static string[] Items(string? json) => string.IsNullOrWhiteSpace(json) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        var primary = Items(primaryJson); var secondary = Items(secondaryJson); var goals = Items(goalsJson);
        return primary.Length > 0 && primary.All(m => Enum.TryParse<TrainingMuscleGroup>(m, true, out _))
            && secondary.All(m => Enum.TryParse<TrainingMuscleGroup>(m, true, out _))
            && goals.Length > 0 && goals.All(g => Enum.TryParse<TrainingGoal>(g, true, out _));
    }
}
