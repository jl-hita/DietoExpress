using System;
using System.Collections.Generic;
namespace Anguloso.Server.Models;
public partial class subscription_plans
{
    public int id { get; set; }
    public string code { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string? description { get; set; }
    public decimal monthly_price { get; set; }
    public decimal yearly_price { get; set; }
    public string? stripe_product_id { get; set; }
    public string? stripe_monthly_price_id { get; set; }
    public string? stripe_yearly_price_id { get; set; }
    /// <summary>Price de Stripe para cada puesto profesional adicional.</summary>
    public string? stripe_additional_monthly_price_id { get; set; }
    public string? stripe_additional_yearly_price_id { get; set; }
    public int? max_nutritionists { get; set; }
    public int? max_clients_per_nutritionist { get; set; }
    public int? max_total_clients { get; set; }
    public int? trial_days { get; set; }
    public bool active { get; set; } = true;
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public virtual ICollection<subscription_plan_features> features { get; set; } = new List<subscription_plan_features>();
    public virtual ICollection<subscriptions> subscriptions { get; set; } = new List<subscriptions>();
}