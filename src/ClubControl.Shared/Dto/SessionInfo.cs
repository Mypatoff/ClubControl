namespace ClubControl.Shared.Dto;

/// <summary>What the session timer window shows. All numbers come from the server; the client never computes money.</summary>
public class SessionInfo
{
    public SessionType Type { get; set; }

    public int RemainingSeconds { get; set; }  // Prepay: counts down
    public int ElapsedSeconds { get; set; }     // Postpay/Free: counts up
    public int CurrentCostUzs { get; set; }     // Postpay/Free: current cost

    public int MaxPaymentUzs { get; set; }      // Postpay/Free: for the 90% warning. 0 = no warning.
}
