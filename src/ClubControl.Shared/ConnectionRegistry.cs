using System.Collections.Concurrent;

namespace ClubControl.Shared;

/// <summary>
/// Tracks the single live connection id for each machine and keeps both directions of that
/// mapping consistent:
/// - A fast reconnect (new connection registers before the old one's disconnect is observed)
///   must not let that old connection's eventual disconnect evict the new, correct mapping.
/// - A disconnect must only clear a machine's mapping if it is still the active connection for
///   that machine.
/// Pure, side-effect-free beyond its own tables, so the transport-specific code (ClubHub, which
/// decides when to mark a Pc offline) can stay a thin wrapper around this and the mapping logic
/// itself can be unit tested without a live SignalR server.
/// </summary>
public class ConnectionRegistry<TMachineId> where TMachineId : notnull
{
    private readonly ConcurrentDictionary<string, TMachineId> _connectionToMachine = new();
    private readonly ConcurrentDictionary<TMachineId, string> _machineToConnection = new();

    /// <summary>
    /// Registers connectionId as the live connection for machineId. If a different connection was
    /// already registered for this machine, that old connection id is returned (and immediately
    /// evicted from this registry) so the caller can know a stale connection existed.
    /// </summary>
    public string? Register(TMachineId machineId, string connectionId)
    {
        _connectionToMachine[connectionId] = machineId;

        string? previousConnectionId = null;
        if (_machineToConnection.TryGetValue(machineId, out var existingConnectionId) && existingConnectionId != connectionId)
        {
            previousConnectionId = existingConnectionId;
            _connectionToMachine.TryRemove(existingConnectionId, out _);
        }

        _machineToConnection[machineId] = connectionId;
        return previousConnectionId;
    }

    public bool TryGetConnectionId(TMachineId machineId, out string connectionId) =>
        _machineToConnection.TryGetValue(machineId, out connectionId!);

    /// <summary>Forcibly evicts a machine's mapping, e.g. when a heartbeat-timeout decides it is offline.</summary>
    public void Forget(TMachineId machineId)
    {
        if (_machineToConnection.TryRemove(machineId, out var connectionId))
            _connectionToMachine.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// Call when a connection disconnects. Found is false if this connection id was never
    /// registered. WasActiveConnection is false when a newer reconnect already superseded this
    /// connection via <see cref="Register"/> - the caller must NOT treat that case as "the machine
    /// went offline" (doing so would evict the live mapping and misreport a connected PC as offline).
    /// </summary>
    public (bool Found, TMachineId MachineId, bool WasActiveConnection) Disconnect(string connectionId)
    {
        if (!_connectionToMachine.TryRemove(connectionId, out var machineId))
            return (false, default!, false);

        var wasActive = _machineToConnection.TryRemove(new KeyValuePair<TMachineId, string>(machineId, connectionId));
        return (true, machineId, wasActive);
    }
}
