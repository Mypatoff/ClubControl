using System;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Services;

/// <summary>
/// A client the server has never seen is added as a "New PC" on its first heartbeat (the admin then gives it a
/// number and tariff). Anyone on the network can send such a heartbeat, so only this many PCs may wait for setup at
/// once: a flood of made-up machine ids cannot fill the panel and the database.
/// </summary>
public static class NewPcLimit
{
    /// <summary>The most PCs that may be waiting for a number at the same time (a fresh club with 30 PCs installs them in small groups).</summary>
    public const int MaxUnassigned = 10;

    /// <summary>The "ignored a new PC" line goes to the event log at most this often.</summary>
    public static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(1);

    private static readonly object WarnLock = new();
    private static DateTime _lastWarnUtc = DateTime.MinValue;

    public static bool IsFull(int unassignedCount) => unassignedCount >= MaxUnassigned;

    /// <summary>PCs that connected but have no number yet (manual resources have no heartbeat and never count).</summary>
    public static Task<int> CountUnassignedAsync(ClubDbContext db) =>
        db.Pcs.CountAsync(p => !p.IsManual && p.Number == null);

    /// <summary>True once per <see cref="WarnEvery"/>, so a flood writes one log line, not one per heartbeat.</summary>
    public static bool ShouldWarn(DateTime nowUtc)
    {
        lock (WarnLock)
        {
            if (nowUtc - _lastWarnUtc < WarnEvery)
                return false;

            _lastWarnUtc = nowUtc;
            return true;
        }
    }

    /// <summary>For tests: forget the last warning.</summary>
    public static void ResetWarning()
    {
        lock (WarnLock)
            _lastWarnUtc = DateTime.MinValue;
    }
}
