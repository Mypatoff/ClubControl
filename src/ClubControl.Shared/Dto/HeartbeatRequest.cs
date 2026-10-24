namespace ClubControl.Shared.Dto;

/// <summary>Sent by the client to the server's hub every 5 seconds.</summary>
public class HeartbeatRequest
{
    public Guid MachineGuid { get; set; }
    public string IpAddress { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Hostname { get; set; } = "";
}
