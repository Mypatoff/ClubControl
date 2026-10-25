using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class BackupRetentionTests
{
    private static BackupRetention.BackupEntry Entry(DateTime date) =>
        new($"club_{date:yyyy-MM-dd}.db", date.Date);

    [Fact]
    public void NothingToDelete_WhenAtOrUnderTheLimit()
    {
        var now = DateTime.UtcNow;
        var entries = Enumerable.Range(0, 30).Select(i => Entry(now.AddDays(-i)));
        Assert.Empty(BackupRetention.FilesToDelete(entries));
    }

    [Fact]
    public void KeepsTheNewestThirtyAndDeletesTheRest()
    {
        var now = DateTime.UtcNow;
        var entries = Enumerable.Range(0, 35).Select(i => Entry(now.AddDays(-i))).ToList();

        var toDelete = BackupRetention.FilesToDelete(entries);

        Assert.Equal(5, toDelete.Count);
        foreach (var fileName in toDelete)
        {
            var date = BackupRetention.TryParseBackupDate(fileName)!.Value;
            Assert.True(date <= now.AddDays(-29).Date);
        }
    }

    [Fact]
    public void OrderOfInput_DoesNotMatter()
    {
        var now = DateTime.UtcNow;
        var entries = new[] { Entry(now), Entry(now.AddDays(-5)), Entry(now.AddDays(-1)), Entry(now.AddDays(-3)) };

        var toDelete = BackupRetention.FilesToDelete(entries, keepCount: 2);

        Assert.Equal(2, toDelete.Count);
        Assert.Contains(Entry(now.AddDays(-5)).FileName, toDelete);
        Assert.Contains(Entry(now.AddDays(-3)).FileName, toDelete);
    }

    [Fact]
    public void KeepCountZero_DeletesEverything()
    {
        var now = DateTime.UtcNow;
        var entries = new[] { Entry(now), Entry(now.AddDays(-1)) };

        Assert.Equal(2, BackupRetention.FilesToDelete(entries, keepCount: 0).Count);
    }

    [Fact]
    public void SameDate_DifferentFileNames_AreDecidedIndividuallyNotAsATie()
    {
        // Not a case the real backup service produces (one file per day), but the selection logic
        // must not silently keep-or-delete both together just because their dates match - each
        // entry is identified by its own file name.
        var now = DateTime.UtcNow.Date;
        var entries = new[]
        {
            new BackupRetention.BackupEntry("club_2026-10-24.db", now),
            new BackupRetention.BackupEntry("club_2026-10-24_dup.db", now),
        };

        var toDelete = BackupRetention.FilesToDelete(entries, keepCount: 1);

        Assert.Single(toDelete);
    }

    [Theory]
    [InlineData("club_2026-10-24.db", true)]
    [InlineData("club_2026-1-2.db", false)]
    [InlineData("club.db", false)]
    [InlineData("club_2026-10-24.db.bak", false)]
    [InlineData("notes.txt", false)]
    public void TryParseBackupDate_OnlyAcceptsTheExactExpectedPattern(string fileName, bool shouldParse)
    {
        Assert.Equal(shouldParse, BackupRetention.TryParseBackupDate(fileName).HasValue);
    }
}
