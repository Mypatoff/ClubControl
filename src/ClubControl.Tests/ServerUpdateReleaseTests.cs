using System.IO.Compression;
using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class ServerUpdateReleaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ClubControl.ServerUpdateTests." + Guid.NewGuid().ToString("N"));

    public ServerUpdateReleaseTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Sha = "b329e6f439423ed62ff7ac77725c7cbb54515f77";

    private static readonly string ReleaseJson = $$"""
        {
          "tag_name": "latest",
          "published_at": "2026-10-27T21:03:24Z",
          "body": "Automatically built from the latest commit on main ({{Sha}}). Not a stability guarantee.",
          "assets": [
            { "name": "ClubControl-Server-win-x64.zip", "browser_download_url": "https://example.test/server.zip", "size": 81543882, "updated_at": "2026-10-28T09:15:00Z" },
            { "name": "ClubControl-Client-win-x64.zip", "browser_download_url": "https://example.test/client.zip", "size": 67371020 },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.test/sums.txt", "size": 300 }
          ]
        }
        """;

    // ---- Parse ----

    [Fact]
    public void Parse_ReadsTheCommitTheDateAndTheAssets()
    {
        var release = ServerUpdateRelease.Parse(ReleaseJson);

        Assert.Equal(Sha, release.CommitSha);
        // The server zip's upload date, not the release's creation date: the release is reused for every build.
        Assert.Equal(new DateTime(2026, 10, 28, 9, 15, 0, DateTimeKind.Utc), release.PublishedUtc);
        Assert.Equal(3, release.Assets.Count);
        Assert.Equal("https://example.test/client.zip", release.Asset(ServerUpdateRelease.ClientZip)!.DownloadUrl);
        Assert.Equal(81543882, release.Asset(ServerUpdateRelease.ServerZip)!.Size);
        Assert.Null(release.Asset("missing.zip"));
    }

    [Fact]
    public void Parse_AReleaseWithoutACommitInItsTextHasAnEmptyCommit()
    {
        var release = ServerUpdateRelease.Parse("""{ "body": "hello", "assets": [] }""");

        Assert.Equal("", release.CommitSha);
        Assert.Empty(release.Assets);
    }

    // ---- IsNewer ----

    [Fact]
    public void IsNewer_TheSameCommit_IsNotNewer()
    {
        Assert.False(ServerUpdateRelease.IsNewer("1.0.0+" + Sha, Sha));
        Assert.False(ServerUpdateRelease.IsNewer("1.0.0+" + Sha[..7], Sha)); // a shortened installed version
    }

    [Fact]
    public void IsNewer_ADifferentCommit_IsNewer()
    {
        Assert.True(ServerUpdateRelease.IsNewer("1.0.0+1111111111111111111111111111111111111111", Sha));
    }

    [Theory]
    [InlineData("1.0.0", Sha)]       // a build with no commit in its version
    [InlineData("1.0.0+abc", Sha)]   // too short to compare
    [InlineData("1.0.0+" + Sha, "")] // the release does not say
    public void IsNewer_WhenEitherSideIsUnknown_OffersTheUpdate(string installed, string latest)
    {
        Assert.True(ServerUpdateRelease.IsNewer(installed, latest));
    }

    [Fact]
    public void IsNewer_IgnoresAnythingAfterTheCommit()
    {
        Assert.False(ServerUpdateRelease.IsNewer("1.0.0+" + Sha + ".dirty", Sha));
    }

    // ---- checksums ----

    [Fact]
    public void ParseChecksums_ReadsTheSha256sumFormat()
    {
        var hashA = new string('a', 64);
        var hashB = new string('B', 64);
        var text = $"{hashA}  ClubControl-Server-win-x64.zip\r\n{hashB} *ClubControl-Client-win-x64.zip\r\n\r\nnot a line\r\n";

        var sums = ServerUpdateRelease.ParseChecksums(text);

        Assert.Equal(2, sums.Count);
        Assert.Equal(hashA, sums["ClubControl-Server-win-x64.zip"]);
        Assert.Equal(new string('b', 64), sums["ClubControl-Client-win-x64.zip"]); // lower-cased
    }

    [Fact]
    public async Task Sha256OfFile_MatchesTheKnownHashOfTheContent()
    {
        var path = Path.Combine(_root, "abc.txt");
        await File.WriteAllTextAsync(path, "abc");

        // SHA-256 of the three bytes "abc".
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", await ServerUpdateRelease.Sha256OfFileAsync(path));
    }

    // ---- zip check ----

    private string MakeZip(string name, params string[] entries)
    {
        var path = Path.Combine(_root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
            zip.CreateEntry(entry).Open().Dispose();
        return path;
    }

    [Fact]
    public void ZipContainsExe_AtTheTopLevel_IsAccepted()
    {
        Assert.True(ServerUpdateRelease.ZipContainsExe(MakeZip("a.zip", "App.exe", "App.dll"), "App.exe"));
    }

    [Fact]
    public void ZipContainsExe_InsideOneWrapperFolder_IsAccepted()
    {
        Assert.True(ServerUpdateRelease.ZipContainsExe(MakeZip("b.zip", "publish/App.exe", "publish/App.dll"), "App.exe"));
    }

    [Fact]
    public void ZipContainsExe_WithoutTheExeOrTooDeep_IsRejected()
    {
        Assert.False(ServerUpdateRelease.ZipContainsExe(MakeZip("c.zip", "readme.txt"), "App.exe"));
        Assert.False(ServerUpdateRelease.ZipContainsExe(MakeZip("d.zip", "one/two/App.exe"), "App.exe"));
        Assert.False(ServerUpdateRelease.ZipContainsExe(MakeZip("e.zip", "x/App.exe", "y/other.txt"), "App.exe"));
    }
}
