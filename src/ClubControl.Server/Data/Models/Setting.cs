using System;
using ClubControl.Shared;

namespace ClubControl.Server.Data.Models;

/// <summary>Single-row table holding every General setting plus the server's own persistent identity.</summary>
public class Setting
{
    public int Id { get; set; } = 1;

    public Guid ServerId { get; set; } = Guid.NewGuid();

    public string ClubName { get; set; } = "ClubControl";
    public string LockScreenText { get; set; } = "";
    public LockScreenLanguage LockScreenLanguage { get; set; } = LockScreenLanguage.English;
    public AdminPanelLanguage AdminPanelLanguage { get; set; } = AdminPanelLanguage.Russian;
    public UiTheme UiTheme { get; set; } = UiTheme.Light;

    public int WarningMinutes1 { get; set; } = 5;
    public int WarningMinutes2 { get; set; } = 1;

    public AfterSessionAction AfterSessionAction { get; set; } = AfterSessionAction.CloseProcesses;
    public string ProcessListCsv { get; set; } = "steam.exe";

    public int OfflineUnlockWaitSeconds { get; set; } = 60;

    /// <summary>Idle-locked PCs (not limit-reached, not mid-session) shut themselves down after this many minutes. 0 = disabled.</summary>
    public int AutoShutdownAfterMinutes { get; set; } = 1;

    public string? AdminPasswordHash { get; set; }
}
