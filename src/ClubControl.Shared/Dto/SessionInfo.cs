namespace ClubControl.Shared.Dto;

/// <summary>
/// What the session timer window shows. The numbers come from the server, which stays the source
/// of truth for money: the client only extrapolates from them between updates, and - when the
/// server cannot be reached - to keep the timer/cost running and lock the PC when time or money runs out.
/// </summary>
public class SessionInfo
{
    public SessionType Type { get; set; }

    public int RemainingSeconds { get; set; }  // Prepay: counts down
    public int ElapsedSeconds { get; set; }     // Postpay/Free: counts up
    public int CurrentCostUzs { get; set; }     // Postpay/Free: current cost

    public int MaxPaymentUzs { get; set; }      // Postpay/Free: for the 90% warning and the offline limit lock. 0 = none.

    // Postpay only: lets the client keep the cost growing with BillingCalculator.PostpayCostWithOpenSegment.
    public int ClosedSegmentsCostUzs { get; set; }    // cost of segments already closed (e.g. before a Move)
    public int OpenSegmentElapsedSeconds { get; set; } // how long the current segment has been running
    public int PricePerHourUzs { get; set; }          // price of the current segment
    public int MinPaymentUzs { get; set; }            // the cost never shows below this
}
