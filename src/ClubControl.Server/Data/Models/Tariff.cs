using ClubControl.Shared;

namespace ClubControl.Server.Data.Models;

/// <summary>
/// A named price plan the admin creates/edits freely (SPEC extension: not just a fixed
/// Standard/VIP pair) - prices are in whole UZS. Each tariff belongs to one resource kind, so a
/// Computer and an Other (named) resource pick from separate lists.
/// </summary>
public class Tariff
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
    public ResourceKind Kind { get; set; }

    public int PricePerHourUzs { get; set; }
    public int MinPaymentUzs { get; set; }
    public int MaxPaymentUzs { get; set; }
}
