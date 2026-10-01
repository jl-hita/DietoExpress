using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Models;

public partial class angulosodbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<billing_checkout_attempts>(entity =>
        {
            entity.Property(e => e.billing_interval).HasMaxLength(20).IsRequired();
            entity.Property(e => e.idempotency_key).HasMaxLength(255).IsRequired();
            entity.Property(e => e.stripe_session_id).HasMaxLength(255);
            entity.Property(e => e.checkout_url).IsRequired(false);
            entity.Property(e => e.status).HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<subscription_plans>(entity =>
        {
            entity.Property(e => e.stripe_product_id).HasMaxLength(255);
            entity.Property(e => e.stripe_monthly_price_id).HasMaxLength(255);
            entity.Property(e => e.stripe_yearly_price_id).HasMaxLength(255);
        });

        modelBuilder.Entity<subscriptions>(entity =>
        {
            entity.Property(e => e.amount).HasPrecision(12, 2);
            entity.Property(e => e.currency).HasMaxLength(10).IsRequired();
            entity.Property(e => e.billing_interval).HasMaxLength(20);
            entity.Property(e => e.payment_provider).HasMaxLength(50);
            entity.Property(e => e.provider_customer_id).HasMaxLength(255);
            entity.Property(e => e.provider_subscription_id).HasMaxLength(255);
        });

        modelBuilder.Entity<subscription_payments>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.amount).HasPrecision(12, 2);
            entity.Property(e => e.tax_amount).HasPrecision(12, 2);
            entity.Property(e => e.provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.provider_payment_id).HasMaxLength(255).IsRequired();
            entity.Property(e => e.status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.currency).HasMaxLength(10).IsRequired();
            entity.HasOne(e => e.subscription).WithMany().HasForeignKey(e => e.subscription_id).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<payment_events>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.HasIndex(e => new { e.provider, e.event_id }).IsUnique();
            entity.Property(e => e.provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.event_id).HasMaxLength(255).IsRequired();
            entity.Property(e => e.event_type).HasMaxLength(150).IsRequired();
            entity.Property(e => e.status).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<invoices>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.HasIndex(e => new { e.series, e.number }).IsUnique();
            entity.Property(e => e.series).HasMaxLength(20).IsRequired();
            entity.Property(e => e.status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.currency).HasMaxLength(10).IsRequired();
            entity.Property(e => e.subtotal).HasPrecision(12, 2);
            entity.Property(e => e.tax_amount).HasPrecision(12, 2);
            entity.Property(e => e.total).HasPrecision(12, 2);
            entity.HasOne(e => e.tenant).WithMany().HasForeignKey(e => e.tenant_id).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.lines).WithOne(e => e.invoice).HasForeignKey(e => e.invoice_id).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<invoice_lines>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.HasIndex(e => new { e.invoice_id, e.line_number }).IsUnique();
            entity.Property(e => e.description).HasMaxLength(500).IsRequired();
            entity.Property(e => e.quantity).HasPrecision(12, 4);
            entity.Property(e => e.unit_price).HasPrecision(12, 2);
            entity.Property(e => e.net_amount).HasPrecision(12, 2);
            entity.Property(e => e.tax_rate).HasPrecision(7, 4);
            entity.Property(e => e.tax_amount).HasPrecision(12, 2);
            entity.Property(e => e.total_amount).HasPrecision(12, 2);
        });

        modelBuilder.Entity<fiscal_records>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.HasIndex(e => new { e.invoice_id, e.record_type }).IsUnique();
            entity.Property(e => e.previous_hash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.hash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.record_type).HasMaxLength(20).IsRequired();
            entity.Property(e => e.record_version).HasMaxLength(20).IsRequired();
            entity.Property(e => e.submission_status).HasMaxLength(50).IsRequired();
            entity.HasOne(e => e.invoice).WithMany().HasForeignKey(e => e.invoice_id).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
