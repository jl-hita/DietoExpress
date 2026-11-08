using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Models;

/// <summary>
/// Capa curada sobre <c>foods</c> para la generación automática de dietas. Guarda el rol culinario,
/// la familia, la popularidad, las porciones y las etiquetas de cada alimento.
/// No modifica los datos importados (BEDCA/USDA/OpenFoodFacts), de modo que una re-sincronización
/// del catálogo no pisa la curación. Las filas con <c>origin = 'manual'</c> nunca las reescribe el seeder.
/// </summary>
public partial class food_culinary_profile
{
    public int food_id { get; set; }
    public string role { get; set; } = "";
    public string family { get; set; } = "";
    public short commonness { get; set; } = 3;
    public string[] meal_slots { get; set; } = Array.Empty<string>();
    public string cooking_state { get; set; } = "ready";
    public double min_grams { get; set; }
    public double max_grams { get; set; }
    public double step_grams { get; set; } = 10;
    public double default_grams { get; set; }
    public string? unit_name { get; set; }
    public double? unit_grams { get; set; }
    public string[] tags { get; set; } = Array.Empty<string>();
    public bool is_staple { get; set; }
    public string? excluded_reason { get; set; }
    public string origin { get; set; } = "auto";
    public int classifier_version { get; set; }
    public DateTime updated_at { get; set; } = DateTime.UtcNow;

    public virtual foods food { get; set; } = null!;
}

public partial class angulosodbContext
{
    public virtual DbSet<food_culinary_profile> food_culinary_profile { get; set; }

    private static void ConfigureFoodCulinaryProfile(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<food_culinary_profile>(entity =>
        {
            entity.ToTable("food_culinary_profile");
            entity.HasKey(e => e.food_id).HasName("food_culinary_profile_pkey");
            entity.Property(e => e.role).HasMaxLength(30);
            entity.Property(e => e.family).HasMaxLength(60);
            entity.Property(e => e.cooking_state).HasMaxLength(20);
            entity.Property(e => e.unit_name).HasMaxLength(40);
            entity.Property(e => e.excluded_reason).HasMaxLength(200);
            entity.Property(e => e.origin).HasMaxLength(20);
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");
            entity.HasOne(e => e.food).WithMany().HasForeignKey(e => e.food_id).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
