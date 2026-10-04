using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Models;

public partial class angulosodbContext
{
    // Tabla operativa, gestionada por bootstrap y consultada mediante SQL para que el aviso siga disponible
    // incluso cuando otra migración de esquema haya fallado durante el arranque.
    public virtual DbSet<system_alerts> system_alerts { get; set; }
}
