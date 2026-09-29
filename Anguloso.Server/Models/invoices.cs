using System;
using System.Collections.Generic;

namespace Anguloso.Server.Models;

#pragma warning disable CS8981 // El nombre de tipo solo contiene caracteres ASCII en minúsculas. Estos nombres pueden reservarse para el idioma.
public partial class invoices
#pragma warning restore CS8981 // El nombre de tipo solo contiene caracteres ASCII en minúsculas. Estos nombres pueden reservarse para el idioma.
{
    public long id { get; set; }
    public int tenant_id { get; set; }
    public int? subscription_id { get; set; }
    public string series { get; set; } = "A";
    public long number { get; set; }
    public string status { get; set; } = "draft";
    public DateTime issue_date { get; set; } = DateTime.UtcNow;
    public DateTime? operation_date { get; set; }
    public string currency { get; set; } = "EUR";
    public decimal subtotal { get; set; }
    public decimal tax_amount { get; set; }
    public decimal total { get; set; }
    public string? issuer_legal_name { get; set; }
    public string? issuer_tax_id { get; set; }
    public string? customer_legal_name { get; set; }
    public string? customer_tax_id { get; set; }
    public string? customer_email { get; set; }
    public string? tax_treatment { get; set; }
    public string? payment_status { get; set; }
    public string? provider_invoice_id { get; set; }
    public string? pdf_url { get; set; }
    public bool is_rectifying { get; set; }
    public long? rectifies_invoice_id { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public DateTime updated_at { get; set; } = DateTime.UtcNow;

    public virtual tenants tenant { get; set; } = null!;
    public virtual ICollection<invoice_lines> lines { get; set; } = new List<invoice_lines>();
}
