using System;
using System.Collections.Generic;
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
    /// <para>
    /// If the backup itself fails (disk full, folder locked) nothing has been replaced yet, so the old
    /// build is simply started again and the script stops: a rollback from a half-made backup would
    /// delete files instead of restoring them.
    /// </para>
    /// <para>
    /// The server passes <paramref name="databasePath"/> and <paramref name="databaseCopyPath"/>: its
    /// database is copied once the old server has exited, and put back on a rollback. A new build may
    /// upgrade club.db before it fails, and the old build must not be started on a newer database.
    /// </para>
    /// </summary>
    public static string Build(int clientPid, string sourceDir, string extractDir, string installDir, string exeName,
        string backupDir, string markerPath, int exitWaitSeconds = 60, int healthSeconds = 90,
        string? databasePath = null, string? databaseCopyPath = null)
    {
        var exePath = Path.Combine(installDir, exeName);
        var processName = Path.GetFileNameWithoutExtension(exeName);

        var withDatabase = databasePath is not null && databaseCopyPath is not null;

        var lines = new List<string>
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
        };

        if (withDatabase)
            lines.AddRange(DatabaseSteps(databasePath!, databaseCopyPath!, restore: true, indent: "    "));

        lines.AddRange(new[]
        {
            $"    robocopy {Q(backupDir)} {Q(installDir)} /MIR | Out-Null",
            $"    Start-Process -FilePath {Q(exePath)} -WorkingDirectory {Q(installDir)}",
            "}",

            $"if (Test-Path {Q(backupDir)}) {{ Remove-Item -Recurse -Force {Q(backupDir)} }}",
            $"robocopy {Q(installDir)} {Q(backupDir)} /E | Out-Null",
            // robocopy: 0-7 mean "copied (some/none)", 8 and up mean something failed. The install folder is still
            // untouched here, so the old build just starts again; Restore-OldBuild would mirror a broken backup over it.
            $"if ($LASTEXITCODE -ge 8) {{ Remove-Item -Recurse -Force {Q(backupDir)} -ErrorAction SilentlyContinue; Start-Process -FilePath {Q(exePath)} -WorkingDirectory {Q(installDir)}; exit 4 }}",
        });

        if (withDatabase)
        {
            lines.Add("try {");
            lines.AddRange(DatabaseSteps(databasePath!, databaseCopyPath!, restore: false, indent: "    "));
            lines.Add($"}} catch {{ Remove-Item -Recurse -Force {Q(backupDir)} -ErrorAction SilentlyContinue; Start-Process -FilePath {Q(exePath)} -WorkingDirectory {Q(installDir)}; exit 4 }}");
        }

        lines.AddRange(new[]
        {
            $"robocopy {Q(sourceDir)} {Q(installDir)} /E /IS /IT | Out-Null",
            "if ($LASTEXITCODE -ge 8) { Restore-OldBuild; exit 2 }",

            $"Remove-Item {Q(markerPath)} -Force -ErrorAction SilentlyContinue",
            $"Start-Process -FilePath {Q(exePath)} -WorkingDirectory {Q(installDir)}",
            $"$deadline = (Get-Date).AddSeconds({healthSeconds})",
            $"while (-not (Test-Path {Q(markerPath)}) -and (Get-Date) -lt $deadline) {{ Start-Sleep -Seconds 1 }}",
            $"if (-not (Test-Path {Q(markerPath)})) {{ Restore-OldBuild; exit 3 }}",

            $"Remove-Item -Recurse -Force {Q(extractDir)} -ErrorAction SilentlyContinue",
            $"Remove-Item -Recurse -Force {Q(backupDir)} -ErrorAction SilentlyContinue",
        });

        if (withDatabase)
        {
            foreach (var suffix in DatabaseFileSuffixes)
                lines.Add($"Remove-Item {Q(databaseCopyPath! + suffix)} -Force -ErrorAction SilentlyContinue");
        }

        return string.Join(Environment.NewLine, lines);
    }

    // The database file plus the files SQLite keeps beside it while the database is open (or after a crash).
    private static readonly string[] DatabaseFileSuffixes = { "", "-wal", "-shm" };

    /// <summary>Copies the database aside (<paramref name="restore"/> false) or puts that copy back (true). A file that is not there is removed from the target, so the two never mix.</summary>
    private static IEnumerable<string> DatabaseSteps(string databasePath, string copyPath, bool restore, string indent)
    {
        foreach (var suffix in DatabaseFileSuffixes)
        {
            var from = Q((restore ? copyPath : databasePath) + suffix);
            var to = Q((restore ? databasePath : copyPath) + suffix);
            // Making the copy must succeed (a failure aborts the update). Putting it back must not stop the script:
            // the old build still has to be started, whatever happens to the copy.
            var onError = restore ? "Continue" : "Stop";
            yield return $"{indent}if (Test-Path {from}) {{ Copy-Item {from} {to} -Force -ErrorAction {onError} }} else {{ Remove-Item {to} -Force -ErrorAction SilentlyContinue }}";
        }
    }

    // Single-quoted PowerShell string: nothing inside is expanded, only ' needs doubling.
    private static string Q(string value) => "'" + value.Replace("'", "''") + "'";
}
