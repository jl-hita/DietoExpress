using System;

namespace Anguloso.Server.Models;

public partial class food_favorites
{
    public int id { get; set; }
    public int user_id { get; set; }
    public int food_id { get; set; }
    public DateTime created_at { get; set; } = DateTime.UtcNow;

    public virtual users user { get; set; } = null!;
    public virtual foods food { get; set; } = null!;
}
