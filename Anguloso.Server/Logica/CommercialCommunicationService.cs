using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Anguloso.Server.Logica;

public sealed class CommercialCommunicationService
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;

    public CommercialCommunicationService(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
    }

    public async Task<bool> IsOptedInAsync(int clientId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT email_enabled
            FROM patient_commercial_communication_preferences
            WHERE client_id=@client
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("client", clientId);
        return (bool?)await command.ExecuteScalarAsync(cancellationToken) == true;
    }

    public async Task SetEmailPreferenceAsync(int clientId, int tenantId, bool enabled, string source = "patient_portal", string? consentVersion = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!enabled)
        {
            await using var disable = new NpgsqlCommand("""
                UPDATE patient_commercial_communication_preferences
                SET email_enabled=false, unsubscribed_at=NOW(), revoked_at=NOW(), updated_at=NOW()
                WHERE client_id=@client AND tenant_id=@tenant;
                """, connection, transaction);
            disable.Parameters.AddWithValue("client", clientId);
            disable.Parameters.AddWithValue("tenant", tenantId);
            await disable.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var hash = HashToken(token);
            await using var enable = new NpgsqlCommand("""
                INSERT INTO patient_commercial_communication_preferences
                    (client_id, tenant_id, email_enabled, unsubscribe_token_hash, unsubscribed_at, consented_at, consent_version, consent_source, revoked_at, updated_at)
                VALUES (@client,@tenant,true,@hash,NULL,NOW(),@version,@source,NULL,NOW())
                ON CONFLICT (client_id)
                DO UPDATE SET tenant_id=EXCLUDED.tenant_id,
                              email_enabled=true,
                              unsubscribe_token_hash=EXCLUDED.unsubscribe_token_hash,
                              unsubscribed_at=NULL,
                              updated_at=NOW();
                """, connection, transaction);
            enable.Parameters.AddWithValue("client", clientId);
            enable.Parameters.AddWithValue("tenant", tenantId);
            enable.Parameters.AddWithValue("hash", hash);
            enable.Parameters.AddWithValue("version", (object?)consentVersion ?? "commercial-communications-v1");
            enable.Parameters.AddWithValue("source", source);
            await enable.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string?> CreateUnsubscribeUrlAsync(int clientId, CancellationToken cancellationToken = default)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = HashToken(token);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE patient_commercial_communication_preferences
            SET unsubscribe_token_hash=@hash, updated_at=NOW()
            WHERE client_id=@client AND email_enabled=true;
            """, connection);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("hash", hash);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0) return null;

        var baseUrl = (_configuration["PublicBaseUrl"] ?? _configuration["App:PublicBaseUrl"] ?? "https://jlhitap.duckdns.org").TrimEnd('/');
        return $"{baseUrl}/api/commercial-communications/unsubscribe?token={Uri.EscapeDataString(token)}";
    }

    public async Task<bool> UnsubscribeAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(token);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE patient_commercial_communication_preferences
            SET email_enabled=false, unsubscribed_at=NOW(), revoked_at=NOW(), updated_at=NOW()
            WHERE unsubscribe_token_hash=@hash
            RETURNING client_id;
            """, connection);
        command.Parameters.AddWithValue("hash", hash);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }


    public async Task<string?> GetCurrentConsentVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT setting_value
            FROM legal_configuration
            WHERE scope_type='platform' AND scope_id=1 AND setting_key='document_version'
            LIMIT 1;
            """, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text && !string.IsNullOrWhiteSpace(text) ? text : "commercial-communications-v1";
    }

    public async Task<bool> SendCommercialEmailAsync(
        EmailServ emailServ,
        int clientId,
        string subject,
        string htmlBody,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var unsubscribeUrl = await CreateUnsubscribeUrlAsync(clientId, cancellationToken);
        if (unsubscribeUrl is null || !await IsOptedInAsync(clientId, cancellationToken))
            return false;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT email
            FROM clients
            WHERE id=@client AND archived_at IS NULL AND email IS NOT NULL AND TRIM(email) <> ''
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("client", clientId);
        var email = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var footer = $"""
            <hr>
            <p style="font-size:12px;color:#64748b">
              Recibes este mensaje porque has aceptado comunicaciones comerciales de DietoExpress.
              <a href="{System.Net.WebUtility.HtmlEncode(unsubscribeUrl)}">Darte de baja de comunicaciones comerciales</a>.
              Las comunicaciones asistenciales necesarias no se ven afectadas.
            </p>
            """;
        var result = await emailServ.SendEmailAsync(email, subject, htmlBody + footer, idempotencyKey);
        return result.Exito;
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
