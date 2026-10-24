namespace ClubControl.Shared.Dto;

/// <summary>Sent by the server in reply to a UDP discovery broadcast.</summary>
public class DiscoveryResponse
{
    public Guid ServerId { get; set; }
    public string ServerIp { get; set; } = "";
    public int Port { get; set; }

    public override string ToString() =>
        $"{NetworkConstants.DiscoveryResponsePrefix}|{ServerId}|{ServerIp}|{Port}";

    public static DiscoveryResponse? TryParse(string text)
    {
        var parts = text.Split('|');
        if (parts.Length != 4 || parts[0] != NetworkConstants.DiscoveryResponsePrefix)
            return null;

        if (!Guid.TryParse(parts[1], out var serverId))
            return null;

        if (!int.TryParse(parts[3], out var port))
            return null;

        return new DiscoveryResponse
        {
            ServerId = serverId,
            ServerIp = parts[2],
            Port = port
        };
    }
}
