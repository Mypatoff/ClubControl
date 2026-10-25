namespace ClubControl.Shared;

/// <summary>
/// Pure billing math shared by the server's session logic and its tests (SPEC section 5).
/// No database, no clock reads except where a DateTime is passed in - every rule here is a plain
/// function of its inputs so it can be unit-tested directly.
/// </summary>
public static class BillingCalculator
{
    public const int RoundingUzs = 100;

    /// <summary>Every charge is rounded UP to the nearest 100 UZS.</summary>
    public static int RoundUpToHundred(int amountUzs)
    {
        if (amountUzs <= 0)
            return 0;

        return (amountUzs + RoundingUzs - 1) / RoundingUzs * RoundingUzs;
    }

    /// <summary>A session is charged per started minute: any part of a minute counts as a full minute.</summary>
    public static int StartedMinutes(int elapsedSeconds)
    {
        if (elapsedSeconds <= 0)
            return 0;

        return (elapsedSeconds + 59) / 60;
    }

    /// <summary>
    /// Price for a number of already-started minutes, rounded up to 100 UZS. Integer-only math
    /// (multiply before dividing) so there is no floating-point rounding drift.
    /// </summary>
    public static int PriceForMinutes(int minutes, int pricePerHourUzs)
    {
        if (minutes <= 0 || pricePerHourUzs <= 0)
            return 0;

        // price = minutes * pricePerHourUzs / 60, then round up to the nearest 100:
        // ceil(price / 100) * 100 == ceil(minutes * pricePerHourUzs / 6000) * 100.
        long totalUzsTimes60 = (long)minutes * pricePerHourUzs;
        long roundedUnits = (totalUzsTimes60 + 5_999) / 6_000;
        return (int)(roundedUnits * 100);
    }

    /// <summary>How many whole minutes a prepay payment buys at a given price. Rounds down - no partial minutes.</summary>
    public static int MinutesForMoney(int moneyUzs, int pricePerHourUzs)
    {
        if (moneyUzs <= 0 || pricePerHourUzs <= 0)
            return 0;

        return (int)((long)moneyUzs * 60 / pricePerHourUzs);
    }

    /// <summary>A prepay payment (start or total after extend) must stay within the tariff's min/max.</summary>
    public static bool IsValidPrepayPayment(int amountUzs, int minPaymentUzs, int maxPaymentUzs) =>
        amountUzs >= minPaymentUzs && amountUzs <= maxPaymentUzs;

    /// <summary>
    /// An extend (Доплата) must itself add a positive amount of both money and time: a top-up too
    /// small to buy even one extra minute at the current price must be rejected rather than
    /// silently charging money for zero added time, and a non-positive minute count must never be
    /// allowed to shrink the session's planned end time.
    /// </summary>
    public static bool IsValidExtendIncrement(int addMoneyUzs, int addMinutes) =>
        addMoneyUzs > 0 && addMinutes > 0;

    /// <summary>
    /// Postpay cost for elapsed time so far: per-started-minute charge, never below the tariff minimum.
    /// Does not cap at the maximum - call <see cref="HasReachedLimit"/> to decide that separately.
    /// </summary>
    public static int PostpayCost(int elapsedSeconds, int pricePerHourUzs, int minPaymentUzs)
    {
        var raw = PriceForMinutes(StartedMinutes(elapsedSeconds), pricePerHourUzs);
        return Math.Max(raw, minPaymentUzs);
    }

    /// <summary>A postpay session reaches "Limit reached" once its cost reaches the tariff maximum.</summary>
    public static bool HasReachedLimit(int costUzs, int maxPaymentUzs) =>
        maxPaymentUzs > 0 && costUzs >= maxPaymentUzs;

    /// <summary>
    /// Move (prepay): the money still owed for the remaining time at the old price is recalculated
    /// into minutes at the new PC's price.
    /// </summary>
    public static int RecalculateMinutesForMove(int remainingSeconds, int oldPricePerHourUzs, int newPricePerHourUzs)
    {
        if (remainingSeconds <= 0 || oldPricePerHourUzs <= 0 || newPricePerHourUzs <= 0)
            return 0;

        long remainingMoneyUzs = (long)remainingSeconds * oldPricePerHourUzs / 3600;
        return MinutesForMoney((int)remainingMoneyUzs, newPricePerHourUzs);
    }

    /// <summary>
    /// Move (postpay/free): cost already run up is kept and counting continues at the new price.
    /// Each segment (one PC-and-price slice of the session) is charged on its own, then the pieces
    /// are added together - this is the "kept" part, since a closed segment's charge never changes.
    /// </summary>
    public static int SumPostpaySegments(IEnumerable<(int ElapsedSeconds, int PricePerHourUzs)> segments)
    {
        long total = 0;
        foreach (var segment in segments)
            total += PriceForMinutes(StartedMinutes(segment.ElapsedSeconds), segment.PricePerHourUzs);

        return (int)total;
    }

    /// <summary>
    /// Power-cut restore (prepay): a session whose planned end time has already passed while the
    /// server was down must be closed at that planned end time, not at "now".
    /// </summary>
    public static bool IsPrepayExpired(DateTime plannedEndUtc, DateTime nowUtc) => nowUtc >= plannedEndUtc;
}
