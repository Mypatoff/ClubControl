using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class ConnectionRegistryTests
{
    [Fact]
    public void Register_ThenTryGetConnectionId_FindsTheConnection()
    {
        var registry = new ConnectionRegistry<Guid>();
        var machine = Guid.NewGuid();

        registry.Register(machine, "conn-1");

        Assert.True(registry.TryGetConnectionId(machine, out var connectionId));
        Assert.Equal("conn-1", connectionId);
    }

    [Fact]
    public void UnknownMachine_TryGetConnectionId_ReturnsFalse()
    {
        var registry = new ConnectionRegistry<Guid>();
        Assert.False(registry.TryGetConnectionId(Guid.NewGuid(), out _));
    }

    [Fact]
    public void Disconnect_UnknownConnection_ReturnsNotFound()
    {
        var registry = new ConnectionRegistry<Guid>();
        var (found, _, wasActive) = registry.Disconnect("never-registered");

        Assert.False(found);
        Assert.False(wasActive);
    }

    [Fact]
    public void Disconnect_ActiveConnection_ClearsTheMapping()
    {
        var registry = new ConnectionRegistry<Guid>();
        var machine = Guid.NewGuid();
        registry.Register(machine, "conn-1");

        var (found, disconnectedMachine, wasActive) = registry.Disconnect("conn-1");

        Assert.True(found);
        Assert.True(wasActive);
        Assert.Equal(machine, disconnectedMachine);
        Assert.False(registry.TryGetConnectionId(machine, out _));
    }

    [Fact]
    public void FastReconnect_SupersedesTheOldConnection_RegisterReturnsIt()
    {
        var registry = new ConnectionRegistry<Guid>();
        var machine = Guid.NewGuid();

        registry.Register(machine, "conn-old");
        var previous = registry.Register(machine, "conn-new");

        Assert.Equal("conn-old", previous);
        Assert.True(registry.TryGetConnectionId(machine, out var current));
        Assert.Equal("conn-new", current);
    }

    [Fact]
    public void FastReconnect_ThenLateDisconnectOfOldConnection_DoesNotEvictTheNewMapping()
    {
        // This is the exact bug: a client reconnects (new SignalR connection, re-Heartbeats)
        // before the server observes the old socket's disconnect. Register() already forgot the
        // old connection id the moment the new one took over, so when that old connection's
        // disconnect event finally fires, it is a clean no-op - not something that could wipe out
        // the machine's current (correct, newer) connection.
        var registry = new ConnectionRegistry<Guid>();
        var machine = Guid.NewGuid();

        registry.Register(machine, "conn-old");
        registry.Register(machine, "conn-new");

        var (found, _, wasActive) = registry.Disconnect("conn-old");

        Assert.False(found); // already forgotten at Register() time - nothing to disconnect
        Assert.False(wasActive);

        Assert.True(registry.TryGetConnectionId(machine, out var current));
        Assert.Equal("conn-new", current);
    }

    [Fact]
    public void DisconnectingTheOldConnection_DoesNotLeaveItAddressable()
    {
        var registry = new ConnectionRegistry<Guid>();
        var machine = Guid.NewGuid();

        registry.Register(machine, "conn-old");
        registry.Register(machine, "conn-new");

        // "conn-old" must never be resolvable again, from either direction.
        var (found, _, _) = registry.Disconnect("conn-old");
        Assert.False(found);
    }

    [Fact]
    public void DisconnectingTheCurrentConnection_AfterAReconnectSupersededAnEvenOlderOne_StillWorks()
    {
        var registry = new ConnectionRegistry<Guid>();
        var machine = Guid.NewGuid();

        registry.Register(machine, "conn-1");
        registry.Register(machine, "conn-2");

        var (found, _, wasActive) = registry.Disconnect("conn-2");

        Assert.True(found);
        Assert.True(wasActive);
        Assert.False(registry.TryGetConnectionId(machine, out _));
    }

    [Fact]
    public void Forget_EvictsTheMappingFromBothDirections()
    {
        var registry = new ConnectionRegistry<Guid>();
        var machine = Guid.NewGuid();
        registry.Register(machine, "conn-1");

        registry.Forget(machine);

        Assert.False(registry.TryGetConnectionId(machine, out _));
        var (found, _, _) = registry.Disconnect("conn-1");
        Assert.False(found);
    }

    [Fact]
    public void Forget_UnknownMachine_IsANoOp()
    {
        var registry = new ConnectionRegistry<Guid>();
        registry.Forget(Guid.NewGuid()); // must not throw
    }

    [Fact]
    public void DifferentMachines_DoNotInterfereWithEachOther()
    {
        var registry = new ConnectionRegistry<Guid>();
        var machineA = Guid.NewGuid();
        var machineB = Guid.NewGuid();

        registry.Register(machineA, "conn-a");
        registry.Register(machineB, "conn-b");

        registry.Disconnect("conn-a");

        Assert.False(registry.TryGetConnectionId(machineA, out _));
        Assert.True(registry.TryGetConnectionId(machineB, out var connectionId));
        Assert.Equal("conn-b", connectionId);
    }
}
