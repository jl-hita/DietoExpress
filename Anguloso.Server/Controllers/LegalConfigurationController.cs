using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Gestiona los datos que personalizan los documentos legales sin mezclar esos
/// datos con los textos jurídicos versionados.
/// </summary>
[ApiController]
[Route("api/legal-configuration")]
[Authorize(Policy = "Professional")]
public sealed class LegalConfigurationController : ControllerBase
{
    private static readonly string[] ProfessionalKeys =
    [
        "legal_name", "tax_id", "address", "contact_email", "contact_phone",
        "privacy_email", "dpo_email", "website", "professional_title",
        "professional_college", "professional_collegiate_number",
        "professional_title_country", "patient_privacy_legal_basis",
        "patient_recipients_summary", "patient_retention_summary",
        "privacy_policy_url", "consultation_description", "consultation_limits",
        "service_prices_summary", "booking_payment_summary",
        "appointment_cancellation_summary", "refund_summary", "no_show_summary",
        "dpa_duration", "dpa_end_of_service_summary", "patient_additional_purposes",
        "patient_special_categories_summary", "consultation_risks"
    ];

    private readonly IConfiguration _configuration;

    public LegalConfigurationController(IConfiguration configuration) => _configuration = configuration;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var scope = GetScope();
        if (scope == null) return Unauthorized();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var values = await ReadValues(connection, scope.Value.type, scope.Value.id, cancellationToken);
        return Ok(ProfessionalKeys.Select(key => new LegalSettingDto(key, values.GetValueOrDefault(key, string.Empty))));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] LegalSettingsRequest request, CancellationToken cancellationToken)
    {
        var scope = GetScope();
        if (scope == null) return Unauthorized();
        if (request?.Values == null) return BadRequest("Configuración legal no válida.");

        var allowed = ProfessionalKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (request.Values.Keys.Any(key => !allowed.Contains(key)))
            return BadRequest("La solicitud contiene campos legales no permitidos.");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var item in request.Values)
        {
            var value = item.Value?.Trim() ?? string.Empty;
            if (value.Length > 10000) return BadRequest($"El campo {item.Key} es demasiado largo.");

            await using var command = new NpgsqlCommand("""
                INSERT INTO legal_configuration (scope_type, scope_id, setting_key, setting_value, updated_at)
                VALUES (@scope,@scopeId,@key,@value,NOW())
                ON CONFLICT (scope_type, scope_id, setting_key)
                DO UPDATE SET setting_value=EXCLUDED.setting_value, updated_at=NOW();
                """, connection, transaction);
            command.Parameters.AddWithValue("scope", scope.Value.type);
            command.Parameters.AddWithValue("scopeId", scope.Value.id);
            command.Parameters.AddWithValue("key", item.Key.Trim().ToLowerInvariant());
            command.Parameters.AddWithValue("value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Ok();
    }

    private (string type, int id)? GetScope()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (!userId.HasValue) return null;

        var tenantId = AuthHelpers.GetTenantId(User);
        if (User.IsInRole("clinic_admin") && tenantId.HasValue)
            return ("tenant", tenantId.Value);

        return ("user", userId.Value);
    }

    private async Task<Dictionary<string, string>> ReadValues(
        NpgsqlConnection connection, string scopeType, int scopeId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT setting_key, setting_value
            FROM legal_configuration
            WHERE scope_type=@scope AND scope_id=@scopeId;
            """, connection);
        command.Parameters.AddWithValue("scope", scopeType);
        command.Parameters.AddWithValue("scopeId", scopeId);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
}

public sealed record LegalSettingDto(string Key, string Value);

public sealed record LegalSettingsRequest(Dictionary<string, string> Values);
