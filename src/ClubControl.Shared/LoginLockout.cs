namespace ClubControl.Shared;

/// <summary>
/// Pure brute-force throttling decisions for the admin password prompt (SPEC section 7). Pure
/// logic shared by the server's login window and its tests; the caller owns where the failed-
/// attempt count and lockout deadline actually live.
/// </summary>
public static class LoginLockout
{
    /// <summary>Wrong attempts allowed before the first lockout kicks in.</summary>
    public const int MaxAttemptsBeforeLockout = 5;

    public static readonly TimeSpan BaseLockoutDuration = TimeSpan.FromSeconds(30);

    /// <summary>Caps exponential backoff at 30s * 16 = 8 minutes, so a locked-out admin is never stuck indefinitely.</summary>
    private const int MaxLockoutMultiplier = 16;

    /// <summary>True if a login attempt should be refused right now because of a prior lockout.</summary>
    public static bool IsLockedOut(DateTime? lockoutUntilUtc, DateTime nowUtc) =>
        lockoutUntilUtc.HasValue && nowUtc < lockoutUntilUtc.Value;

    /// <summary>
    /// Call after a wrong password. Returns the new failed-attempt count and, once it reaches
    /// <see cref="MaxAttemptsBeforeLockout"/>, a lockout end time with exponential backoff (30s,
    /// 60s, 120s, ... capped at 8 minutes) so repeated guessing gets slower, not instantly retryable.
    /// </summary>
    public static (int FailedAttempts, DateTime? LockoutUntilUtc) RecordFailure(int previousFailedAttempts, DateTime nowUtc)
    {
        var failedAttempts = previousFailedAttempts + 1;
        if (failedAttempts < MaxAttemptsBeforeLockout)
            return (failedAttempts, null);

        var lockoutsTriggered = failedAttempts - MaxAttemptsBeforeLockout + 1;
        var multiplier = Math.Min(1 << Math.Min(lockoutsTriggered - 1, 4), MaxLockoutMultiplier);
        var lockoutUntilUtc = nowUtc + TimeSpan.FromTicks(BaseLockoutDuration.Ticks * multiplier);
        return (failedAttempts, lockoutUntilUtc);
    }

    /// <summary>Call after a correct password - the slate is wiped clean.</summary>
    public static (int FailedAttempts, DateTime? LockoutUntilUtc) RecordSuccess() => (0, null);
}
