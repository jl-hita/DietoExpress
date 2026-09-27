using System;

namespace Anguloso.Server.Models;

public partial class payment_events
{
    public long id { get; set; }
    public string provider { get; set; } = "stripe";
    public string event_id { get; set; } = string.Empty;
    public string event_type { get; set; } = string.Empty;
    public DateTime received_at { get; set; } = DateTime.UtcNow;
    public DateTime? processed_at { get; set; }
    public string? payload { get; set; }
    public string status { get; set; } = "received";
    public string? error { get; set; }
}
