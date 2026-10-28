using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ClubControl.Server.Services;

/// <summary>
/// Asks the network "is that PC switched on?", without needing anything of the ClubControl client.
/// The ARP lookup is the main check: it works on the club's LAN even when the PC's Windows firewall
/// drops ping (the default). Ping is only a second chance.
/// </summary>
public static class PcPowerProbe
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddress, ref uint macLength);

    /// <param name="macAddress">The MAC the PC reported ("AABBCCDDEEFF"); if the IP now answers with a different one, another device took the IP over.</param>
    public static async Task<bool> IsPoweredOnAsync(string ipAddress, string macAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var ip) || IPAddress.IsLoopback(ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;

        try
        {
            var answeredMac = await Task.Run(() => AskArp(ip));
            if (answeredMac is not null)
                return macAddress == "" || string.Equals(answeredMac, macAddress, StringComparison.OrdinalIgnoreCase);

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 1000);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception)
        {
            // A probe that cannot run means "don't know", never "the PC is on".
            return false;
        }
    }

    /// <summary>The MAC that answered for this IP, or null if nobody did.</summary>
    private static string? AskArp(IPAddress ip)
    {
        var mac = new byte[8];
        var length = (uint)mac.Length;
        if (SendARP(BitConverter.ToUInt32(ip.GetAddressBytes(), 0), 0, mac, ref length) != 0)
            return null;

        return Convert.ToHexString(mac, 0, (int)length);
    }
}
