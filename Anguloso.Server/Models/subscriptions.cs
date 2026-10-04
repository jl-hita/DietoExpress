using System;
namespace Anguloso.Server.Models;
#pragma warning disable CS8981 // El nombre de tipo solo contiene caracteres ASCII en minúsculas. Estos nombres pueden reservarse para el idioma.
public partial class subscriptions
#pragma warning restore CS8981 // El nombre de tipo solo contiene caracteres ASCII en minúsculas. Estos nombres pueden reservarse para el idioma.
{
    public int id { get; set; }
    public int tenant_id { get; set; }
    public int plan_id { get; set; }
    /// <summary>Plazas profesionales contratadas actualmente. Para clinic_full incluye las plazas base más las adicionales.</summary>
    public int? contracted_nutritionists { get; set; }
    public string status { get; set; } = "active";
    public DateTime started_at { get; set; } = DateTime.UtcNow;
    public DateTime? expires_at { get; set; }
    public DateTime? cancelled_at { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public string? billing_interval { get; set; }
    public decimal? amount { get; set; }
    public string currency { get; set; } = "eur";
    public string? payment_provider { get; set; }
    public string? provider_customer_id { get; set; }
    public string? provider_subscription_id { get; set; }
    public DateTime? current_period_start { get; set; }
    public DateTime? current_period_end { get; set; }
    public DateTime? trial_end { get; set; }
    public bool cancel_at_period_end { get; set; }
    public DateTime updated_at { get; set; } = DateTime.UtcNow;
    public DateTime? last_stripe_event_created_at { get; set; }
    public string? last_stripe_event_id { get; set; }
    public virtual tenants tenant { get; set; } = null!;
    public virtual subscription_plans plan { get; set; } = null!;
}