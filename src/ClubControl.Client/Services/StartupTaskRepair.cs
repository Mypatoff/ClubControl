using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClubControl.Shared;

namespace ClubControl.Client.Services;

/// <summary>
/// On every start, makes sure the "ClubControl Client" Task Scheduler task re-launches the client every
/// minute (see <see cref="ClientTaskRepair"/>). It is the new build that does this, not the update
/// script: the script that swaps the files is written by the client that is being replaced, which does
/// not know about the repair yet. Runs in the background, never blocks the lock screen and never throws.
/// </summary>
public static class StartupTaskRepair
{
    public static void RunInBackground() => Task.Run(Run);

    private static void Run()
    {
        try
        {
            var script = ClientTaskRepair.BuildScript();
            var startInfo = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {ClientTaskRepair.ToEncodedCommand(script)}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
                return;

            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill();
                ClientLog.Info("StartupTaskRepair", "Timed out; the scheduled task was left as it was.");
                return;
            }

            // Only worth a log line when something happened: this runs on every start.
            if (process.ExitCode == ClientTaskRepair.ExitCodeUpdated)
                ClientLog.Info("StartupTaskRepair", "Scheduled task now re-launches the client every minute.");
            else if (process.ExitCode != 0)
                ClientLog.Info("StartupTaskRepair", $"Could not update the scheduled task (exit code {process.ExitCode}): {errors.Result.Trim()}");
        }
        catch (Exception ex)
        {
            ClientLog.Error("StartupTaskRepair", ex);
        }
    }
}
