using System;
using System.Text;

namespace ClubControl.Shared;

/// <summary>
/// Makes an already-installed client's "ClubControl Client" Task Scheduler task re-launch the client
/// every minute. That repetition is what brings back a client someone ended in Task Manager (Task
/// Scheduler's restart-on-failure does not fire for a manual "End Task"). A fresh install gets it from
/// install-client.ps1 / ClubControl-Setup.exe; an "Update client" only swaps files, so the new build
/// calls this at startup to give PCs installed before the fix the same task. Kept in Shared so the
/// cross-platform tests can check the generated script.
/// </summary>
public static class ClientTaskRepair
{
    public const string TaskName = "ClubControl Client";

    /// <summary>Exit code of the script when it changed the task (0 means: nothing to do, or already fine).</summary>
    public const int ExitCodeUpdated = 10;

    /// <summary>
    /// No task (client started some other way) means nothing to repair, and a task that already
    /// repeats is not touched. Otherwise only the logon trigger is replaced; the action, the user
    /// and the settings of the task stay as they were.
    /// </summary>
    public static string BuildScript(string taskName = TaskName)
    {
        var lines = new[]
        {
            "$ErrorActionPreference = 'Stop'",
            $"$name = {Q(taskName)}",
            "$task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue",
            "if (-not $task) { exit 0 }",
            "$user = $task.Principal.UserId",
            "if (-not $user) { exit 0 }",
            "$logon = @($task.Triggers | Where-Object { $_.CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger' })",
            "if ($logon.Count -gt 0 -and $logon[0].Repetition.Interval -eq 'PT1M') { exit 0 }",
            "$others = @($task.Triggers | Where-Object { $_.CimClass.CimClassName -ne 'MSFT_TaskLogonTrigger' })",
            // Same trigger install-client.ps1 builds: New-ScheduledTaskTrigger has no repetition option for
            // -AtLogOn, so the Repetition of a throwaway "once" trigger is copied over.
            "$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user",
            "$repeat = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 1) -RepetitionDuration (New-TimeSpan -Days 3650)",
            "$trigger.Repetition = $repeat.Repetition",
            "Set-ScheduledTask -TaskName $name -Trigger (@($trigger) + $others) | Out-Null",
            $"exit {ExitCodeUpdated}",
        };

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The script as the argument of powershell.exe -EncodedCommand (UTF-16, base64): no quoting problems.</summary>
    public static string ToEncodedCommand(string script) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    // Single-quoted PowerShell string: nothing inside is expanded, only ' needs doubling.
    private static string Q(string value) => "'" + value.Replace("'", "''") + "'";
}
