#nullable disable
using System;
using System.Collections.Generic;

namespace Anguloso.Server.Models;

public partial class food_preferences
{
    public int id { get; set; }

    public int client_id { get; set; }

    public string preferred_foods { get; set; }

    public string disliked_foods { get; set; }

    public string allergies { get; set; }

    public virtual clients client { get; set; }
}
