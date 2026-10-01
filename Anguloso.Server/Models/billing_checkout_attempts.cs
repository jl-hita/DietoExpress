using System;

namespace Anguloso.Server.Models;

public class billing_checkout_attempts
{
    public long id { get; set; }
    public int tenant_id { get; set; }
    public int plan_id { get; set; }
    public string billing_interval { get; set; } = string.Empty;
    public string idempotency_key { get; set; } = string.Empty;
    public string? stripe_session_id { get; set; }
    public string checkout_url { get; set; } = string.Empty;
    public string status { get; set; } = "pending";
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public DateTime expires_at { get; set; }
    public DateTime? completed_at { get; set; }
    public virtual tenants tenant { get; set; } = null!;
    public virtual subscription_plans plan { get; set; } = null!;
}
