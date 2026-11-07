using System;

namespace ClubControl.Server.Data.Models;

/// <summary>Something the bar sells (a drink, a snack). Archived products are hidden from selling but keep their history.</summary>
public class BarProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int PriceUzs { get; set; }
    public int Stock { get; set; }
    public bool Archived { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
