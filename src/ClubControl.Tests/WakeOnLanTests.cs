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
}
