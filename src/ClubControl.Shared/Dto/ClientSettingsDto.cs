namespace ClubControl.Shared.Dto;

/// <summary>The subset of the server's General settings the client needs. Sent via ApplySettings.</summary>
public class ClientSettingsDto
{
    public string ClubName { get; set; } = "ClubControl";
    public string LockScreenText { get; set; } = "";
    public LockScreenLanguage LockScreenLanguage { get; set; } = LockScreenLanguage.English;

    public int WarningMinutes1 { get; set; } = 5;
    public int WarningMinutes2 { get; set; } = 1;

    public AfterSessionAction AfterSessionAction { get; set; } = AfterSessionAction.CloseProcesses;
    public string ProcessListCsv { get; set; } = "";

    public int OfflineUnlockWaitSeconds { get; set; } = 60;
}
