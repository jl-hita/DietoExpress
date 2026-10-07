using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Añade el estado de publicación/verificación del directorio sin depender de migraciones EF generadas.
/// </summary>
public static class PublicDirectoryVerificationSchema
{
    public static void Initialize(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE users
                ADD COLUMN IF NOT EXISTS directory_publication_status VARCHAR(20) NOT NULL DEFAULT 'draft',
                ADD COLUMN IF NOT EXISTS directory_verified_at TIMESTAMPTZ NULL,
                ADD COLUMN IF NOT EXISTS directory_verified_by_user_id INTEGER NULL REFERENCES users(id) ON DELETE SET NULL,
                ADD COLUMN IF NOT EXISTS directory_verification_note VARCHAR(2000) NULL;

            UPDATE users
            SET directory_publication_status = CASE
                WHEN directory_enabled = TRUE THEN 'pending'
                ELSE 'draft'
            END
            WHERE directory_publication_status IS NULL OR directory_publication_status = '';

            CREATE INDEX IF NOT EXISTS idx_users_directory_publication
                ON users(directory_publication_status, directory_enabled);

            ALTER TABLE users
                DROP CONSTRAINT IF EXISTS users_directory_publication_status_check;

            ALTER TABLE users
                ADD CONSTRAINT users_directory_publication_status_check
                CHECK (directory_publication_status IN ('draft','pending','verified','published','rejected'));
        ");

        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS directory_verification_evidence (
                id BIGSERIAL PRIMARY KEY,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                evidence_type VARCHAR(30) NOT NULL,
                original_file_name VARCHAR(255) NOT NULL,
                mime_type VARCHAR(100) NOT NULL,
                file_size BIGINT NOT NULL CHECK (file_size > 0 AND file_size <= 20971520),
                storage_key VARCHAR(500) NOT NULL UNIQUE,
                sha256 VARCHAR(64) NOT NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'pending',
                review_note VARCHAR(2000),
                uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                reviewed_at TIMESTAMPTZ,
                reviewed_by_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
                revoked_at TIMESTAMPTZ,
                CONSTRAINT directory_verification_evidence_type_check
                    CHECK (evidence_type IN ('identity','qualification')),
                CONSTRAINT directory_verification_evidence_status_check
                    CHECK (status IN ('pending','approved','rejected'))
            );

            CREATE INDEX IF NOT EXISTS idx_directory_verification_evidence_user
                ON directory_verification_evidence(user_id, evidence_type, uploaded_at DESC);

            CREATE INDEX IF NOT EXISTS idx_directory_verification_evidence_status
                ON directory_verification_evidence(status, uploaded_at);
        ");

        logger.LogInformation("Esquema de verificación/publicación del directorio inicializado.");
    }
}