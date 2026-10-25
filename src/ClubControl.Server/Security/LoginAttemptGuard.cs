using System;
using ClubControl.Shared;

namespace ClubControl.Server.Security;

/// <summary>
/// Process-lifetime brute-force throttle for the admin password prompt. The actual lockout math
/// is <see cref="LoginLockout"/> (pure, unit-tested); this just holds the running counter.
/// Deliberately in-memory rather than persisted in Settings: restarting the whole tray app to wipe
/// the counter is already much higher friction for a local attacker than retrying a password, and
/// this avoids a database schema change for a single process-wide counter.
/// </summary>
public static class LoginAttemptGuard
{
    private static int _failedAttempts;
    private static DateTime? _lockoutUntilUtc;

    public static bool IsLockedOut(out TimeSpan remaining)
    {
        var now = DateTime.UtcNow;
        if (LoginLockout.IsLockedOut(_lockoutUntilUtc, now))
        {
            remaining = _lockoutUntilUtc!.Value - now;
            return true;
        }

        remaining = TimeSpan.Zero;
        return false;
    }

    public static void RecordFailure() =>
        (_failedAttempts, _lockoutUntilUtc) = LoginLockout.RecordFailure(_failedAttempts, DateTime.UtcNow);

    public static void RecordSuccess() =>
        (_failedAttempts, _lockoutUntilUtc) = LoginLockout.RecordSuccess();
}
