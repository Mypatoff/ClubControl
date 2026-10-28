using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class BillingCalculatorTests
{
    // ---- rounding (SPEC 5: "Round every charge UP to the nearest 100 UZS") ----

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 100)]
    [InlineData(99, 100)]
    [InlineData(100, 100)]
    [InlineData(101, 200)]
    [InlineData(250, 300)]
    [InlineData(-50, 0)]
    public void RoundUpToHundred_RoundsUpToNextHundred(int input, int expected)
    {
        Assert.Equal(expected, BillingCalculator.RoundUpToHundred(input));
    }

    // ---- per-started-minute charging (SPEC 5: "Charge per started minute") ----

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(59, 1)]
    [InlineData(60, 1)]
    [InlineData(61, 2)]
    [InlineData(120, 2)]
    [InlineData(121, 3)]
    public void StartedMinutes_RoundsPartialMinutesUp(int elapsedSeconds, int expectedMinutes)
    {
        Assert.Equal(expectedMinutes, BillingCalculator.StartedMinutes(elapsedSeconds));
    }

    [Theory]
    [InlineData(0, 10000, 0)]
    [InlineData(30, 0, 0)]
    [InlineData(30, 10000, 5000)]   // exact multiple of 100 already
    [InlineData(7, 10000, 1200)]    // 1166.67 -> rounds up
    [InlineData(1, 10000, 200)]     // 166.67 -> rounds up
    [InlineData(10, 15000, 2500)]
    public void PriceForMinutes_ChargesAndRoundsUpToHundred(int minutes, int pricePerHourUzs, int expected)
    {
        Assert.Equal(expected, BillingCalculator.PriceForMinutes(minutes, pricePerHourUzs));
    }

    // ---- prepay start-by-money / start-by-time (SPEC 5: "Prepay") ----

    [Theory]
    [InlineData(5000, 10000, 30)]
    [InlineData(5999, 10000, 35)]   // rounds down - no partial minutes
    [InlineData(0, 10000, 0)]
    [InlineData(5000, 0, 0)]
    public void MinutesForMoney_BuysWholeMinutesOnly(int moneyUzs, int pricePerHourUzs, int expectedMinutes)
    {
        Assert.Equal(expectedMinutes, BillingCalculator.MinutesForMoney(moneyUzs, pricePerHourUzs));
    }

    [Fact]
    public void StartByMoney_ThenStartByTime_AreConsistent()
    {
        const int pricePerHourUzs = 10000;
        const int moneyUzs = 5000;

        var minutes = BillingCalculator.MinutesForMoney(moneyUzs, pricePerHourUzs);
        var priceForThoseMinutes = BillingCalculator.PriceForMinutes(minutes, pricePerHourUzs);

        Assert.Equal(30, minutes);
        Assert.Equal(moneyUzs, priceForThoseMinutes);
    }

    [Theory]
    [InlineData(1999, 2000, 50000, false)]  // below minimum
    [InlineData(2000, 2000, 50000, true)]   // at minimum - inclusive
    [InlineData(25000, 2000, 50000, true)]  // in range
    [InlineData(50000, 2000, 50000, true)]  // at maximum - inclusive
    [InlineData(50001, 2000, 50000, false)] // above maximum
    public void IsValidPrepayPayment_ChecksTariffRangeInclusive(int amountUzs, int minUzs, int maxUzs, bool expected)
    {
        Assert.Equal(expected, BillingCalculator.IsValidPrepayPayment(amountUzs, minUzs, maxUzs));
    }

    // ---- postpay (SPEC 5: "Postpay") ----

    [Theory]
    [InlineData(0, 10000, 2000, 2000)]     // just started - clamped to minimum
    [InlineData(70, 10000, 2000, 2000)]    // 2 started minutes, still below minimum
    [InlineData(780, 10000, 2000, 2200)]   // 13 started minutes, above minimum
    public void PostpayCost_NeverBelowMinimum(int elapsedSeconds, int pricePerHourUzs, int minPaymentUzs, int expected)
    {
        Assert.Equal(expected, BillingCalculator.PostpayCost(elapsedSeconds, pricePerHourUzs, minPaymentUzs));
    }

    [Theory]
    [InlineData(49999, 50000, false)]
    [InlineData(50000, 50000, true)]
    [InlineData(50001, 50000, true)]
    [InlineData(100, 0, false)] // no maximum configured - never "reaches" it
    public void HasReachedLimit_AtOrAboveMaximum(int costUzs, int maxPaymentUzs, bool expected)
    {
        Assert.Equal(expected, BillingCalculator.HasReachedLimit(costUzs, maxPaymentUzs));
    }

    // ---- move (SPEC 5: "Move") ----

    [Fact]
    public void RecalculateMinutesForMove_Prepay_ConvertsRemainingMoneyAtNewPrice()
    {
        // 30 minutes remaining at 10000/hr = 5000 UZS owed; at 20000/hr that buys 15 minutes.
        var minutes = BillingCalculator.RecalculateMinutesForMove(
            remainingSeconds: 1800, oldPricePerHourUzs: 10000, newPricePerHourUzs: 20000);

        Assert.Equal(15, minutes);
    }

    [Fact]
    public void RecalculateMinutesForMove_SamePrice_KeepsRemainingTimeUnchanged()
    {
        var minutes = BillingCalculator.RecalculateMinutesForMove(
            remainingSeconds: 3600, oldPricePerHourUzs: 10000, newPricePerHourUzs: 10000);

        Assert.Equal(60, minutes);
    }

    [Theory]
    [InlineData(0, 10000, 10000)]
    [InlineData(1800, 0, 10000)]
    [InlineData(1800, 10000, 0)]
    public void RecalculateMinutesForMove_InvalidInputs_YieldZero(int remainingSeconds, int oldPrice, int newPrice)
    {
        Assert.Equal(0, BillingCalculator.RecalculateMinutesForMove(remainingSeconds, oldPrice, newPrice));
    }

    // ---- stop early: unused prepaid money (SPEC 5: "Early stop") ----

    [Fact]
    public void RemainingPrepayMoney_ValuesTheTimeLeftAtThePrice()
    {
        // 30 minutes left at 10000/hr = 5000 UZS unused.
        Assert.Equal(5000, BillingCalculator.RemainingPrepayMoney(remainingSeconds: 1800, pricePerHourUzs: 10000, paidUzs: 10000));
    }

    [Fact]
    public void RemainingPrepayMoney_RoundsDownToHundred()
    {
        // 29m30s at 10000/hr = 4916 UZS -> 4900, never 5000.
        Assert.Equal(4900, BillingCalculator.RemainingPrepayMoney(remainingSeconds: 29 * 60 + 30, pricePerHourUzs: 10000, paidUzs: 10000));
    }

    [Fact]
    public void RemainingPrepayMoney_NeverExceedsWhatWasPaid()
    {
        // A paid amount that bought slightly less time than the arithmetic suggests must cap the answer.
        Assert.Equal(4000, BillingCalculator.RemainingPrepayMoney(remainingSeconds: 3600, pricePerHourUzs: 10000, paidUzs: 4000));
    }

    [Theory]
    [InlineData(0, 10000, 5000)]
    [InlineData(-5, 10000, 5000)]
    [InlineData(1800, 0, 5000)]
    [InlineData(1800, 10000, 0)]
    public void RemainingPrepayMoney_InvalidInputs_YieldZero(int remainingSeconds, int price, int paid)
    {
        Assert.Equal(0, BillingCalculator.RemainingPrepayMoney(remainingSeconds, price, paid));
    }

    [Fact]
    public void PrepayIncomeOnEarlyStop_IsWhatWasPaidMinusTheRefund()
    {
        // Paid 10000, 30 minutes of 60 left: 5000 is handed back, 5000 is income.
        Assert.Equal(5000, BillingCalculator.PrepayIncomeOnEarlyStop(remainingSeconds: 1800, pricePerHourUzs: 10000, paidUzs: 10000));
    }

    [Fact]
    public void PrepayIncomeOnEarlyStop_RoundsTheRefundDownSoIncomeRoundsUp()
    {
        // 29m30s left = 4916 UZS -> refund 4900, income 5100.
        Assert.Equal(5100, BillingCalculator.PrepayIncomeOnEarlyStop(29 * 60 + 30, 10000, 10000));
    }

    [Fact]
    public void PrepayIncomeOnEarlyStop_NothingLeft_KeepsTheFullPayment()
    {
        Assert.Equal(10000, BillingCalculator.PrepayIncomeOnEarlyStop(0, 10000, 10000));
    }

    [Fact]
    public void SumPostpaySegments_AddsEachSegmentsOwnRoundedCharge()
    {
        var segments = new[]
        {
            (ElapsedSeconds: 600, PricePerHourUzs: 10000),  // 10 min @ 10000/hr -> 1700
            (ElapsedSeconds: 300, PricePerHourUzs: 20000),  // 5 min @ 20000/hr -> 1700
        };

        Assert.Equal(3400, BillingCalculator.SumPostpaySegments(segments));
    }

    [Fact]
    public void SumPostpaySegments_NoSegments_IsZero()
    {
        Assert.Equal(0, BillingCalculator.SumPostpaySegments(Array.Empty<(int, int)>()));
    }

    // ---- client-side extrapolation of the postpay cost (offline fallback) must match the server's math ----

    [Theory]
    [InlineData(0, 0, 10_000, 2_000, 2_000)]        // nothing run yet: the minimum payment
    [InlineData(0, 1, 10_000, 2_000, 2_000)]        // 1 started minute = 200, still below the minimum
    [InlineData(0, 1_770, 10_000, 2_000, 5_000)]    // 29.5 min -> 30 started minutes = 5 000
    [InlineData(5_000, 0, 15_000, 3_000, 5_000)]    // after a Move: kept cost, nothing added at the new price yet
    [InlineData(5_000, 600, 15_000, 3_000, 7_500)]  // ... then 10 minutes at the new price (2 500)
    public void PostpayCostWithOpenSegment_AddsTheOpenSegmentToTheKeptCost(
        int closedCost, int openElapsedSeconds, int price, int min, int expected)
    {
        Assert.Equal(expected, BillingCalculator.PostpayCostWithOpenSegment(closedCost, openElapsedSeconds, price, min));
    }

    [Fact]
    public void PostpayCostWithOpenSegment_MatchesTheServersSegmentSum()
    {
        // The server sums closed segments + the open one with SumPostpaySegments, then applies the minimum.
        var closed = new[] { (ElapsedSeconds: 1_770, PricePerHourUzs: 10_000) };
        var closedCost = BillingCalculator.SumPostpaySegments(closed);

        for (var openElapsed = 0; openElapsed <= 3 * 3600; openElapsed += 37)
        {
            var serverCost = Math.Max(
                BillingCalculator.SumPostpaySegments(closed.Append((openElapsed, 15_000))), 3_000);

            Assert.Equal(serverCost, BillingCalculator.PostpayCostWithOpenSegment(closedCost, openElapsed, 15_000, 3_000));
        }
    }

    // ---- tariff integrity (the exact bug this guards against: a price of 0 lets MinutesForMoney
    // silently buy zero minutes while still recording the full payment as paid) ----

    [Theory]
    [InlineData(10000, 2000, 50000, true)]   // ordinary valid tariff
    [InlineData(0, 2000, 50000, false)]      // price 0 - the freshly-added-tariff bug
    [InlineData(-5000, 2000, 50000, false)]  // negative price
    [InlineData(10000, 2000, 0, false)]      // no maximum configured - unusable, not "unlimited"
    [InlineData(10000, -1, 50000, false)]    // negative minimum
    [InlineData(10000, 50000, 2000, false)]  // min above max
    [InlineData(10000, 2000, 2000, true)]    // min == max is still a valid (if narrow) range
    public void IsValidTariff_RequiresPositivePriceAndSaneRange(int priceUzs, int minUzs, int maxUzs, bool expected)
    {
        Assert.Equal(expected, BillingCalculator.IsValidTariff(priceUzs, minUzs, maxUzs));
    }

    [Fact]
    public void IsValidTariff_ZeroPrice_WouldOtherwiseBuyZeroMinutesWhileChargingFullMoney()
    {
        // Documents exactly why IsValidTariff must reject price 0: without this guard,
        // MinutesForMoney silently returns 0 minutes for a real, positive payment.
        const int priceUzs = 0;
        const int moneyUzs = 5000;

        Assert.False(BillingCalculator.IsValidTariff(priceUzs, 2000, 50000));
        Assert.Equal(0, BillingCalculator.MinutesForMoney(moneyUzs, priceUzs));
    }

    // ---- zero-minute prepay start (a payment can be within [min, max] and still round down to
    // 0 minutes if the tariff's minimum is set below the cost of one minute at that price) ----

    [Fact]
    public void IsValidPrepayStart_MinPaymentBelowOneMinuteCost_RejectsEvenThoughInRange()
    {
        // 100,000 UZS/hour costs 1,666.67 UZS/min. A minimum payment of 1,000 UZS is "in range"
        // for IsValidPrepayPayment but MinutesForMoney(1000, 100000) rounds down to 0.
        const int priceUzs = 100_000;
        const int minPaymentUzs = 1_000;
        const int maxPaymentUzs = 50_000;

        Assert.True(BillingCalculator.IsValidPrepayPayment(minPaymentUzs, minPaymentUzs, maxPaymentUzs));
        Assert.Equal(0, BillingCalculator.MinutesForMoney(minPaymentUzs, priceUzs));
        Assert.False(BillingCalculator.IsValidPrepayStart(minPaymentUzs, priceUzs, minPaymentUzs, maxPaymentUzs));
    }

    [Fact]
    public void IsValidPrepayStart_SmallestPaymentThatBuysOneMinute_IsAccepted()
    {
        // 100,000 UZS/hour: exactly 1,667 UZS buys floor(1667*60/100000) = 1 minute.
        const int priceUzs = 100_000;
        const int moneyUzs = 1_667;

        Assert.Equal(1, BillingCalculator.MinutesForMoney(moneyUzs, priceUzs));
        Assert.True(BillingCalculator.IsValidPrepayStart(moneyUzs, priceUzs, minPaymentUzs: 0, maxPaymentUzs: 50_000));
    }

    [Fact]
    public void IsValidPrepayStart_NormalPayment_IsAccepted()
    {
        const int priceUzs = 10_000;
        const int moneyUzs = 20_000;

        Assert.Equal(120, BillingCalculator.MinutesForMoney(moneyUzs, priceUzs));
        Assert.True(BillingCalculator.IsValidPrepayStart(moneyUzs, priceUzs, minPaymentUzs: 2_000, maxPaymentUzs: 50_000));
    }

    [Fact]
    public void IsValidPrepayStart_OutOfRangePayment_IsRejectedRegardlessOfMinutes()
    {
        const int priceUzs = 10_000;

        Assert.False(BillingCalculator.IsValidPrepayStart(1_000, priceUzs, minPaymentUzs: 2_000, maxPaymentUzs: 50_000));
        Assert.False(BillingCalculator.IsValidPrepayStart(60_000, priceUzs, minPaymentUzs: 2_000, maxPaymentUzs: 50_000));
    }

    // ---- power-cut restore (SPEC 5: "Power cuts / restarts") ----

    [Fact]
    public void IsPrepayExpired_BeforePlannedEnd_IsNotExpired()
    {
        var plannedEnd = DateTime.UtcNow.AddMinutes(1);
        Assert.False(BillingCalculator.IsPrepayExpired(plannedEnd, DateTime.UtcNow));
    }

    [Fact]
    public void IsPrepayExpired_AtPlannedEnd_IsExpired()
    {
        var now = DateTime.UtcNow;
        Assert.True(BillingCalculator.IsPrepayExpired(now, now));
    }

    [Fact]
    public void IsPrepayExpired_AfterPlannedEnd_IsExpired()
    {
        var plannedEnd = DateTime.UtcNow.AddMinutes(-5);
        Assert.True(BillingCalculator.IsPrepayExpired(plannedEnd, DateTime.UtcNow));
    }
}
