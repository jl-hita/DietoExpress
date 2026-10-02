using System;

namespace Anguloso.Server.Models;

public partial class patient_checkins
{
    public int id { get; set; }
    public int client_id { get; set; }
    public int tenant_id { get; set; }
    public DateOnly week_start { get; set; }
    public DateTime submitted_at { get; set; } = DateTime.UtcNow;
    public double? weight { get; set; }
    public int? adherence { get; set; }
    public int? hunger { get; set; }
    public string? difficulties { get; set; }
    public string? notes { get; set; }
    public DateTime? reviewed_at { get; set; }
    public int? reviewed_by_user_id { get; set; }

    public virtual clients client { get; set; } = null!;
    public virtual tenants tenant { get; set; } = null!;
}
