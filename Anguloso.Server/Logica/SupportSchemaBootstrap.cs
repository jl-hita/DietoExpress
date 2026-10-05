using Microsoft.EntityFrameworkCore;
using Anguloso.Server.Models;

namespace Anguloso.Server.Logica;

public static class SupportSchemaBootstrap
{
    public static void Initialize(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw("""
CREATE TABLE IF NOT EXISTS support_tickets (
    id BIGSERIAL PRIMARY KEY, tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    created_by_user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    assigned_to_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    subject VARCHAR(200) NOT NULL, category VARCHAR(40) NOT NULL,
    priority VARCHAR(20) NOT NULL DEFAULT 'normal', status VARCHAR(30) NOT NULL DEFAULT 'open',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), closed_at TIMESTAMPTZ,
    CONSTRAINT support_tickets_category_check CHECK (category IN ('problem','billing','account','question','suggestion','other')),
    CONSTRAINT support_tickets_priority_check CHECK (priority IN ('low','normal','high','urgent')),
    CONSTRAINT support_tickets_status_check CHECK (status IN ('open','in_progress','waiting_user','resolved','closed'))
);
CREATE INDEX IF NOT EXISTS idx_support_tickets_tenant_updated ON support_tickets(tenant_id,updated_at DESC,id DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_created_by ON support_tickets(created_by_user_id,updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_status ON support_tickets(status,updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_support_tickets_assigned ON support_tickets(assigned_to_user_id,status,updated_at DESC);

CREATE TABLE IF NOT EXISTS support_messages (
    id BIGSERIAL PRIMARY KEY, ticket_id BIGINT NOT NULL REFERENCES support_tickets(id) ON DELETE CASCADE,
    author_user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    body TEXT NOT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), is_internal BOOLEAN NOT NULL DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS idx_support_messages_ticket_created ON support_messages(ticket_id,created_at,id);
CREATE INDEX IF NOT EXISTS idx_support_messages_author_created ON support_messages(author_user_id,created_at DESC);

CREATE TABLE IF NOT EXISTS support_notifications (
    id BIGSERIAL PRIMARY KEY,
    recipient_user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    ticket_id BIGINT NOT NULL REFERENCES support_tickets(id) ON DELETE CASCADE,
    type VARCHAR(40) NOT NULL, title VARCHAR(200) NOT NULL, message TEXT NOT NULL,
    action_url VARCHAR(500), idempotency_key VARCHAR(200) NOT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), read_at TIMESTAMPTZ,
    CONSTRAINT uq_support_notifications_key UNIQUE(recipient_user_id,idempotency_key)
);
CREATE INDEX IF NOT EXISTS idx_support_notifications_user_unread ON support_notifications(recipient_user_id,read_at,created_at DESC);

CREATE TABLE IF NOT EXISTS support_ticket_audit (
    id BIGSERIAL PRIMARY KEY, ticket_id BIGINT NOT NULL REFERENCES support_tickets(id) ON DELETE CASCADE,
    actor_user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    action VARCHAR(50) NOT NULL, old_value TEXT, new_value TEXT, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_support_ticket_audit_ticket ON support_ticket_audit(ticket_id,created_at,id);
""");
        logger.LogInformation("Esquema de soporte SuperAdmin verificado correctamente.");
    }
}