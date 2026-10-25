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

    [Fact]
    public void Online_AfterMissingASingleHeartbeatToNetworkJitter()
    {
        // The client heartbeats every 5s; the 15s default timeout must tolerate at least one
        // dropped/delayed heartbeat (brief Wi-Fi/switch jitter) without flipping to offline.
        var now = DateTime.UtcNow;
        var lastHeartbeat = now - TimeSpan.FromSeconds(11);
        Assert.False(OfflineDetector.IsOffline(lastHeartbeat, now));
    }

    [Fact]
    public void Online_WhenClockSkewMakesLastHeartbeatAppearInTheFuture()
    {
        // A backward clock step (NTP correction) must never be misread as "ancient" heartbeat -
        // a negative elapsed time is clearly not ">" the timeout.
        var now = DateTime.UtcNow;
        var lastHeartbeat = now + TimeSpan.FromSeconds(5);
        Assert.False(OfflineDetector.IsOffline(lastHeartbeat, now));
    }
}
