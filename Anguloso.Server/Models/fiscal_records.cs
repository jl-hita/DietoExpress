using System;

namespace Anguloso.Server.Models;

public partial class fiscal_records
{
    public long id { get; set; }
    public long invoice_id { get; set; }
    public string record_type { get; set; } = "alta";
    public string record_version { get; set; } = "1.0";
    public DateTime generated_at { get; set; } = DateTime.UtcNow;
    public string previous_hash { get; set; } = string.Empty;
    public string hash { get; set; } = string.Empty;
    public string? qr_data { get; set; }
    public string submission_status { get; set; } = "pending";
    public DateTime? submitted_at { get; set; }
    public string? external_id { get; set; }
    public string? submission_error { get; set; }
    public string? payload { get; set; }
    public virtual invoices invoice { get; set; } = null!;
}
