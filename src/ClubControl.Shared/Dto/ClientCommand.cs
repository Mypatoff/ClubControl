namespace ClubControl.Shared.Dto;

/// <summary>Sent by the server to one client via the ReceiveCommand hub method.</summary>
public class ClientCommand
{
    public CommandType Type { get; set; }

    public LockInfo? Lock { get; set; }           // Lock
    public SessionInfo? Session { get; set; }     // StartSession, UpdateSession
    public ClientSettingsDto? Settings { get; set; } // ApplySettings
    public string? AuthToken { get; set; }        // ProvisionToken
}
