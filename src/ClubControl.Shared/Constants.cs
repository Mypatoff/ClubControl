namespace ClubControl.Shared;

public static class NetworkConstants
{
    public const int SignalRPort = 5050;
    public const int DiscoveryPort = 5051;
    public const string HubRoute = "/clubhub";

    // UDP discovery is a two-line text exchange, not JSON, so it is easy to read in a packet sniffer.
    public const string DiscoveryRequestText = "CLUBCONTROL_DISCOVER";
    public const string DiscoveryResponsePrefix = "CLUBCONTROL_SERVER";
}

public static class HubMethodNames
{
    // Client -> server
    public const string Heartbeat = "Heartbeat";

    /// <summary>Tells the server (for the event log only) that staff unlocked this PC with the hotkey.</summary>
    public const string ReportManualUnlock = "ReportManualUnlock";

    // Server -> client
    public const string ReceiveCommand = "ReceiveCommand";
}
