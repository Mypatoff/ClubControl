using System;

namespace ClubControl.Server.Data.Models;

/// <summary>
/// One bar sale: some units of a product, plus optionally some cups (stakan) at the cup price.
/// The name and prices are copied here, so a later price change never rewrites old sales.
/// </summary>
public class BarSale
{
    public int Id { get; set; }

    public int BarProductId { get; set; }
    public BarProduct? BarProduct { get; set; }

    public string ProductName { get; set; } = "";
    public int UnitPriceUzs { get; set; }
    public int Quantity { get; set; }
    public int Cups { get; set; }
    public int CupPriceUzs { get; set; }
    public int TotalUzs { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? VoidedUtc { get; set; }

    /// <summary>
    /// Set when the sale was added to a running PC session and is part of that session's bill.
    /// An unpaid sale made at a PC has no extra: it is not in the bill, only in the unpaid list.
    /// </summary>
    public int? SessionExtraId { get; set; }
    public SessionExtra? SessionExtra { get; set; }

    /// <summary>The session and PC the sale was made at ("PC 5"), for the record. Empty/null for counter sales.</summary>
    public int? SessionId { get; set; }
    public int? PcId { get; set; }
    public string PcLabel { get; set; } = "";

    /// <summary>Sold on credit: nobody paid yet. Stays true after it is settled; <see cref="PaidUtc"/> says when.</summary>
    public bool IsUnpaid { get; set; }
    public string UnpaidComment { get; set; } = "";
    public DateTime? PaidUtc { get; set; }

    /// <summary>The working shift (smena) that was open when the sale was made; the bar report is kept per shift. Null only for imported history.</summary>
    public int? ShiftId { get; set; }

    /// <summary>For a sale on credit: the shift that was open when it was paid, which is the shift its money counts for.</summary>
    public int? PaidShiftId { get; set; }

    public bool IsVoided => VoidedUtc.HasValue;

    /// <summary>On credit, not settled and not cancelled: what the unpaid tracker counts.</summary>
    public bool IsOutstanding => IsUnpaid && !PaidUtc.HasValue && !VoidedUtc.HasValue;
}
