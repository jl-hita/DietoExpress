using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Models;

public partial class angulosodbContext
{
    public virtual DbSet<google_calendar_connections> google_calendar_connections { get; set; }
    public virtual DbSet<google_calendar_oauth_states> google_calendar_oauth_states { get; set; }
    public virtual DbSet<external_calendar_events> external_calendar_events { get; set; }
}
