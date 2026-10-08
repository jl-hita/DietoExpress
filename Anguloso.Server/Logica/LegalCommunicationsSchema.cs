using Npgsql;

namespace Anguloso.Server.Logica;

public static class LegalCommunicationsSchema
{
    public static void Initialize(NpgsqlConnection connection)
    {
        using var command = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS patient_commercial_communication_preferences (
                client_id INTEGER PRIMARY KEY REFERENCES clients(id) ON DELETE CASCADE,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                email_enabled BOOLEAN NOT NULL DEFAULT FALSE,
                unsubscribe_token_hash VARCHAR(128),
                unsubscribed_at TIMESTAMPTZ,
                consented_at TIMESTAMPTZ,
                consent_version VARCHAR(128),
                consent_source VARCHAR(128),
                revoked_at TIMESTAMPTZ,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            ALTER TABLE patient_commercial_communication_preferences ADD COLUMN IF NOT EXISTS consented_at TIMESTAMPTZ;
            ALTER TABLE patient_commercial_communication_preferences ADD COLUMN IF NOT EXISTS consent_version VARCHAR(128);
            ALTER TABLE patient_commercial_communication_preferences ADD COLUMN IF NOT EXISTS consent_source VARCHAR(128);
            ALTER TABLE patient_commercial_communication_preferences ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMPTZ;

            CREATE INDEX IF NOT EXISTS idx_patient_commercial_preferences_tenant
                ON patient_commercial_communication_preferences(tenant_id);
            CREATE UNIQUE INDEX IF NOT EXISTS uq_patient_commercial_unsubscribe_token
                ON patient_commercial_communication_preferences(unsubscribe_token_hash)
                WHERE unsubscribe_token_hash IS NOT NULL;
            """, connection);
        command.ExecuteNonQuery();
    }
}
