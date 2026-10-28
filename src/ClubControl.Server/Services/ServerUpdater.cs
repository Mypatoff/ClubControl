using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Shared;

namespace ClubControl.Server.Services;

/// <summary>Where a downloaded, checked build is waiting for the swap.</summary>
public record StagedServerUpdate(string SourceDir, string ExtractDir, string InstallDir);

/// <summary>
/// "Update server": looks up the newest build on GitHub (the rolling "latest" release the Build workflow
/// publishes), downloads and checks it, refreshes the client update file for the gaming PCs, and hands
/// the actual file swap to a short PowerShell script, because a running .exe cannot overwrite its own
/// files. It reuses <see cref="ClientUpdateScript"/>, which already does exactly that for the client:
/// wait for the old process to exit, back the folder up, copy the new build over it, start it, and put
/// the old build back if the new one does not report healthy.
/// </summary>
public class ServerUpdater
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        // No overall time limit: the files are large and the admin can cancel. GitHub's API requires a User-Agent.
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ClubControl-Updater");
        return http;
    }

    /// <summary>The build this server runs, as the assembly reports it ("1.0.0+&lt;commit&gt;").</summary>
    public static string InstalledVersion =>
        typeof(ServerUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    /// <summary>A short form for the screen: "1.0.0+f4545fa".</summary>
    public static string ShortVersion(string version)
    {
        var plus = version.IndexOf('+');
        return plus >= 0 && version.Length > plus + 8 ? version[..(plus + 8)] : version;
    }

    public static string InstallDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// The swap runs as the same user as the server, so the install folder must be one that user can
    /// write to (a folder from the zip, like E:\ClubControl-Server). C:\Program Files is not: that one
    /// is updated by running ClubControl-Setup.exe, which asks for administrator rights itself.
    /// </summary>
    public static bool InstallFolderIsWritable()
    {
        try
        {
            var probe = Path.Combine(InstallDir, ".update-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<ReleaseInfo> CheckAsync(CancellationToken ct)
    {
        var json = await Http.GetStringAsync(ServerUpdateRelease.ApiUrl, ct);
        return ServerUpdateRelease.Parse(json);
    }

    /// <summary>
    /// Downloads the server and client zips, checks both against the published checksums, unpacks the
    /// server build, and replaces the client update file (<see cref="ClientUpdateFile.StagePath"/>) with
    /// the new client zip. Nothing of the running server is touched yet.
    /// </summary>
    public async Task<StagedServerUpdate> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct)
    {
        var serverAsset = release.Asset(ServerUpdateRelease.ServerZip) ?? throw new InvalidDataException($"The release has no {ServerUpdateRelease.ServerZip}.");
        var clientAsset = release.Asset(ServerUpdateRelease.ClientZip) ?? throw new InvalidDataException($"The release has no {ServerUpdateRelease.ClientZip}.");
        var sumsAsset = release.Asset(ServerUpdateRelease.ChecksumFile) ?? throw new InvalidDataException($"The release has no {ServerUpdateRelease.ChecksumFile}, so the download cannot be checked.");

        var workDir = Path.Combine(Path.GetTempPath(), "ClubControl.Server.Update");
        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);
        Directory.CreateDirectory(workDir);

        var checksums = ServerUpdateRelease.ParseChecksums(await Http.GetStringAsync(sumsAsset.DownloadUrl, ct));
        if (!checksums.TryGetValue(serverAsset.Name, out var serverHash) || !checksums.TryGetValue(clientAsset.Name, out var clientHash))
            throw new InvalidDataException($"{ServerUpdateRelease.ChecksumFile} does not list both zips.");

        var totalBytes = Math.Max(1, serverAsset.Size + clientAsset.Size);
        var serverZip = Path.Combine(workDir, serverAsset.Name);
        var clientZip = Path.Combine(workDir, clientAsset.Name);

        await DownloadAndCheckAsync(serverAsset, serverZip, serverHash, done => progress.Report(done / (double)totalBytes), ct);
        await DownloadAndCheckAsync(clientAsset, clientZip, clientHash, done => progress.Report((serverAsset.Size + done) / (double)totalBytes), ct);

        // Both zips must hold what they claim before anything is replaced.
        if (!ServerUpdateRelease.ZipContainsExe(clientZip, "ClubControl.Client.exe"))
            throw new InvalidDataException("The client zip does not contain ClubControl.Client.exe.");

        var extractDir = Path.Combine(workDir, "extract");
        ZipFile.ExtractToDirectory(serverZip, extractDir);
        var exeName = Path.GetFileName(Environment.ProcessPath) ?? "ClubControl.Server.exe";
        var sourceDir = ClientUpdateScript.FindBuildFolder(extractDir, exeName);

        StageClientZip(clientZip);

        File.Delete(serverZip);
        File.Delete(clientZip);
        return new StagedServerUpdate(sourceDir, extractDir, InstallDir);
    }

    /// <summary>Starts the swap script. The caller must close the server right after, the script waits for that.</summary>
    public static void StartSwap(StagedServerUpdate staged)
    {
        var tempDir = Path.GetTempPath();
        var exeName = Path.GetFileName(Environment.ProcessPath) ?? "ClubControl.Server.exe";
        var backupDir = Path.Combine(tempDir, "ClubControl.Server.Backup");
        var scriptPath = Path.Combine(tempDir, "ClubControl.Server.Update.ps1");

        // The script also keeps a copy of club.db and puts it back if the new build never reports healthy: that
        // build may already have upgraded the database, and the old build cannot run on a newer one.
        var databaseCopy = Path.Combine(tempDir, "ClubControl.Server.club-before-update.db");

        File.WriteAllText(scriptPath, ClientUpdateScript.Build(
            Environment.ProcessId, staged.SourceDir, staged.ExtractDir, staged.InstallDir, exeName, backupDir, ServerUpdateRelease.HealthMarkerPath,
            databasePath: DbPaths.DatabasePath, databaseCopyPath: databaseCopy));

        Process.Start(new ProcessStartInfo("powershell.exe", $"-WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static async Task DownloadAndCheckAsync(ReleaseAsset asset, string path, string expectedSha256, Action<long> bytesDone, CancellationToken ct)
    {
        using (var response = await Http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(path);

            var buffer = new byte[128 * 1024];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                bytesDone(done);
            }
        }

        var actual = await ServerUpdateRelease.Sha256OfFileAsync(path);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidDataException($"{asset.Name} does not match its published checksum, so it was discarded.");
        }
    }

    /// <summary>Replaces the zip the gaming PCs download on "Update client", so one click updates both.</summary>
    private static void StageClientZip(string newClientZip)
    {
        var target = ClientUpdateFile.StagePath;

        // Copy next to the target first, then swap: a PC downloading right now never sees a half-written file.
        var partial = target + ".new";
        File.Copy(newClientZip, partial, overwrite: true);
        File.Move(partial, target, overwrite: true);

        // An older copy in the other place would only confuse; the new one is what gets handed out.
        foreach (var other in new[] { ClientUpdateFile.ServerFolderPath, ClientUpdateFile.LegacyPath })
        {
            try
            {
                if (!string.Equals(other, target, StringComparison.OrdinalIgnoreCase) && File.Exists(other))
                    File.Delete(other);
            }
            catch (Exception)
            {
                // Not allowed to delete it: harmless, the newer file is the one served.
            }
        }
    }
}
