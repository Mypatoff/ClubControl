using System.Net;
using System.Text.RegularExpressions;

namespace ClubControl.Shared;

/// <summary>Builds the Wake-on-LAN magic packet (SPEC section 6). Sending it over UDP is the server's job.</summary>
public static class WakeOnLan
{
    public const int Port = 9;

    /// <summary>6 bytes of 0xFF followed by the 6-byte MAC address repeated 16 times (102 bytes total).</summary>
    public static byte[] BuildMagicPacket(string macAddress)
    {
        var mac = ParseMacAddress(macAddress);

        var packet = new byte[6 + 16 * 6];
        for (var i = 0; i < 6; i++)
            packet[i] = 0xFF;

        for (var repeat = 0; repeat < 16; repeat++)
            mac.CopyTo(packet, 6 + repeat * 6);

        return packet;
    }

    /// <summary>
    /// The subnet's directed broadcast address for a given local IPv4 address/mask, e.g.
    /// 192.168.1.50/255.255.255.0 -> 192.168.1.255. Sending the magic packet here (rather than
    /// relying only on the generic 255.255.255.255 broadcast) reaches PCs whose router/switch does
    /// not forward the generic broadcast across VLANs/subnets, and works correctly on a multi-NIC
    /// server where each adapter sits on a different subnet.
    /// </summary>
    public static IPAddress GetDirectedBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();
        if (addressBytes.Length != maskBytes.Length)
            throw new ArgumentException("Address and subnet mask must be the same address family.");

        var broadcastBytes = new byte[addressBytes.Length];
        for (var i = 0; i < addressBytes.Length; i++)
            broadcastBytes[i] = (byte)(addressBytes[i] | (byte)~maskBytes[i]);

        return new IPAddress(broadcastBytes);
    }

    private static byte[] ParseMacAddress(string macAddress)
    {
        var hex = Regex.Replace(macAddress, "[^0-9A-Fa-f]", "");
        if (hex.Length != 12)
            throw new FormatException($"'{macAddress}' is not a valid MAC address.");

        var bytes = new byte[6];
        for (var i = 0; i < 6; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

        return bytes;
    }
}
