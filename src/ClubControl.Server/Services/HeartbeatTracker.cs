using System;
using System.Collections.Concurrent;

namespace ClubControl.Server.Services;

/// <summary>
/// When each PC last sent a heartbeat, kept in memory. Every client sends one every 5 seconds, and writing
/// each of them to the database meant a disk write for every PC every 5 seconds around the clock. Now the
/// hub notes the time here (free), and the database copy (<c>Pc.LastHeartbeatUtc</c>) is refreshed only about
/// once a minute and whenever the PC goes offline. Memory only: after a server restart every PC is
/// marked offline anyway and the first heartbeat fills this in again.
/// </summary>
public class HeartbeatTracker
{
    /// <summary>How old the saved <c>Pc.LastHeartbeatUtc</c> may get before a heartbeat writes it again.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, DateTime> _last = new();

    public void Record(Guid machineGuid, DateTime nowUtc) => _last[machineGuid] = nowUtc;

    /// <summary>The newest heartbeat we know of: the one in memory, or the saved one if it is newer (or none is in memory).</summary>
    public DateTime Latest(Guid machineGuid, DateTime savedUtc) =>
        _last.TryGetValue(machineGuid, out var inMemory) && inMemory > savedUtc ? inMemory : savedUtc;

    /// <summary>True when the saved heartbeat time is old enough that it should be written again.</summary>
    public static bool ShouldSave(DateTime savedUtc, DateTime nowUtc) => nowUtc - savedUtc >= SaveEvery;
}
