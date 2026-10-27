using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using ClubControl.Shared.Security;

namespace ClubControl.Client.Services;

/// <summary>
/// Handles CommandType.UpdateClient - an admin-triggered pull update, not automatic polling (see
/// the admin panel's "Update client" action). Downloads the build the admin staged on the server
/// (ClientUpdateFile there), checks it is really the build the server signed for this PC, hands
/// the actual file swap to a short PowerShell script since a running .exe cannot overwrite its own
/// files, then exits so the script can bring the updated build back up (or restore the old one if
/// it does not come up healthy - see ClientUpdateScript.Build). The App-level single-instance
/// mutex (see App.xaml.cs) makes it harmless if the Task Scheduler restart policy also relaunches it.
/// </summary>
public static class ClientUpdater
{
    public static async Task ApplyUpdateAsync(string serverIp, int port, ClientCommand command)
    {
        try
        {
            // The command must carry a signature only the real server can make for this PC - see
            // ClientUpdateAuth. A PC with no token yet (never provisioned) cannot check one, so it
            // refuses rather than trust whoever answered discovery.
            var token = TokenStore.Load();
            if (token is null)
                throw new InvalidOperationException("This PC has no auth token yet, so it cannot verify an update.");

            if (command.UpdateSha256 is null || !ClientUpdateAuth.Verify(token, command.UpdateSha256, command.UpdateSignature))
                throw new InvalidOperationException("Update command is not signed by this PC's server - ignored.");

            // Trimmed: a trailing "\" right before the closing quote in the script would be
            // read as an escaped quote once PowerShell hands the argument to the native robocopy.exe,
            // not as a literal backslash - a classic Windows argument-quoting trap.
            var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var exeName = Path.GetFileName(Environment.ProcessPath ?? "ClubControl.Client.exe");

            var tempDir = Path.GetTempPath();
            var zipPath = Path.Combine(tempDir, "ClubControl.Client.Update.zip");
            var extractDir = Path.Combine(tempDir, "ClubControl.Client.Update");
            var backupDir = Path.Combine(tempDir, "ClubControl.Client.Backup");

            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync($"http://{serverIp}:{port}{NetworkConstants.ClientUpdateRoute}");

            // The zip itself came over plain HTTP; the signed hash is what makes it trustworthy.
            if (!string.Equals(ClientUpdateAuth.ComputeSha256Hex(bytes), command.UpdateSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Downloaded update does not match the signed hash - discarded.");

            await File.WriteAllBytesAsync(zipPath, bytes);

            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, recursive: true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // Checked before anything is closed: a bad zip must fail here, while the old client is
            // still running, not after it has already shut down.
            var sourceDir = ClientUpdateScript.FindBuildFolder(extractDir, exeName);

            var scriptPath = Path.Combine(tempDir, "ClubControl.Client.Update.ps1");
            await File.WriteAllTextAsync(scriptPath, ClientUpdateScript.Build(
                Environment.ProcessId, sourceDir, extractDir, installDir, exeName, backupDir, ClientUpdateScript.HealthMarkerPath));

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
}
