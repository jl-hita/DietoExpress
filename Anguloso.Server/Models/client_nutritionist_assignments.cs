using System;
namespace Anguloso.Server.Models;
public partial class client_nutritionist_assignments
{
    public int id { get; set; }
    public int client_id { get; set; }
    public int nutritionist_id { get; set; }
    public DateTime assigned_at { get; set; } = DateTime.UtcNow;
    public int? assigned_by_user_id { get; set; }
    public DateTime? unassigned_at { get; set; }
    public bool is_active { get; set; } = true;
    public virtual clients client { get; set; } = null!;
    public virtual users nutritionist { get; set; } = null!;
    public virtual users? assigned_by_user { get; set; }
}