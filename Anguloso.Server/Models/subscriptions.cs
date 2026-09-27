using System;
namespace Anguloso.Server.Models;
public partial class subscriptions
{
    public int id { get; set; }
    public int tenant_id { get; set; }
    public int plan_id { get; set; }
    public string status { get; set; } = "active";
    public DateTime started_at { get; set; } = DateTime.UtcNow;
    public DateTime? expires_at { get; set; }
    public DateTime? cancelled_at { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public virtual tenants tenant { get; set; } = null!;
    public virtual subscription_plans plan { get; set; } = null!;
}