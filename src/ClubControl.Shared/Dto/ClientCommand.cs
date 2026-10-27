namespace ClubControl.Shared.Dto;

/// <summary>Sent by the server to one client via the ReceiveCommand hub method.</summary>
public class ClientCommand
{
    public CommandType Type { get; set; }

    public LockInfo? Lock { get; set; }           // Lock
    public SessionInfo? Session { get; set; }     // StartSession, UpdateSession
    public ClientSettingsDto? Settings { get; set; } // ApplySettings
    public string? AuthToken { get; set; }        // ProvisionToken
    public string? UpdateSha256 { get; set; }     // UpdateClient: hex SHA-256 of the staged zip
    public string? UpdateSignature { get; set; }  // UpdateClient: ClientUpdateAuth.Sign of that hash, for this PC
}
