using System;

namespace Anguloso.Server.Models;

public partial class invoice_lines
{
    public long id { get; set; }
    public long invoice_id { get; set; }
    public int line_number { get; set; }
    public string description { get; set; } = string.Empty;
    public decimal quantity { get; set; } = 1;
    public decimal unit_price { get; set; }
    public decimal net_amount { get; set; }
    public decimal tax_rate { get; set; }
    public decimal tax_amount { get; set; }
    public decimal total_amount { get; set; }
    public virtual invoices invoice { get; set; } = null!;
}
