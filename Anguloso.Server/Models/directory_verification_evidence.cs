namespace Anguloso.Server.Models;

/// <summary>Evidence records are stored outside the public web root and reviewed by SuperAdmin.</summary>
public partial class directory_verification_evidence
{
    public long id { get; set; }
    public int user_id { get; set; }
    public string evidence_type { get; set; } = string.Empty;
    public string original_file_name { get; set; } = string.Empty;
    public string mime_type { get; set; } = string.Empty;
    public long file_size { get; set; }
    public string storage_key { get; set; } = string.Empty;
    public string sha256 { get; set; } = string.Empty;
    public string status { get; set; } = "pending";
    public string? review_note { get; set; }
    public DateTime uploaded_at { get; set; }
    public DateTime? reviewed_at { get; set; }
    public int? reviewed_by_user_id { get; set; }
    public DateTime? revoked_at { get; set; }
}