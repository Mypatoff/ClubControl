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
    /// A prepay *start* by money must both stay within the tariff's min/max AND buy at least one
    /// whole minute. These are not the same check: MinutesForMoney rounds down, so a tariff whose
    /// minimum payment is set below the cost of one minute at that price (e.g. MinPaymentUzs lower
    /// than PricePerHourUzs/60) lets a payment pass the min/max range check while still computing
    /// to 0 minutes - charging real money for a session that is already over before it starts.
    /// </summary>
    public static bool IsValidPrepayStart(int amountUzs, int pricePerHourUzs, int minPaymentUzs, int maxPaymentUzs) =>
        IsValidPrepayPayment(amountUzs, minPaymentUzs, maxPaymentUzs) &&
        MinutesForMoney(amountUzs, pricePerHourUzs) > 0;

    /// <summary>
    /// A tariff must have a positive price and a sane min/max before it can be used to bill
    /// anything. Without this, a tariff with price 0 (e.g. freshly added via "Add Tariff" and not
    /// yet edited) would let <see cref="MinutesForMoney"/> silently return 0 minutes while the
    /// full payment is still recorded as paid - charging real money for no time at all.
    /// </summary>
    public static bool IsValidTariff(int pricePerHourUzs, int minPaymentUzs, int maxPaymentUzs) =>
        pricePerHourUzs > 0 && minPaymentUzs >= 0 && maxPaymentUzs > 0 && minPaymentUzs <= maxPaymentUzs;

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

    /// <summary>
    /// Postpay cost of a session whose last segment is still running, from the pieces the server
    /// sends the client: the cost of the already-closed segments (after a Move), plus the open
    /// segment's elapsed time at its price, never below the tariff minimum. Gives the same number as
    /// <see cref="SumPostpaySegments"/> with the minimum applied, so the client can let the cost grow
    /// between server updates (or while the server is unreachable) without drifting from the server.
    /// </summary>
    public static int PostpayCostWithOpenSegment(int closedSegmentsCostUzs, int openSegmentElapsedSeconds, int pricePerHourUzs, int minPaymentUzs)
    {
        var raw = closedSegmentsCostUzs + PriceForMinutes(StartedMinutes(openSegmentElapsedSeconds), pricePerHourUzs);
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
    /// Prepay: the money still unused if the session stops now - the remaining time valued at the
    /// current price, rounded DOWN to 100 UZS (never promise more than is really left) and never more
    /// than what was paid. It is what the admin hands back when stopping early (SPEC 5).
    /// </summary>
    public static int RemainingPrepayMoney(int remainingSeconds, int pricePerHourUzs, int paidUzs)
    {
        if (remainingSeconds <= 0 || pricePerHourUzs <= 0 || paidUzs <= 0)
            return 0;

        var remainingUzs = (long)remainingSeconds * pricePerHourUzs / 3600;
        var roundedDown = remainingUzs / RoundingUzs * RoundingUzs;
        return (int)Math.Min(roundedDown, paidUzs);
    }

    /// <summary>
    /// Prepay stopped early: the session counts as income only for the time really used, i.e. what was
    /// paid minus the <see cref="RemainingPrepayMoney"/> that is handed back. A session that has run its
    /// full time (nothing remaining) stays fully paid.
    /// </summary>
    public static int PrepayIncomeOnEarlyStop(int remainingSeconds, int pricePerHourUzs, int paidUzs) =>
        paidUzs - RemainingPrepayMoney(remainingSeconds, pricePerHourUzs, paidUzs);

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
