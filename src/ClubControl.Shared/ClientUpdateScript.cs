using System;
using System.IO;

namespace ClubControl.Shared;

/// <summary>
/// The pure, testable parts of "Update client": picking the build folder out of an extracted zip
/// and writing the PowerShell script that swaps the files. Kept here, not in the Windows-only
/// client project, so the cross-platform tests can check them.
/// </summary>
public static class ClientUpdateScript
{
    /// <summary>
    /// A freshly started client writes this file after its first successful heartbeat. The update
    /// script waits for it to tell a healthy new build from one that starts and then dies.
    /// </summary>
    public static readonly string HealthMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClubControl", "client-healthy.flag");

    /// <summary>
    /// The folder inside the extracted zip that actually holds the client exe. Normally the zip
    /// root, but zipping the publish folder itself (instead of its contents) adds one wrapper
    /// folder, so that is accepted too. Anything else is rejected - copying a zip that has no exe
    /// over the install folder would "update" nothing and leave the admin guessing.
    /// </summary>
    public static string FindBuildFolder(string extractDir, string exeName)
    {
        if (File.Exists(Path.Combine(extractDir, exeName)))
            return extractDir;

        var subDirs = Directory.GetDirectories(extractDir);
        if (subDirs.Length == 1 && File.Exists(Path.Combine(subDirs[0], exeName)))
            return subDirs[0];

        throw new InvalidDataException($"Update zip does not contain {exeName}.");
    }

    /// <summary>
    /// Waits for the old client to exit (so its exe/dll files are no longer locked), backs up the
    /// install folder, copies the new build over it and starts it. If the new build does not report
    /// healthy (see <see cref="HealthMarkerPath"/>) within <paramref name="healthSeconds"/>, the
    /// backup is restored and the old build is started again - a bad build costs a minute on that
    /// PC, not a manual reinstall.
    /// </summary>
    public static string Build(int clientPid, string sourceDir, string extractDir, string installDir, string exeName,
        string backupDir, string markerPath, int exitWaitSeconds = 60, int healthSeconds = 90)
    {
        var exePath = Path.Combine(installDir, exeName);
        var processName = Path.GetFileNameWithoutExtension(exeName);

        var lines = new[]
        {
            // Not named $pid: that is a read-only built-in in PowerShell (the script's own process
            // id), so assigning to it fails and the wait below would never end.
            $"$clientPid = {clientPid}",
            // Gives up instead of waiting forever; the old client is left untouched.
            $"Wait-Process -Id $clientPid -Timeout {exitWaitSeconds} -ErrorAction SilentlyContinue",
            "if (Get-Process -Id $clientPid -ErrorAction SilentlyContinue) { exit 1 }",
            "Start-Sleep -Seconds 1",

            $"function Restore-OldBuild {{",
            $"    Get-Process -Name {Q(processName)} -ErrorAction SilentlyContinue | Stop-Process -Force",
            "    Start-Sleep -Seconds 2",
            $"    robocopy {Q(backupDir)} {Q(installDir)} /MIR | Out-Null",
            $"    Start-Process -FilePath {Q(exePath)} -WorkingDirectory {Q(installDir)}",
            "}",

            $"if (Test-Path {Q(backupDir)}) {{ Remove-Item -Recurse -Force {Q(backupDir)} }}",
            $"robocopy {Q(installDir)} {Q(backupDir)} /E | Out-Null",
            $"robocopy {Q(sourceDir)} {Q(installDir)} /E /IS /IT | Out-Null",
            // robocopy: 0-7 mean "copied (some/none)", 8 and up mean something failed.
            "if ($LASTEXITCODE -ge 8) { Restore-OldBuild; exit 2 }",

            $"Remove-Item {Q(markerPath)} -Force -ErrorAction SilentlyContinue",
            $"Start-Process -FilePath {Q(exePath)} -WorkingDirectory {Q(installDir)}",
            $"$deadline = (Get-Date).AddSeconds({healthSeconds})",
            $"while (-not (Test-Path {Q(markerPath)}) -and (Get-Date) -lt $deadline) {{ Start-Sleep -Seconds 1 }}",
            $"if (-not (Test-Path {Q(markerPath)})) {{ Restore-OldBuild; exit 3 }}",

            $"Remove-Item -Recurse -Force {Q(extractDir)} -ErrorAction SilentlyContinue",
            $"Remove-Item -Recurse -Force {Q(backupDir)} -ErrorAction SilentlyContinue",
        };

        return string.Join(Environment.NewLine, lines);
    }

    // Single-quoted PowerShell string: nothing inside is expanded, only ' needs doubling.
    private static string Q(string value) => "'" + value.Replace("'", "''") + "'";
}
