using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class LoginLockoutTests
{
    [Fact]
    public void NotLockedOut_WhenThereIsNoLockoutDeadline()
    {
        Assert.False(LoginLockout.IsLockedOut(null, DateTime.UtcNow));
    }

    [Fact]
    public void LockedOut_WhileNowIsBeforeTheDeadline()
    {
        var now = DateTime.UtcNow;
        Assert.True(LoginLockout.IsLockedOut(now.AddSeconds(10), now));
    }

    [Fact]
    public void NotLockedOut_OnceTheDeadlineHasPassed()
    {
        var now = DateTime.UtcNow;
        Assert.False(LoginLockout.IsLockedOut(now.AddSeconds(-1), now));
    }

    [Fact]
    public void FewFailures_DoNotTriggerALockout()
    {
        var now = DateTime.UtcNow;
        var attempts = 0;
        DateTime? lockoutUntil = null;

        for (var i = 0; i < LoginLockout.MaxAttemptsBeforeLockout - 1; i++)
            (attempts, lockoutUntil) = LoginLockout.RecordFailure(attempts, now);

        Assert.Null(lockoutUntil);
        Assert.Equal(LoginLockout.MaxAttemptsBeforeLockout - 1, attempts);
    }

    [Fact]
    public void ReachingTheThreshold_TriggersALockout()
    {
        var now = DateTime.UtcNow;
        var (attempts, lockoutUntil) = LoginLockout.RecordFailure(LoginLockout.MaxAttemptsBeforeLockout - 1, now);

        Assert.Equal(LoginLockout.MaxAttemptsBeforeLockout, attempts);
        Assert.NotNull(lockoutUntil);
        Assert.True(lockoutUntil > now);
    }

    [Fact]
    public void RepeatedFailuresPastTheThreshold_IncreaseTheLockoutDuration()
    {
        var now = DateTime.UtcNow;

        var (_, firstLockout) = LoginLockout.RecordFailure(LoginLockout.MaxAttemptsBeforeLockout - 1, now);
        var (_, secondLockout) = LoginLockout.RecordFailure(LoginLockout.MaxAttemptsBeforeLockout, now);

        var firstDuration = firstLockout!.Value - now;
        var secondDuration = secondLockout!.Value - now;

        Assert.True(secondDuration > firstDuration,
            $"expected backoff to increase: first={firstDuration}, second={secondDuration}");
    }

    [Fact]
    public void Success_ClearsTheCounterAndAnyLockout()
    {
        var (attempts, lockoutUntil) = LoginLockout.RecordSuccess();

        Assert.Equal(0, attempts);
        Assert.Null(lockoutUntil);
    }
}
