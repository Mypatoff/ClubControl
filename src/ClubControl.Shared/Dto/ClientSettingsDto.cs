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

    /// <summary>
    /// PBKDF2 hash of the staff unlock password (Setting.ClientUnlockPasswordHash). The client
    /// caches it on disk so the unlock hotkey still works with no server reachable. Null = none set.
    /// </summary>
    public string? UnlockPasswordHash { get; set; }

    // Safe default is 0 (disabled) rather than mirroring the server's default: a client that
    // hasn't synced real settings yet (e.g. still searching for the server at boot) must never
    // shut itself down on a stale/default value. 0 means disabled.
    public int AutoShutdownAfterMinutes { get; set; } = 0;
}
