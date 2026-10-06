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

        logger.LogInformation("Esquema de verificación/publicación del directorio inicializado.");
    }
}