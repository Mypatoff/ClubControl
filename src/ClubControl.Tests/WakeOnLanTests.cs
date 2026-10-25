using System.Net;
using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class WakeOnLanTests
{
    [Fact]
    public void Packet_Is102Bytes()
    {
        var packet = WakeOnLan.BuildMagicPacket("AA:BB:CC:DD:EE:FF");
        Assert.Equal(102, packet.Length);
    }

    [Fact]
    public void Packet_StartsWithSixFFBytes()
    {
        var packet = WakeOnLan.BuildMagicPacket("AA:BB:CC:DD:EE:FF");
        for (var i = 0; i < 6; i++)
            Assert.Equal(0xFF, packet[i]);
    }

    [Fact]
    public void Packet_RepeatsTheMacSixteenTimes()
    {
        var packet = WakeOnLan.BuildMagicPacket("AA-BB-CC-DD-EE-FF");
        byte[] expectedMac = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];

        for (var repeat = 0; repeat < 16; repeat++)
        {
            var offset = 6 + repeat * 6;
            Assert.Equal(expectedMac, packet[offset..(offset + 6)]);
        }
    }

    [Theory]
    [InlineData("AABBCCDDEEFF")]
    [InlineData("aa:bb:cc:dd:ee:ff")]
    [InlineData("AA-BB-CC-DD-EE-FF")]
    public void AcceptsColonDashAndBareFormats(string macAddress)
    {
        var packet = WakeOnLan.BuildMagicPacket(macAddress);
        Assert.Equal(102, packet.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-mac")]
    [InlineData("AA:BB:CC:DD:EE")]
    public void RejectsInvalidMacAddresses(string macAddress)
    {
        Assert.Throws<FormatException>(() => WakeOnLan.BuildMagicPacket(macAddress));
    }

    [Theory]
    [InlineData("192.168.1.50", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.0.5.17", "255.255.0.0", "10.0.255.255")]
    [InlineData("172.16.3.200", "255.255.255.128", "172.16.3.255")]
    [InlineData("192.168.1.1", "255.255.255.255", "192.168.1.1")]
    public void GetDirectedBroadcastAddress_ComputesSubnetBroadcast(string address, string mask, string expectedBroadcast)
    {
        var broadcast = WakeOnLan.GetDirectedBroadcastAddress(IPAddress.Parse(address), IPAddress.Parse(mask));
        Assert.Equal(IPAddress.Parse(expectedBroadcast), broadcast);
    }

    [Fact]
    public void GetDirectedBroadcastAddress_DifferentSubnetsOnMultipleNics_YieldDifferentBroadcasts()
    {
        // Simulates a multi-NIC/VLAN server: each adapter's own subnet must get its own directed
        // broadcast, not one shared 255.255.255.255 that might not even be forwarded on either link.
        var nic1 = WakeOnLan.GetDirectedBroadcastAddress(IPAddress.Parse("192.168.1.10"), IPAddress.Parse("255.255.255.0"));
        var nic2 = WakeOnLan.GetDirectedBroadcastAddress(IPAddress.Parse("10.10.0.10"), IPAddress.Parse("255.255.0.0"));

        Assert.Equal(IPAddress.Parse("192.168.1.255"), nic1);
        Assert.Equal(IPAddress.Parse("10.10.255.255"), nic2);
        Assert.NotEqual(nic1, nic2);
    }
}
