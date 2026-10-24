using ClubControl.Shared;

namespace ClubControl.Server.Data.Models;

/// <summary>One tariff per PC class. Prices are in whole UZS.</summary>
public class Tariff
{
    public int Id { get; set; }
    public PcClass Class { get; set; }

    public int PricePerHourUzs { get; set; }
    public int MinPaymentUzs { get; set; }
    public int MaxPaymentUzs { get; set; }
}
