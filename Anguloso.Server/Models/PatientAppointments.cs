using System;

namespace Anguloso.Server.Models;

public partial class patient_appointments
{
    public int id { get; set; }
    public int tenant_id { get; set; }
    public int client_id { get; set; }
    public int nutritionist_id { get; set; }
    public DateTime starts_at { get; set; }
    public DateTime ends_at { get; set; }
    public string status { get; set; } = "requested";
    public string? patient_notes { get; set; }
    public string? professional_notes { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public DateTime updated_at { get; set; } = DateTime.UtcNow;

    public virtual tenants tenant { get; set; } = null!;
    public virtual clients client { get; set; } = null!;
    public virtual users nutritionist { get; set; } = null!;
}
