using System;
using System.Collections.Generic;

namespace ClubControl.Server.Services;

/// <summary>
/// Tells "the PC is switched off" apart from "the PC is on but the ClubControl client is gone"
/// (deleted, uninstalled, closed). Both look the same to the server - the heartbeats stop - so
/// <see cref="ClientMissingMonitorService"/> asks the network whether the PC still answers, and this
/// class decides when enough answers have come in to say so. Memory only (no database column, no
/// migration): the answer is only needed for as long as the PC stays in that state. Pure logic with
/// no clock of its own, so it is unit-tested.
/// </summary>
public class ClientMissingTracker
{
    /// <summary>
    /// How long a PC must be offline before it is checked at all. The client is relaunched by its
    /// scheduled task every minute, and a PC that is just booting answers the network long before its
    /// client has started - neither may raise the alert.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(30);

    /// <summary>The PC must answer this many checks in a row, so one stray reply is not enough.</summary>
    public const int ProbesNeeded = 2;

    private sealed class Entry
    {
        public int PoweredOnProbes { get; set; }
        public DateTime LastProbeUtc { get; set; }
        public bool Missing { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<Guid, Entry> _entries = new();

    /// <summary>True while the PC is on but its client is not running.</summary>
    public bool IsMissing(Guid machineGuid)
    {
        lock (_lock)
            return _entries.TryGetValue(machineGuid, out var entry) && entry.Missing;
    }

    /// <summary>Whether it is time to check this PC: offline long enough, and not checked in the last <see cref="ProbeEvery"/>.</summary>
    public bool ShouldProbe(Guid machineGuid, DateTime offlineSinceUtc, DateTime nowUtc)
    {
        if (nowUtc - offlineSinceUtc < Grace)
            return false;

        lock (_lock)
            return !_entries.TryGetValue(machineGuid, out var entry) || nowUtc - entry.LastProbeUtc >= ProbeEvery;
    }

    /// <summary>
    /// Records one check. Returns true only at the moment the PC becomes "client missing" (so it is
    /// logged once). A PC that stops answering - it was switched off - is cleared again.
    /// </summary>
    public bool RecordProbe(Guid machineGuid, bool poweredOn, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(machineGuid, out var entry))
                _entries[machineGuid] = entry = new Entry();

            entry.LastProbeUtc = nowUtc;

            if (!poweredOn)
            {
                entry.PoweredOnProbes = 0;
                entry.Missing = false;
                return false;
            }

            entry.PoweredOnProbes++;
            if (entry.Missing || entry.PoweredOnProbes < ProbesNeeded)
                return false;

            entry.Missing = true;
            return true;
        }
    }

    /// <summary>The client is connected again (or the PC no longer matters): forget everything about it.</summary>
    public void Forget(Guid machineGuid)
    {
        lock (_lock)
            _entries.Remove(machineGuid);
    }
}
