namespace ClubControl.Shared.Dto;

/// <summary>What the lock screen shows. PC number/class/price are blank until the admin configures the PC.</summary>
public class LockInfo
{
    public bool LimitReached { get; set; }

    public int? PcNumber { get; set; }
    public PcClass? Class { get; set; }
    public int PricePerHourUzs { get; set; }
}
