using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Models;

public partial class angulosodbContext
{
    public virtual DbSet<patient_checkins> patient_checkins { get; set; }
}
