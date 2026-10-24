using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class OfflineDetectorTests
{
    [Fact]
    public void Online_WhenHeartbeatJustArrived()
    {
        var now = DateTime.UtcNow;
        Assert.False(OfflineDetector.IsOffline(now, now));
    }

    [Fact]
    public void Online_AtExactlyTheTimeoutBoundary()
    {
        var now = DateTime.UtcNow;
        var lastHeartbeat = now - OfflineDetector.HeartbeatTimeout;
        Assert.False(OfflineDetector.IsOffline(lastHeartbeat, now));
    }

    [Fact]
    public void Offline_OneSecondPastTheTimeout()
    {
        var now = DateTime.UtcNow;
        var lastHeartbeat = now - OfflineDetector.HeartbeatTimeout - TimeSpan.FromSeconds(1);
        Assert.True(OfflineDetector.IsOffline(lastHeartbeat, now));
    }

    [Fact]
    public void CustomTimeout_IsRespected()
    {
        var now = DateTime.UtcNow;
        var lastHeartbeat = now - TimeSpan.FromSeconds(30);
        Assert.True(OfflineDetector.IsOffline(lastHeartbeat, now, TimeSpan.FromSeconds(15)));
        Assert.False(OfflineDetector.IsOffline(lastHeartbeat, now, TimeSpan.FromSeconds(60)));
    }
}
