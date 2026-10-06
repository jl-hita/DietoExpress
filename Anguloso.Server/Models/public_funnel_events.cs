using System;

namespace Anguloso.Server.Models;

public partial class public_funnel_events
{
    public long id { get; set; }
    public string event_name { get; set; } = string.Empty;
    public string? professional_slug { get; set; }
    public int? appointment_id { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;
}
