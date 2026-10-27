using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class ClientUpdateScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ClubControl.UpdateScriptTests." + Guid.NewGuid().ToString("N"));

    public ClientUpdateScriptTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string MakeDir(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    // ---- FindBuildFolder: what the updater accepts out of an extracted zip ----

    [Fact]
    public void FindBuildFolder_ExeAtZipRoot_UsesTheRoot()
    {
        var extract = MakeDir("a");
        File.WriteAllText(Path.Combine(extract, "App.exe"), "");

        Assert.Equal(extract, ClientUpdateScript.FindBuildFolder(extract, "App.exe"));
    }

    [Fact]
    public void FindBuildFolder_ExeInsideASingleWrapperFolder_UsesThatFolder()
    {
        var extract = MakeDir("b");
        var wrapper = MakeDir("b", "publish");
        File.WriteAllText(Path.Combine(wrapper, "App.exe"), "");

        Assert.Equal(wrapper, ClientUpdateScript.FindBuildFolder(extract, "App.exe"));
    }

    [Fact]
    public void FindBuildFolder_NoExe_Throws()
    {
        var extract = MakeDir("c");
        File.WriteAllText(Path.Combine(extract, "readme.txt"), "");

        Assert.Throws<InvalidDataException>(() => ClientUpdateScript.FindBuildFolder(extract, "App.exe"));
    }

    [Fact]
    public void FindBuildFolder_ExeOnlyDeeperThanOneWrapper_Throws()
    {
        var extract = MakeDir("d");
        var deep = MakeDir("d", "one", "two");
        File.WriteAllText(Path.Combine(deep, "App.exe"), "");

        Assert.Throws<InvalidDataException>(() => ClientUpdateScript.FindBuildFolder(extract, "App.exe"));
    }

    [Fact]
    public void FindBuildFolder_ExeInOneOfSeveralFolders_Throws()
    {
        var extract = MakeDir("e");
        File.WriteAllText(Path.Combine(MakeDir("e", "x"), "App.exe"), "");
        MakeDir("e", "y");

        Assert.Throws<InvalidDataException>(() => ClientUpdateScript.FindBuildFolder(extract, "App.exe"));
    }

    // ---- Build: the generated PowerShell ----

    private static string BuildScript(string installDir = @"C:\Club\Client", int exitWait = 60, int health = 90) =>
        ClientUpdateScript.Build(1234, @"C:\Temp\src", @"C:\Temp\extract", installDir, "App.exe",
            @"C:\Temp\backup", @"C:\Data\healthy.flag", exitWait, health);

    [Fact]
    public void Build_NeverAssignsTheReadOnlyPidVariable()
    {
        // $pid is a read-only built-in; assigning it makes the wait loop watch PowerShell itself forever.
        var script = BuildScript();

        Assert.DoesNotContain("$pid", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$clientPid = 1234", script);
    }

    [Fact]
    public void Build_WaitsForExitWithTheGivenTimeoutAndHealthDeadline()
    {
        var script = BuildScript(exitWait: 7, health: 11);

        Assert.Contains("Wait-Process -Id $clientPid -Timeout 7", script);
        Assert.Contains("AddSeconds(11)", script);
    }

    [Fact]
    public void Build_BacksUpBeforeCopyingAndRestoresIfTheNewBuildIsNotHealthy()
    {
        var script = BuildScript();

        var backup = script.IndexOf("robocopy 'C:\\Club\\Client' 'C:\\Temp\\backup' /E", StringComparison.Ordinal);
        var copy = script.IndexOf("robocopy 'C:\\Temp\\src' 'C:\\Club\\Client'", StringComparison.Ordinal);
        var restore = script.IndexOf("robocopy 'C:\\Temp\\backup' 'C:\\Club\\Client' /MIR", StringComparison.Ordinal);

        Assert.True(backup >= 0 && copy > backup, "the install folder must be backed up before the new build is copied over it");
        Assert.True(restore >= 0, "a restore path must exist");
        Assert.Contains("if (-not (Test-Path 'C:\\Data\\healthy.flag')) { Restore-OldBuild; exit 3 }", script);
    }

    [Fact]
    public void Build_QuotesPathsSoAnApostropheCannotBreakOutOfTheString()
    {
        var script = BuildScript(installDir: @"C:\Bob's Club\Client");

        Assert.Contains(@"'C:\Bob''s Club\Client'", script);
    }
}
