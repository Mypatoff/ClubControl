using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class BackupRetentionTests
{
    [Fact]
    public void NothingToDelete_WhenAtOrUnderTheLimit()
    {
        var now = DateTime.UtcNow;
        var timestamps = Enumerable.Range(0, 30).Select(i => now.AddDays(-i));
        Assert.Empty(BackupRetention.FilesToDelete(timestamps));
    }

    [Fact]
    public void KeepsTheNewestThirtyAndDeletesTheRest()
    {
        var now = DateTime.UtcNow;
        var timestamps = Enumerable.Range(0, 35).Select(i => now.AddDays(-i)).ToList();

        var toDelete = BackupRetention.FilesToDelete(timestamps);

        Assert.Equal(5, toDelete.Count);
        Assert.All(toDelete, t => Assert.True(t <= now.AddDays(-29)));
    }

    [Fact]
    public void OrderOfInput_DoesNotMatter()
    {
        var now = DateTime.UtcNow;
        var timestamps = new[] { now, now.AddDays(-5), now.AddDays(-1), now.AddDays(-3) };

        var toDelete = BackupRetention.FilesToDelete(timestamps, keepCount: 2);

        Assert.Equal(2, toDelete.Count);
        Assert.Contains(now.AddDays(-5), toDelete);
        Assert.Contains(now.AddDays(-3), toDelete);
    }

    [Fact]
    public void KeepCountZero_DeletesEverything()
    {
        var now = DateTime.UtcNow;
        var timestamps = new[] { now, now.AddDays(-1) };

        Assert.Equal(2, BackupRetention.FilesToDelete(timestamps, keepCount: 0).Count);
    }
}
