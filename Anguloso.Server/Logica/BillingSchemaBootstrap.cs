using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anguloso.Server.Logica;

public static class BillingSchemaBootstrap
{
    public static void Initialize(Models.angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS subscription_payments (
                id BIGSERIAL PRIMARY KEY,
                subscription_id INTEGER NOT NULL REFERENCES subscriptions(id) ON DELETE CASCADE,
                provider VARCHAR(50) NOT NULL DEFAULT 'stripe',
                provider_payment_id VARCHAR(255) NOT NULL,
                provider_invoice_id VARCHAR(255),
                status VARCHAR(50) NOT NULL DEFAULT 'pending',
                amount NUMERIC(12,2) NOT NULL,
                currency VARCHAR(10) NOT NULL DEFAULT 'eur',
                tax_amount NUMERIC(12,2),
                paid_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                failure_code VARCHAR(150),
                failure_message TEXT,
                invoice_number VARCHAR(100),
                invoice_url TEXT,
                CONSTRAINT subscription_payments_provider_payment_unique UNIQUE(provider, provider_payment_id)
            );

            CREATE INDEX IF NOT EXISTS idx_subscription_payments_subscription_id
                ON subscription_payments(subscription_id);
            CREATE INDEX IF NOT EXISTS idx_subscription_payments_created_at
                ON subscription_payments(created_at DESC);

            CREATE TABLE IF NOT EXISTS payment_events (
                id BIGSERIAL PRIMARY KEY,
                provider VARCHAR(50) NOT NULL,
                event_id VARCHAR(255) NOT NULL,
                event_type VARCHAR(150) NOT NULL,
                received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                processed_at TIMESTAMPTZ,
                payload TEXT,
                status VARCHAR(50) NOT NULL DEFAULT 'received',
                error TEXT,
                CONSTRAINT payment_events_provider_event_unique UNIQUE(provider, event_id)
            );

            CREATE INDEX IF NOT EXISTS idx_payment_events_received_at
                ON payment_events(received_at DESC);

            CREATE TABLE IF NOT EXISTS invoices (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE RESTRICT,
                subscription_id INTEGER REFERENCES subscriptions(id) ON DELETE SET NULL,
                series VARCHAR(20) NOT NULL DEFAULT 'A',
                number BIGINT NOT NULL,
                status VARCHAR(50) NOT NULL DEFAULT 'draft',
                issue_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                operation_date TIMESTAMPTZ,
                currency VARCHAR(10) NOT NULL DEFAULT 'EUR',
                subtotal NUMERIC(12,2) NOT NULL DEFAULT 0,
                tax_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
                total NUMERIC(12,2) NOT NULL DEFAULT 0,
                issuer_legal_name VARCHAR(200),
                issuer_tax_id VARCHAR(50),
                customer_legal_name VARCHAR(200),
                customer_tax_id VARCHAR(50),
                customer_email VARCHAR(150),
                tax_treatment VARCHAR(100),
                payment_status VARCHAR(50),
                provider_invoice_id VARCHAR(255),
                pdf_url TEXT,
                is_rectifying BOOLEAN NOT NULL DEFAULT FALSE,
                rectifies_invoice_id BIGINT REFERENCES invoices(id) ON DELETE RESTRICT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT invoices_series_number_unique UNIQUE(series, number)
            );

            CREATE INDEX IF NOT EXISTS idx_invoices_tenant_id
                ON invoices(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_invoices_issue_date
                ON invoices(issue_date DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_invoices_provider_invoice
                ON invoices(provider_invoice_id)
                WHERE provider_invoice_id IS NOT NULL;

            CREATE TABLE IF NOT EXISTS invoice_lines (
                id BIGSERIAL PRIMARY KEY,
                invoice_id BIGINT NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
                line_number INTEGER NOT NULL,
                description VARCHAR(500) NOT NULL,
                quantity NUMERIC(12,4) NOT NULL DEFAULT 1,
                unit_price NUMERIC(12,2) NOT NULL,
                net_amount NUMERIC(12,2) NOT NULL,
                tax_rate NUMERIC(7,4) NOT NULL DEFAULT 0,
                tax_amount NUMERIC(12,2) NOT NULL DEFAULT 0,
                total_amount NUMERIC(12,2) NOT NULL,
                CONSTRAINT invoice_lines_invoice_line_unique UNIQUE(invoice_id, line_number)
            );

            CREATE TABLE IF NOT EXISTS fiscal_records (
                id BIGSERIAL PRIMARY KEY,
                invoice_id BIGINT NOT NULL REFERENCES invoices(id) ON DELETE RESTRICT,
                record_type VARCHAR(20) NOT NULL DEFAULT 'alta',
                record_version VARCHAR(20) NOT NULL DEFAULT '1.0',
                generated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                previous_hash VARCHAR(128) NOT NULL,
                hash VARCHAR(128) NOT NULL,
                qr_data TEXT,
                submission_status VARCHAR(50) NOT NULL DEFAULT 'pending',
                submitted_at TIMESTAMPTZ,
                external_id VARCHAR(255),
                submission_error TEXT,
                payload TEXT,
                CONSTRAINT fiscal_records_invoice_type_unique UNIQUE(invoice_id, record_type)
            );

            CREATE INDEX IF NOT EXISTS idx_fiscal_records_generated_at
                ON fiscal_records(generated_at DESC);
            CREATE INDEX IF NOT EXISTS idx_fiscal_records_submission_status
                ON fiscal_records(submission_status);
        ");

        logger.LogInformation("Esquema de billing/fiscal verificado correctamente.");
    }
}
