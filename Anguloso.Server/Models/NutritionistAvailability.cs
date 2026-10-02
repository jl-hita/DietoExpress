using System;

namespace Anguloso.Server.Models;

public partial class nutritionist_availability
{
    public int id { get; set; }
    public int tenant_id { get; set; }
    public int nutritionist_id { get; set; }
    public int day_of_week { get; set; }
    public TimeOnly start_time { get; set; }
    public TimeOnly end_time { get; set; }
    public int slot_minutes { get; set; } = 30;
    public bool is_active { get; set; } = true;

    public virtual tenants tenant { get; set; } = null!;
    public virtual users nutritionist { get; set; } = null!;
}
