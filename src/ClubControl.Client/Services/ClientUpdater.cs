using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Shared;

namespace ClubControl.Client.Services;

/// <summary>
/// Handles CommandType.UpdateClient - an admin-triggered pull update, not automatic polling (see
/// the admin panel's "Update client" action). Downloads the build the admin staged on the server
/// (ClientUpdateFile there), hands the actual file swap to a short PowerShell script since a
/// running .exe cannot overwrite its own files, then exits so the script - and, as a safety net,
/// the Task Scheduler restart policy - can bring the updated build back up. The App-level
/// single-instance mutex (see App.xaml.cs) makes it harmless if both end up trying to start it.
/// </summary>
public static class ClientUpdater
{
    public static async Task ApplyUpdateAsync(string serverIp, int port)
    {
        try
        {
            // Trimmed: a trailing "\" right before the closing quote in the script below would be
            // read as an escaped quote once PowerShell hands the argument to the native robocopy.exe,
            // not as a literal backslash - a classic Windows argument-quoting trap.
            var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var exeName = Path.GetFileName(Environment.ProcessPath ?? "ClubControl.Client.exe");

            var zipPath = Path.Combine(Path.GetTempPath(), "ClubControl.Client.Update.zip");
            var extractDir = Path.Combine(Path.GetTempPath(), "ClubControl.Client.Update");

            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync($"http://{serverIp}:{port}{NetworkConstants.ClientUpdateRoute}");
            await File.WriteAllBytesAsync(zipPath, bytes);

            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, recursive: true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // Checked before anything is closed: a bad zip must fail here, while the old client is
            // still running, not after it has already shut down.
            var sourceDir = FindBuildFolder(extractDir, exeName);

            var scriptPath = Path.Combine(Path.GetTempPath(), "ClubControl.Client.Update.ps1");
            await File.WriteAllTextAsync(scriptPath, BuildUpdateScript(Environment.ProcessId, sourceDir, extractDir, installDir, exeName));

            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"")
            {
                UseShellExecute = false,
            });

            // Must come after Process.Start, not before - the script's very first step is waiting
            // for this process to actually exit, so it needs to already be running and watching
            // this PID before Shutdown ends it.
            Application.Current.Dispatcher.Invoke(Application.Current.Shutdown);
        }
        catch (Exception ex)
        {
            // Never take the client down over a failed update attempt - it keeps running the
            // version it already has, and the admin can just try "Update client" again later.
            ClientLog.Error("ClientUpdater.ApplyUpdateAsync", ex);
        }
    }

    /// <summary>
    /// The folder inside the extracted zip that actually holds the client exe. Normally the zip
    /// root, but zipping the publish folder itself (instead of its contents) adds one wrapper
    /// folder, so that is accepted too. Anything else is rejected - copying a zip that has no exe
    /// over the install folder would "update" nothing and leave the admin guessing.
    /// </summary>
    private static string FindBuildFolder(string extractDir, string exeName)
    {
        if (File.Exists(Path.Combine(extractDir, exeName)))
            return extractDir;

        var subDirs = Directory.GetDirectories(extractDir);
        if (subDirs.Length == 1 && File.Exists(Path.Combine(subDirs[0], exeName)))
            return subDirs[0];

        throw new InvalidDataException($"Update zip does not contain {exeName}.");
    }

    /// <summary>
    /// Waits for this process to exit (so the exe/dll files are no longer locked), copies the
    /// freshly extracted build over the install folder, then starts it again.
    /// </summary>
    private static string BuildUpdateScript(int clientPid, string sourceDir, string extractDir, string installDir, string exeName)
    {
        var exePath = Path.Combine(installDir, exeName);
        var lines = new[]
        {
            // Not named $pid: that is a read-only built-in in PowerShell (the script's own process
            // id), so assigning to it fails and the wait below would never end.
            $"$clientPid = {clientPid}",
            // Gives up after 60s instead of waiting forever; the old client is left untouched.
            "Wait-Process -Id $clientPid -Timeout 60 -ErrorAction SilentlyContinue",
            "if (Get-Process -Id $clientPid -ErrorAction SilentlyContinue) { exit 1 }",
            "Start-Sleep -Seconds 1",
            $"robocopy \"{sourceDir}\" \"{installDir}\" /E /IS /IT",
            $"Remove-Item -Recurse -Force \"{extractDir}\"",
            $"Start-Process -FilePath \"{exePath}\" -WorkingDirectory \"{installDir}\""
        };

        return string.Join(Environment.NewLine, lines);
    }
}
