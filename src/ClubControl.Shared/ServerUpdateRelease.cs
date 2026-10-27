using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ClubControl.Shared;

public record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>The newest published build: which commit it was built from, when, and the files attached to it.</summary>
public record ReleaseInfo(string CommitSha, DateTime PublishedUtc, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Asset(string name) => Assets.FirstOrDefault(a => a.Name == name);
}

/// <summary>
/// The pure parts of "Update server": reading GitHub's description of the rolling "latest" release,
/// deciding whether it is newer than what is installed, and checking a download against the
/// published checksums. Kept in Shared (no network, no Windows) so the cross-platform tests cover it.
/// </summary>
public static class ServerUpdateRelease
{
    /// <summary>The release the Build workflow keeps up to date with the newest build of main.</summary>
    public const string ApiUrl = "https://api.github.com/repos/Mypatoff/ClubControl/releases/tags/latest";
    public const string PageUrl = "https://github.com/Mypatoff/ClubControl/releases/tag/latest";

    public const string ServerZip = "ClubControl-Server-win-x64.zip";
    public const string ClientZip = "ClubControl-Client-win-x64.zip";

    /// <summary>"hash  file name" lines (sha256sum format) for the files above, made by the Build workflow.</summary>
    public const string ChecksumFile = "SHA256SUMS.txt";

    /// <summary>
    /// A freshly started server writes this file once it is up (database upgraded, sessions restored).
    /// The update script waits for it to tell a healthy new build from one that starts and then dies.
    /// </summary>
    public static readonly string HealthMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClubControl", "server-healthy.flag");

    /// <summary>Reads GitHub's JSON for one release ("body" holds the commit the Build workflow wrote there).</summary>
    public static ReleaseInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var body = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? "" : "";
        var sha = Regex.Match(body, "\\b[0-9a-f]{40}\\b").Value;

        var published = root.TryGetProperty("published_at", out var publishedElement) && publishedElement.TryGetDateTime(out var date)
            ? date.ToUniversalTime()
            : DateTime.MinValue;

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement))
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (name is not null && url is not null)
                    assets.Add(new ReleaseAsset(name, url, asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0));
            }
        }

        return new ReleaseInfo(sha, published, assets);
    }

    /// <summary>
    /// Whether the release was built from a different commit than the installed build
    /// ("1.0.0+f4545fa..." as the assembly reports it). When either side is unknown the answer is
    /// "yes": offering an update that turns out to be the same is better than hiding a real one.
    /// </summary>
    public static bool IsNewer(string installedVersion, string latestCommitSha)
    {
        var plus = installedVersion.IndexOf('+');
        var installed = plus >= 0 ? new string(installedVersion[(plus + 1)..].TakeWhile(Uri.IsHexDigit).ToArray()) : "";

        if (installed.Length < 7 || latestCommitSha.Length < 7)
            return true;

        return !(latestCommitSha.StartsWith(installed, StringComparison.OrdinalIgnoreCase)
                 || installed.StartsWith(latestCommitSha, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>File name -> lower-case SHA-256 from a "hash  name" (or "hash *name") listing.</summary>
    public static Dictionary<string, string> ParseChecksums(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space != 64)
                continue;

            var name = line[(space + 1)..].TrimStart(' ', '*');
            if (name != "")
                result[name] = line[..64].ToLowerInvariant();
        }

        return result;
    }

    public static async Task<string> Sha256OfFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// Whether a zip holds the given exe at its top level (or inside a single wrapper folder), the same
    /// rule <see cref="ClientUpdateScript.FindBuildFolder"/> applies to an extracted zip. Lets the client
    /// update file be checked without extracting 65 MB.
    /// </summary>
    public static bool ZipContainsExe(string zipPath, string exeName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var topLevel = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).Where(n => n != "").ToList();

        if (topLevel.Contains(exeName))
            return true;

        var wrappers = topLevel.Select(n => n.Split('/')[0]).Distinct().ToList();
        return wrappers.Count == 1 && topLevel.Contains(wrappers[0] + "/" + exeName);
    }
}
