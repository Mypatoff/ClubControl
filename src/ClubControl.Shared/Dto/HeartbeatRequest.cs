namespace ClubControl.Shared.Dto;

/// <summary>Sent by the client to the server's hub every 5 seconds.</summary>
public class HeartbeatRequest
{
    public Guid MachineGuid { get; set; }
    public string IpAddress { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Hostname { get; set; } = "";

    /// <summary>
    /// This PC's auth token (see Pc.AuthTokenHash), once the server has provisioned one. Null for
    /// a PC that has never been configured yet, or that is heartbeating for the very first time
    /// after being configured (it has no token yet to send - ClubHub mints one on this heartbeat).
    /// </summary>
    public string? AuthToken { get; set; }
}
