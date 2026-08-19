#nullable disable
using System;
using System.Collections.Generic;

namespace Anguloso.Server.Models;

public partial class lifestyle_history
{
    public int id { get; set; }

    public int client_id { get; set; }

    public string work_schedule { get; set; }

    public string sleep_habits { get; set; }

    public string water_consumption { get; set; }

    public string alcohol_consumption { get; set; }

    public string tobacco_consumption { get; set; }

    public virtual clients client { get; set; }
}
