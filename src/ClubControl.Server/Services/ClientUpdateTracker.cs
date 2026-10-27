using System;
using System.Collections.Generic;
using System.Linq;

namespace ClubControl.Server.Services;

public enum ClientUpdateStatus
{
    /// <summary>Chosen for the update but its turn has not come yet: the PCs are sent the command one at a time.</summary>
    Waiting,
    /// <summary>The command was sent and the PC has not reported back yet.</summary>
    Updating,
    /// <summary>The PC now reports a different client version.</summary>
    Updated,
    /// <summary>The PC restarted but reports the same version: it was already that build, or the update was rolled back.</summary>
    Unchanged,
    /// <summary>Nothing happened within <see cref="ClientUpdateTracker.Timeout"/>: the PC neither restarted nor changed version.</summary>
    NoResponse,
}

/// <param name="VersionBefore">The version the PC reported when the command was sent ("" if unknown).</param>
/// <param name="VersionNow">The version it reports now (same as before until it changes).</param>
public record ClientUpdateInfo(ClientUpdateStatus Status, string VersionBefore, string VersionNow, DateTime SinceUtc);

/// <summary>
/// Answers "did Update client work on that PC?" without any change to the client: the server remembers
/// which PCs it sent the command to and judges the result from what each PC reports afterwards - a
/// different client version (success), or a new connection with the same version (it restarted, but
/// nothing changed). Memory only: it is only meaningful for the few minutes around an update, and a
/// database column would need a migration for no benefit. Pure logic with no clock of its own, so it
/// is unit-tested.
/// </summary>
public class ClientUpdateTracker
{
    /// <summary>
    /// Download + extract + wait for the old client to exit + start + the 90 s health check of the
    /// update script, with room for slow Wi-Fi.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    // How long a finished result stays on the PC card. A failure stays until the PC is updated again or changes version.
    public static readonly TimeSpan UpdatedShownFor = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan UnchangedShownFor = TimeSpan.FromMinutes(10);

    private sealed class Entry
    {
        public required ClientUpdateStatus Status { get; set; }
        public required string VersionBefore { get; init; }
        public required string VersionNow { get; set; }
        public required string ConnectionAtSend { get; init; }
        public required DateTime SinceUtc { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<Guid, Entry> _entries = new();

    /// <summary>Call when a PC is chosen for the update but its command is not sent yet.</summary>
    public void Queued(Guid machineGuid, string versionBefore, DateTime nowUtc)
    {
        lock (_lock)
        {
            _entries[machineGuid] = new Entry
            {
                Status = ClientUpdateStatus.Waiting,
                VersionBefore = versionBefore,
                VersionNow = versionBefore,
                ConnectionAtSend = "",
                SinceUtc = nowUtc,
            };
        }
    }

    /// <summary>A waiting PC that will not be sent the command after all (it went offline): forget it.</summary>
    public void Cancel(Guid machineGuid)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(machineGuid, out var entry) && entry.Status == ClientUpdateStatus.Waiting)
                _entries.Remove(machineGuid);
        }
    }

    /// <summary>Call when the update command has just been sent to a PC over <paramref name="connectionId"/>.</summary>
    public void Started(Guid machineGuid, string versionBefore, string connectionId, DateTime nowUtc)
    {
        lock (_lock)
        {
            _entries[machineGuid] = new Entry
            {
                Status = ClientUpdateStatus.Updating,
                VersionBefore = versionBefore,
                VersionNow = versionBefore,
                ConnectionAtSend = connectionId,
                SinceUtc = nowUtc,
            };
        }
    }

    /// <summary>
    /// Call on every heartbeat. Returns the new result the moment one is known (so it can be logged
    /// once), otherwise null - including for every heartbeat of a PC that was not just updated.
    /// </summary>
    public ClientUpdateInfo? OnHeartbeat(Guid machineGuid, string version, string connectionId, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(machineGuid, out var entry))
                return null;

            if (entry.Status is ClientUpdateStatus.Updated)
                return null;

            // A different version is success, even late (after "no response") or after "unchanged".
            if (version != entry.VersionBefore)
                return Finish(entry, ClientUpdateStatus.Updated, version, nowUtc);

            if (entry.Status == ClientUpdateStatus.Updating && connectionId != entry.ConnectionAtSend)
                return Finish(entry, ClientUpdateStatus.Unchanged, version, nowUtc);

            return null;
        }
    }

    /// <summary>The PCs whose update got no answer in time; each is returned once, then stays as "no response".</summary>
    public List<(Guid MachineGuid, ClientUpdateInfo Info)> CheckTimeouts(DateTime nowUtc)
    {
        lock (_lock)
        {
            var timedOut = _entries
                .Where(e => e.Value.Status == ClientUpdateStatus.Updating && nowUtc - e.Value.SinceUtc >= Timeout)
                .ToList();

            return timedOut
                .Select(e => (e.Key, Finish(e.Value, ClientUpdateStatus.NoResponse, e.Value.VersionNow, nowUtc)))
                .ToList();
        }
    }

    /// <summary>What to show on the PC's card right now, or null if there is nothing (old results fade away).</summary>
    public ClientUpdateInfo? Get(Guid machineGuid, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(machineGuid, out var entry))
                return null;

            var age = nowUtc - entry.SinceUtc;
            if (entry.Status == ClientUpdateStatus.Updated && age > UpdatedShownFor)
                return null;
            if (entry.Status == ClientUpdateStatus.Unchanged && age > UnchangedShownFor)
                return null;

            return ToInfo(entry);
        }
    }

    /// <summary>The event-log line for a result, e.g. "PC #5 updated: v1.0.0+abc → v1.0.0+def".</summary>
    public static string LogMessage(string pcLabel, ClientUpdateInfo info) => info.Status switch
    {
        ClientUpdateStatus.Updated =>
            $"{pcLabel} updated: {Show(info.VersionBefore)} -> {Show(info.VersionNow)}",
        ClientUpdateStatus.Unchanged =>
            $"{pcLabel} restarted but is still on {Show(info.VersionNow)}: it was already this build, or the update was rolled back (see client-log.txt on that PC)",
        ClientUpdateStatus.NoResponse =>
            $"{pcLabel} did not update: no restart and no new version after {(int)Timeout.TotalMinutes} minutes (still on {Show(info.VersionNow)}). See client-log.txt on that PC",
        _ => $"{pcLabel} is updating",
    };

    /// <summary>Whether a result is a problem the admin has to look at (shown as a warning in the log).</summary>
    public static bool IsProblem(ClientUpdateInfo info) =>
        info.Status is ClientUpdateStatus.Unchanged or ClientUpdateStatus.NoResponse;

    private static string Show(string version) => version == "" ? "an unknown version" : "v" + version;

    private static ClientUpdateInfo Finish(Entry entry, ClientUpdateStatus status, string versionNow, DateTime nowUtc)
    {
        entry.Status = status;
        entry.VersionNow = versionNow;
        entry.SinceUtc = nowUtc;
        return ToInfo(entry);
    }

    private static ClientUpdateInfo ToInfo(Entry entry) =>
        new(entry.Status, entry.VersionBefore, entry.VersionNow, entry.SinceUtc);
}
