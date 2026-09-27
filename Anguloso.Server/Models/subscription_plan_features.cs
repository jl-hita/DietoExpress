namespace Anguloso.Server.Models;
public partial class subscription_plan_features
{
    public int id { get; set; }
    public int plan_id { get; set; }
    public string feature_code { get; set; } = string.Empty;
    public bool enabled { get; set; } = true;
    public virtual subscription_plans plan { get; set; } = null!;
}