namespace Anguloso.Server.Models;

public partial class system_alerts
{
    public long id { get; set; }
    public string severity { get; set; } = string.Empty;
    public string component { get; set; } = string.Empty;
    public string title { get; set; } = string.Empty;
    public string message { get; set; } = string.Empty;
    public string? technical_details { get; set; }
    public DateTime first_seen_at { get; set; }
    public DateTime last_seen_at { get; set; }
    public int occurrences { get; set; }
    public DateTime? resolved_at { get; set; }
    public int? resolved_by_user_id { get; set; }
}
