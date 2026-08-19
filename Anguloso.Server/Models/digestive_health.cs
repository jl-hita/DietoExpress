#nullable disable
using System;
using System.Collections.Generic;

namespace Anguloso.Server.Models;

public partial class digestive_health
{
    public int id { get; set; }

    public int client_id { get; set; }

    public string intestinal_habits { get; set; }

    public bool bloating { get; set; }

    public bool heartburn { get; set; }

    public bool gluten_intolerance { get; set; }

    public bool lactose_intolerance { get; set; }

    public bool fodmaps_intolerance { get; set; }

    public string other_intolerances { get; set; }

    public string notes { get; set; }

    public virtual clients client { get; set; }
}
