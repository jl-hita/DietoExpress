using System.Data;
using System.Data.Common;
using System.Text.Json;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Resuelve las reglas activas de las especializaciones de un paciente.
/// Las reglas permanecen fuera del generador para que nuevas especializaciones
/// puedan añadir restricciones sin acoplarlas al algoritmo de generación.
/// </summary>
public sealed class SpecializationRulesService
{
    private readonly angulosodbContext _context;

    public SpecializationRulesService(angulosodbContext context) => _context = context;

    public async Task<HashSet<string>> GetFoodExclusionsAsync(
        int clientId,
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT sr.configuration
            FROM client_specializations cs
            JOIN specializations s
              ON s.id=cs.specialization_id AND s.active=TRUE
            JOIN tenant_specializations ts
              ON ts.specialization_id=s.id
             AND ts.tenant_id=cs.tenant_id
             AND ts.enabled=TRUE
            JOIN specialization_rules sr
              ON sr.specialization_id=s.id
             AND sr.rule_type='food_exclusion'
             AND sr.active=TRUE
            WHERE cs.client_id=@client
              AND cs.tenant_id=@tenant
            ORDER BY sr.priority ASC, sr.id ASC;";

        Add(command, "client", clientId);
        Add(command, "tenant", tenantId);

        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0)) continue;

            try
            {
                using var document = JsonDocument.Parse(reader.GetString(0));
                if (!document.RootElement.TryGetProperty("keywords", out var keywords))
                    continue;

                foreach (var keyword in keywords.EnumerateArray())
                {
                    var value = keyword.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        exclusions.Add(value);
                }
            }
            catch (JsonException)
            {
                // Una regla mal formada no debe impedir generar una dieta; se omite y queda
                // registrada en el catálogo para que pueda corregirse sin tocar el motor.
            }
        }

        return exclusions;
    }


    /// <summary>
    /// Resuelve alimentos incompatibles usando atributos dietéticos estructurados de la tabla
    /// foods y las banderas declaradas por las reglas de especialización. Mantiene la
    /// coincidencia textual como respaldo para alimentos todavía no clasificados.
    /// </summary>
    public async Task<HashSet<int>> GetExcludedFoodIdsAsync(
        int clientId,
        int tenantId,
        IReadOnlyCollection<int> foodIds,
        CancellationToken cancellationToken = default)
    {
        var excluded = new HashSet<int>();
        if (foodIds.Count == 0) return excluded;

        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        var requiredFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var ruleCommand = connection.CreateCommand())
        {
            ruleCommand.CommandText = @"
                SELECT sr.configuration
                FROM client_specializations cs
                JOIN specializations s
                  ON s.id=cs.specialization_id AND s.active=TRUE
                JOIN tenant_specializations ts
                  ON ts.specialization_id=s.id
                 AND ts.tenant_id=cs.tenant_id
                 AND ts.enabled=TRUE
                JOIN specialization_rules sr
                  ON sr.specialization_id=s.id
                 AND sr.rule_type='food_exclusion'
                 AND sr.active=TRUE
                WHERE cs.client_id=@client
                  AND cs.tenant_id=@tenant;";

            Add(ruleCommand, "client", clientId);
            Add(ruleCommand, "tenant", tenantId);

            await using var reader = await ruleCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0)) continue;

                try
                {
                    using var document = JsonDocument.Parse(reader.GetString(0));
                    if (!document.RootElement.TryGetProperty("required_flags", out var flags))
                        continue;

                    foreach (var flag in flags.EnumerateArray())
                    {
                        var value = flag.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                            requiredFlags.Add(value);
                    }
                }
                catch (JsonException)
                {
                    // La regla textual seguirá funcionando si la configuración estructurada no es válida.
                }
            }
        }

        if (requiredFlags.Count > 0)
        {
            await using var foodCommand = connection.CreateCommand();
            foodCommand.CommandText = @"
                SELECT id
                FROM foods
                WHERE id = ANY(@food_ids::integer[])
                  AND dietary_flags && @required_flags::text[];";

            Add(foodCommand, "food_ids", foodIds.ToArray());
            Add(foodCommand, "required_flags", requiredFlags.ToArray());

            await using var reader = await foodCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                excluded.Add(reader.GetInt32(0));
        }

        return excluded;
    }

