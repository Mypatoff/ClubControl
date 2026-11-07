using ClubControl.Server.Services;

namespace ClubControl.Server.Tests;

public class SlowLogTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_fast_run_is_not_logged()
    {
        Assert.False(SlowLog.ShouldLog(TimeSpan.FromMilliseconds(20), DateTime.MinValue, Now));
    }

    [Fact]
    public void A_slow_run_is_logged()
    {
        Assert.True(SlowLog.ShouldLog(SlowLog.Threshold, DateTime.MinValue, Now));
    }

    [Fact]
    public void The_same_slow_work_is_logged_only_once_a_minute()
    {
        var slow = TimeSpan.FromSeconds(1);

        Assert.False(SlowLog.ShouldLog(slow, Now.AddSeconds(-30), Now));
        Assert.True(SlowLog.ShouldLog(slow, Now - SlowLog.MinGap, Now));
    }

    [Fact]
    public void The_line_names_the_work_and_each_step()
    {
        var text = SlowLog.Describe("Refresh", TimeSpan.FromMilliseconds(412), new List<(string, long)> { ("sessions", 300), ("pcs", 80) });

        Assert.Equal("Refresh took 412 ms: sessions 300 ms, pcs 80 ms", text);
    }

    [Fact]
    public void A_run_without_steps_still_gets_a_line()
    {
        Assert.Equal("Tick took 200 ms", SlowLog.Describe("Tick", TimeSpan.FromMilliseconds(200), new List<(string, long)>()));
    }
}
