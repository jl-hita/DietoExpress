using System;

namespace Anguloso.Server.Models;

public partial class subscription_payments
{
    public long id { get; set; }
    public int subscription_id { get; set; }
    public string provider { get; set; } = "stripe";
    public string provider_payment_id { get; set; } = string.Empty;
    public string? provider_invoice_id { get; set; }
    public string status { get; set; } = "pending";
    public decimal amount { get; set; }
    public string currency { get; set; } = "eur";
    public decimal? tax_amount { get; set; }
    public DateTime? paid_at { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public string? failure_code { get; set; }
    public string? failure_message { get; set; }
    public string? invoice_number { get; set; }
    public string? invoice_url { get; set; }
    public virtual subscriptions subscription { get; set; } = null!;
}
