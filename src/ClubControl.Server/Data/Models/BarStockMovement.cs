using System;

namespace ClubControl.Server.Data.Models;

public enum BarStockMovementType
{
    Initial,
    Sale,
    Void,
    Adjust
}

/// <summary>One change of a product's stock, kept forever so every number can be explained.</summary>
public class BarStockMovement
{
    public int Id { get; set; }

    public int BarProductId { get; set; }
    public BarProduct? BarProduct { get; set; }

    public BarStockMovementType Type { get; set; }
    public int Delta { get; set; }
    public int StockAfter { get; set; }

    /// <summary>Why a manual adjustment was made: restock, recount, damaged or other. Empty for other types.</summary>
    public string Reason { get; set; } = "";
    public string Note { get; set; } = "";

    public int? BarSaleId { get; set; }
    public BarSale? BarSale { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
