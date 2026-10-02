using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Models;

public partial class angulosodbContext
{
    public virtual DbSet<nutritionist_availability> nutritionist_availability { get; set; }
    public virtual DbSet<patient_appointments> patient_appointments { get; set; }
}
