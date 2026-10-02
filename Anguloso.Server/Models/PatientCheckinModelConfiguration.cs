using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Models;

public partial class angulosodbContext
{
    partial void ConfigurePatientCheckins(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<patient_checkins>(entity =>
        {
            entity.HasKey(e => e.id).HasName("patient_checkins_pkey");
            entity.HasIndex(e => new { e.client_id, e.week_start }, "patient_checkins_client_week_key").IsUnique();
            entity.Property(e => e.week_start).HasColumnType("date");
            entity.Property(e => e.submitted_at).HasDefaultValueSql("now()");
            entity.Property(e => e.difficulties).HasColumnType("text");
            entity.Property(e => e.notes).HasColumnType("text");
            entity.HasOne(e => e.client).WithMany().HasForeignKey(e => e.client_id).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.tenant).WithMany().HasForeignKey(e => e.tenant_id).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
