using System;
namespace Anguloso.Server.Models;
public partial class subscription_events
{
    public long id { get; set; }
    public int subscription_id { get; set; }
    public string event_type { get; set; } = string.Empty;
    public int? old_plan_id { get; set; }
    public int? new_plan_id { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public string? details { get; set; }
}