namespace ClubControl.Shared.Dto;

/// <summary>What the lock screen shows. PC number/tariff/price are blank until the admin configures the PC.</summary>
public class LockInfo
{
    public bool LimitReached { get; set; }

    public int? PcNumber { get; set; }
    public string? TariffName { get; set; }
    public int PricePerHourUzs { get; set; }
}
