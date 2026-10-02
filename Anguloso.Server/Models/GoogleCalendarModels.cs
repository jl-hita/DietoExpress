namespace Anguloso.Server.Models;

public partial class google_calendar_connections
{
    public int id { get; set; }
    public int tenant_id { get; set; }
    public int user_id { get; set; }
    public string google_account_email { get; set; } = "";
    public string calendar_id { get; set; } = "primary";
    public string access_token_encrypted { get; set; } = "";
    public string? refresh_token_encrypted { get; set; }
    public DateTime access_token_expires_at { get; set; }
    public string? sync_token { get; set; }
    public DateTime? last_synced_at { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public DateTime updated_at { get; set; } = DateTime.UtcNow;
}

public partial class google_calendar_oauth_states
{
    public int id { get; set; }
    public int user_id { get; set; }
    public string state_hash { get; set; } = "";
    public DateTime expires_at { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;
}

public partial class external_calendar_events
{
    public int id { get; set; }
    public int tenant_id { get; set; }
    public int user_id { get; set; }
    public string provider { get; set; } = "google";
    public string external_event_id { get; set; } = "";
    public string? etag { get; set; }
    public string title { get; set; } = "";
    public DateTime starts_at { get; set; }
    public DateTime ends_at { get; set; }
    public bool is_all_day { get; set; }
    public DateTime updated_at { get; set; } = DateTime.UtcNow;
}
