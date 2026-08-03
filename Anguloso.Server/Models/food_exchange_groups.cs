#nullable disable
using System;
using System.Collections.Generic;

namespace Anguloso.Server.Models;

public partial class food_exchange_groups
{
    public int id { get; set; }

    public string name { get; set; }

    public decimal kcal { get; set; }

    public decimal protein { get; set; }

    public decimal carbs { get; set; }

    public decimal fat { get; set; }

    public virtual ICollection<foods> foods { get; set; } = new List<foods>();

    public virtual ICollection<meal_items> meal_items { get; set; } = new List<meal_items>();
}
