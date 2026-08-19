#nullable disable
using System;
using System.Collections.Generic;

namespace Anguloso.Server.Models;

public partial class medical_history
{
    public int id { get; set; }

    public int client_id { get; set; }

    public bool diabetes { get; set; }

    public bool hypertension { get; set; }

    public bool hypothyroidism { get; set; }

    public string surgeries { get; set; }

    public string routine_medication { get; set; }

    public string other_pathologies { get; set; }

    public virtual clients client { get; set; }
}
