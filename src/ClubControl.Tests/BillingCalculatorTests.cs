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

    // ---- extend (SPEC 5: "Extend (Доплата): add money or minutes at the current PC's price") ----

    [Theory]
    [InlineData(1000, 6, true)]   // ordinary top-up: positive money, buys real extra time
    [InlineData(0, 0, false)]     // zero money -> zero minutes: nothing was actually added
    [InlineData(-500, 0, false)]  // negative money (should never reach here, but must still be rejected)
    [InlineData(500, 0, false)]   // too little money to buy even one extra minute at this price
    public void IsValidExtendIncrement_RequiresPositiveMoneyAndMinutes(int addMoneyUzs, int addMinutes, bool expected)
    {
        Assert.Equal(expected, BillingCalculator.IsValidExtendIncrement(addMoneyUzs, addMinutes));
    }

    [Fact]
    public void IsValidExtendIncrement_TooSmallMoneyAmount_BuysNoTime()
    {
        // The exact bug this guards against: a top-up small enough that MinutesForMoney rounds
        // down to zero would otherwise charge money for zero added time.
        const int pricePerHourUzs = 10000;
        const int tooSmallMoneyUzs = 100;

        var addMinutes = BillingCalculator.MinutesForMoney(tooSmallMoneyUzs, pricePerHourUzs);

        Assert.Equal(0, addMinutes);
        Assert.False(BillingCalculator.IsValidExtendIncrement(tooSmallMoneyUzs, addMinutes));
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
