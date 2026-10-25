using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class ProcessNameListTests
{
    [Fact]
    public void NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(ProcessNameList.Parse(null));
        Assert.Empty(ProcessNameList.Parse(""));
        Assert.Empty(ProcessNameList.Parse("   "));
    }

    [Fact]
    public void SplitsAndTrimsCsv()
    {
        var names = ProcessNameList.Parse(" steam.exe ,  csgo.exe,  dota2.exe ");
        Assert.Equal(new[] { "steam", "csgo", "dota2" }, names);
    }

    [Fact]
    public void IgnoresEmptyEntriesFromExtraCommas()
    {
        var names = ProcessNameList.Parse("steam.exe,,  ,csgo.exe,");
        Assert.Equal(new[] { "steam", "csgo" }, names);
    }

    [Fact]
    public void StripsPathAndExtension_SoFullPathsMatchBareNames()
    {
        var names = ProcessNameList.Parse(@"C:\Games\Steam\steam.exe");
        Assert.Equal(new[] { "steam" }, names);
    }

    [Fact]
    public void DeduplicatesCaseInsensitively()
    {
        var names = ProcessNameList.Parse("steam.exe,STEAM.EXE,Steam.exe");
        Assert.Equal(new[] { "steam" }, names);
    }

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("EXPLORER")]
    [InlineData("csrss.exe")]
    [InlineData("winlogon.exe")]
    [InlineData("lsass.exe")]
    [InlineData("svchost.exe")]
    [InlineData("ClubControl.Client.exe")]
    public void NeverIncludesWindowsCriticalOrSelfProcesses(string configuredName)
    {
        // The club's previous software's lock screen worked by killing/restarting explorer.exe and
        // was unstable because of it - ClubControl must never be able to do that, no matter what an
        // admin (or a bug) puts in the process list.
        Assert.Empty(ProcessNameList.Parse(configuredName));
    }

    [Fact]
    public void ProtectedProcessIsFilteredOutButOthersInTheListStillGoThrough()
    {
        var names = ProcessNameList.Parse("steam.exe,explorer.exe,csgo.exe");
        Assert.Equal(new[] { "steam", "csgo" }, names);
    }

    [Fact]
    public void MalformedEntries_NeverThrow_AndDoNotBreakParsingOfTheRest()
    {
        // Process.GetProcessesByName simply never matches a garbage name later - CloseAll fails
        // safe without this needing to special-case "invalid" characters.
        var names = ProcessNameList.Parse("steam.exe,bad\0name.exe,csgo.exe");
        Assert.Equal(new[] { "steam", "bad\0name", "csgo" }, names);
    }

    [Fact]
    public void EntryThatIsOnlyAPathSeparator_ResultsInAnEmptyNameAndIsDropped()
    {
        var names = ProcessNameList.Parse(@"steam.exe,C:\,csgo.exe");
        Assert.Equal(new[] { "steam", "csgo" }, names);
    }
}
