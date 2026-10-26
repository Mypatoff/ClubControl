using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ClubControl.Shared;

/// <summary>
/// Enumerates this machine's own network interfaces for broadcast purposes. Used by anything that
/// needs to reach the LAN on every adapter rather than whichever one the OS's default route
/// happens to pick - a PC with more than one active IPv4 adapter (a second NIC, or a virtual one
/// added by VPN software like Radmin VPN, Hyper-V, or VMware) can otherwise send a plain
/// 255.255.255.255 broadcast out the wrong adapter and never reach the real LAN at all.
/// </summary>
public static class LocalNetworkInterfaces
{
    /// <summary>Every active, non-loopback IPv4 interface's own address paired with its subnet's directed broadcast address.</summary>
    public static List<(IPAddress Local, IPAddress Broadcast)> GetBroadcastEndpoints()
    {
        var endpoints = new List<(IPAddress Local, IPAddress Broadcast)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                    continue;

                try
                {
                    var broadcast = WakeOnLan.GetDirectedBroadcastAddress(unicast.Address, unicast.IPv4Mask);
                    endpoints.Add((unicast.Address, broadcast));
                }
                catch (ArgumentException)
                {
                    // Malformed/unusual address-mask pairing for this adapter - skip it, not fatal.
                }
            }
        }

        return endpoints;
    }
}
