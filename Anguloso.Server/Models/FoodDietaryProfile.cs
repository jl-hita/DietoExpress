using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Models;

/// <summary>
/// Perfil dietético curado de TODOS los alimentos del catálogo (apto vegano, apto vegetariano, alérgenos UE-14).
/// Ninguna columna admite null: un alimento sin fila aquí está "pendiente de curar". La curación se versiona en
/// <c>FoodCuration/food-dietary-profiles.json</c> y se aplica en el arranque; las filas con <c>origin = 'manual'</c>
/// nunca se sobrescriben. No modifica la tabla <c>foods</c>.
/// </summary>
public partial class food_dietary_profile
{
    public int food_id { get; set; }
    public bool is_vegan { get; set; }
    public bool is_vegetarian { get; set; }
    /// <summary>Alérgenos UE-14 presentes por la identidad del alimento (gluten, crustaceos, huevo, pescado, cacahuete, soja, lacteos, frutos_cascara, apio, mostaza, sesamo, sulfitos, altramuces, moluscos).</summary>
    public string[] allergens { get; set; } = Array.Empty<string>();
    public string origin { get; set; } = "ai-curated";
    public int profile_version { get; set; }
    public DateTime updated_at { get; set; } = DateTime.UtcNow;

    public virtual foods food { get; set; } = null!;
}

public partial class angulosodbContext
{
    public virtual DbSet<food_dietary_profile> food_dietary_profile { get; set; }

    private static void ConfigureFoodDietaryProfile(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<food_dietary_profile>(entity =>
        {
            entity.ToTable("food_dietary_profile");
            entity.HasKey(e => e.food_id).HasName("food_dietary_profile_pkey");
            entity.Property(e => e.origin).HasMaxLength(20);
            entity.Property(e => e.updated_at).HasDefaultValueSql("now()");
            entity.HasOne(e => e.food).WithMany().HasForeignKey(e => e.food_id).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
