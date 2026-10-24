namespace ClubControl.Shared;

/// <summary>Pure heartbeat-timeout logic, shared by the server's offline monitor and its tests.</summary>
public static class OfflineDetector
{
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(15);

    public static bool IsOffline(DateTime lastHeartbeatUtc, DateTime nowUtc) =>
        IsOffline(lastHeartbeatUtc, nowUtc, HeartbeatTimeout);

    public static bool IsOffline(DateTime lastHeartbeatUtc, DateTime nowUtc, TimeSpan timeout) =>
        nowUtc - lastHeartbeatUtc > timeout;
}