public sealed record NutritionProfile(
    double ProteinGramsPerKg,
    double? ProteinMinGramsPerKg,
    double? ProteinMaxGramsPerKg);

    /// <summary>
    /// Devuelve el perfil nutricional estructurado de la especialización deportiva.
    /// Si hay más de un perfil incompatible activo, no se aplica ninguno automáticamente.
    /// </summary>
    public async Task<NutritionProfile?> GetNutritionProfileAsync(
        int clientId,
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT sr.configuration
            FROM client_specializations cs
            JOIN specializations s ON s.id=cs.specialization_id AND s.active=TRUE
            JOIN tenant_specializations ts
              ON ts.specialization_id=s.id AND ts.tenant_id=cs.tenant_id AND ts.enabled=TRUE
            JOIN specialization_rules sr
              ON sr.specialization_id=s.id AND sr.rule_type='nutrition_profile' AND sr.active=TRUE
            WHERE cs.client_id=@client AND cs.tenant_id=@tenant
            ORDER BY sr.priority ASC, sr.id ASC;";

        Add(command, "client", clientId);
        Add(command, "tenant", tenantId);

        NutritionProfile? profile = null;
        var profilesFound = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0)) continue;
            try
            {
                using var document = JsonDocument.Parse(reader.GetString(0));
                var root = document.RootElement;
                if (!root.TryGetProperty("protein_g_per_kg", out var protein))
                    continue;

                profilesFound++;
                if (profilesFound > 1) return null;

                profile = new NutritionProfile(
                    protein.GetDouble(),
                    root.TryGetProperty("protein_min_g_per_kg", out var min) ? min.GetDouble() : null,
                    root.TryGetProperty("protein_max_g_per_kg", out var max) ? max.GetDouble() : null);
            }
            catch (JsonException)
            {
                // Una configuración clínica mal formada no debe romper la generación.
            }
        }

        return profile;
    }

    /// <summary>
    /// Recupera indicaciones clínicas informativas asociadas a las especializaciones activas.
    /// Son contexto para el profesional, no prescripciones automáticas.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetClinicalGuidanceAsync(
        int clientId,
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT sr.configuration
            FROM client_specializations cs
            JOIN specializations s ON s.id=cs.specialization_id AND s.active=TRUE
            JOIN tenant_specializations ts
              ON ts.specialization_id=s.id AND ts.tenant_id=cs.tenant_id AND ts.enabled=TRUE
            JOIN specialization_rules sr
              ON sr.specialization_id=s.id AND sr.rule_type='clinical_guidance' AND sr.active=TRUE
            WHERE cs.client_id=@client AND cs.tenant_id=@tenant
            ORDER BY sr.priority ASC, sr.id ASC;";

        Add(command, "client", clientId);
        Add(command, "tenant", tenantId);

        var guidance = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0)) continue;
            try
            {
                using var document = JsonDocument.Parse(reader.GetString(0));
                if (document.RootElement.TryGetProperty("message", out var message))
                {
                    var value = message.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value)) guidance.Add(value);
                }
            }
            catch (JsonException)
            {
                // La guía es informativa: una configuración inválida se omite.
            }
        }

        return guidance;
    }


    /// <summary>
    /// Devuelve la configuración estructurada de una especialización del paciente.
    /// La lectura siempre queda limitada al tenant y a una especialización concreta.
    /// </summary>
    public async Task<JsonElement?> GetClientSpecializationProfileAsync(
        int clientId,
        int tenantId,
        string specializationCode,
        CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT csp.configuration
            FROM client_specialization_profiles csp
            JOIN specializations s ON s.id=csp.specialization_id AND s.active=TRUE
            WHERE csp.client_id=@client
              AND csp.tenant_id=@tenant
              AND s.code=@code
            LIMIT 1;";
        Add(command, "client", clientId);
        Add(command, "tenant", tenantId);
        Add(command, "code", specializationCode);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value == null || value == DBNull.Value) return null;

        try
        {
            using var document = JsonDocument.Parse(Convert.ToString(value) ?? "{}");
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
