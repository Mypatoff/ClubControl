using System;
using System.Collections.Generic;

namespace ClubControl.Server.Services;

/// <summary>
/// A client that presents a wrong or missing token (token file lost or damaged) is rejected on every heartbeat,
/// i.e. every 5 seconds, forever. Writing each one to the event log buried every other line within minutes, so
/// this lets the hub log a PC's rejection once in a while, and lets the PC card say "auth failed" meanwhile.
/// Memory only (no database column); pure logic with no clock of its own, so it is unit-tested.
/// </summary>
public class AuthRejectionTracker
{
    public static readonly TimeSpan LogEvery = TimeSpan.FromMinutes(10);

    /// <summary>Heartbeats come every 5 s, so a PC that is still being rejected was rejected within this long.</summary>
    public static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(30);

    private sealed class Entry
    {
        public DateTime LastRejectedUtc { get; set; }
        public DateTime LastLoggedUtc { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<Guid, Entry> _entries = new();

    /// <summary>Call for every rejected heartbeat. Returns true when this one should also be written to the event log.</summary>
    public bool Record(Guid machineGuid, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(machineGuid, out var entry))
            {
                _entries[machineGuid] = new Entry { LastRejectedUtc = nowUtc, LastLoggedUtc = nowUtc };
                return true;
            }

            entry.LastRejectedUtc = nowUtc;
            if (nowUtc - entry.LastLoggedUtc < LogEvery)
                return false;

            entry.LastLoggedUtc = nowUtc;
            return true;
        }
    }

    /// <summary>True while this PC's heartbeats are still being rejected.</summary>
    public bool IsRejected(Guid machineGuid, DateTime nowUtc)
    {
        lock (_lock)
            return _entries.TryGetValue(machineGuid, out var entry) && nowUtc - entry.LastRejectedUtc <= ShownFor;
    }
}
