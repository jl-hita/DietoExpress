using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/admin/legal-configuration")]
[Authorize(Roles = "superadmin")]
public sealed class AdminLegalConfigurationController : ControllerBase
{
    private const string ScopeType = "platform";
    private const int ScopeId = 1;

    private static readonly string[] PlatformKeys =
    [
        "legal_name", "tax_id", "address", "contact_email", "contact_phone",
        "privacy_email", "dpo_email", "website", "registration_information",
        "providers_summary", "international_transfers_summary",
        "retention_policy_reference", "cancellation_policy_summary",
        "support_email", "support_policy_summary", "claims_email",
        "governing_law_summary", "pricing_summary", "billing_terms_summary", "refund_summary", "consumer_withdrawal_summary",
        "non_essential_cookies_summary",
        "cookie_third_parties", "subprocessors_summary", "document_version", "last_update_date", "breach_notification_summary",
        "rat_controller_role", "rat_review_date", "rat_accounts_summary", "rat_billing_summary",
        "rat_patient_summary", "rat_security_summary", "rat_rights_summary",
        "retention_matrix_summary",
        "risk_owner", "risk_date", "risk_version", "risk_scope", "risk_summary", "risk_controls_summary",
        "risk_residual_risk_summary", "risk_dpia_decision", "risk_dpia_justification", "risk_next_review"
    ];

    private readonly IConfiguration _configuration;

    public AdminLegalConfigurationController(IConfiguration configuration) => _configuration = configuration;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var values = await ReadValues(connection, cancellationToken);
        return Ok(PlatformKeys.Select(key => new { key, value = values.GetValueOrDefault(key, string.Empty) }));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] AdminLegalSettingsRequest request, CancellationToken cancellationToken)
    {
        if (request?.Values == null) return BadRequest("Configuración legal no válida.");

        var allowed = PlatformKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            command.Parameters.AddWithValue("scope", ScopeType);
            command.Parameters.AddWithValue("scopeId", ScopeId);
            command.Parameters.AddWithValue("key", item.Key.Trim().ToLowerInvariant());
            command.Parameters.AddWithValue("value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Ok();
    }

    private async Task<Dictionary<string, string>> ReadValues(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT setting_key, setting_value
            FROM legal_configuration
            WHERE scope_type=@scope AND scope_id=@scopeId;
            """, connection);
        command.Parameters.AddWithValue("scope", ScopeType);
        command.Parameters.AddWithValue("scopeId", ScopeId);

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

public sealed record AdminLegalSettingsRequest(Dictionary<string, string> Values);
