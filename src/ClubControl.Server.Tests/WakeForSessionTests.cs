using ClubControl.Server.Data.Models;
using ClubControl.Server.Services;
using ClubControl.Shared;

namespace ClubControl.Server.Tests;

public class WakeForSessionTests
{
    private static Pc OfflinePc() => new() { Number = 1, MacAddress = "AABBCCDDEEFF", IsOnline = false };

    [Fact]
    public void A_switched_off_PC_with_a_known_MAC_is_woken()
    {
        Assert.True(PowerService.NeedsWakeUp(OfflinePc()));
    }

    [Fact]
    public void A_PC_that_is_already_connected_is_not_woken()
    {
        var pc = OfflinePc();
        pc.IsOnline = true;

        Assert.False(PowerService.NeedsWakeUp(pc));
    }

    [Fact]
    public void A_PC_that_never_reported_a_MAC_cannot_be_woken()
    {
        var pc = OfflinePc();
        pc.MacAddress = "";

        Assert.False(PowerService.NeedsWakeUp(pc));
    }

    [Fact]
    public void Manual_resources_are_never_woken()
    {
        var table = OfflinePc();
        table.IsManual = true;
        table.Kind = ResourceKind.Other;

        Assert.False(PowerService.NeedsWakeUp(table));
    }
}
