using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Crea y actualiza el esquema persistente del módulo de soporte.
/// Se mantiene separado del bootstrap general para que la evolución del ticketing
/// no obligue a modificar el modelo EF generado.
/// </summary>
public static class SupportSchemaBootstrap
{
    public static void Initialize(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw("""
CREATE TABLE IF NOT EXISTS support_tickets (
    id BIGSERIAL PRIMARY KEY,
    tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    created_by_user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    assigned_to_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    subject VARCHAR(200) NOT NULL,
    category VARCHAR(40) NOT NULL,
    priority VARCHAR(20) NOT NULL DEFAULT 'normal',
    status VARCHAR(30) NOT NULL DEFAULT 'open',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    closed_at TIMESTAMPTZ,
    CONSTRAINT support_tickets_category_check CHECK (category IN ('problem','billing','account','question','suggestion','other')),
    CONSTRAINT support_tickets_priority_check CHECK (priority IN ('low','normal','high','urgent')),
    CONSTRAINT support_tickets_status_check CHECK (status IN ('open','in_progress','waiting_user','resolved','closed'))
);

CREATE INDEX IF NOT EXISTS idx_support_tickets_tenant_updated
    ON support_tickets(tenant_id, updated_at DESC, id DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_created_by
    ON support_tickets(created_by_user_id, updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_status
    ON support_tickets(status, updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_assigned
    ON support_tickets(assigned_to_user_id, status, updated_at DESC);

CREATE TABLE IF NOT EXISTS support_messages (
    id BIGSERIAL PRIMARY KEY,
    ticket_id BIGINT NOT NULL REFERENCES support_tickets(id) ON DELETE CASCADE,
    author_user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    body TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_internal BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS idx_support_messages_ticket_created
    ON support_messages(ticket_id, created_at, id);
CREATE INDEX IF NOT EXISTS idx_support_messages_author_created
    ON support_messages(author_user_id, created_at DESC);
""");

        logger.LogInformation("Esquema de soporte SuperAdmin verificado correctamente.");
    }
}
