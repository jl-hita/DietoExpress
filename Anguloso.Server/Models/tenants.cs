using System;
using System.Collections.Generic;

namespace Anguloso.Server.Models;

#pragma warning disable CS8981 // El nombre de tipo solo contiene caracteres ASCII en minúsculas. Estos nombres pueden reservarse para el idioma.
public partial class tenants
#pragma warning restore CS8981 // El nombre de tipo solo contiene caracteres ASCII en minúsculas. Estos nombres pueden reservarse para el idioma.
{
    public int id { get; set; }

    public string legal_name { get; set; } = string.Empty;

    public string trade_name { get; set; } = string.Empty;

    public string cif_nif { get; set; } = string.Empty;

    public string slug { get; set; } = string.Empty;

    public string status { get; set; } = "active";

    public string? dpo_email { get; set; }

    public string? contact_email { get; set; }

    public string? contact_phone { get; set; }

    public string? address { get; set; }

    public string? logo_url { get; set; }

    public DateTime created_at { get; set; } = DateTime.UtcNow;

    public virtual ICollection<users> users { get; set; } = new List<users>();

    public virtual ICollection<clients> clients { get; set; } = new List<clients>();

    public virtual ICollection<diets> diets { get; set; } = new List<diets>();
}
