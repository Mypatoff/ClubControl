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

    /// <summary>Every active IPv4 address on this machine with its subnet mask (loopback and tunnel adapters left out).</summary>
    public static List<(IPAddress Address, IPAddress Mask)> GetIPv4Addresses()
    {
        var addresses = new List<(IPAddress Address, IPAddress Mask)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask is not null)
                    addresses.Add((unicast.Address, unicast.IPv4Mask));
            }
        }

        return addresses;
    }

    /// <summary>
    /// Which of this machine's addresses to tell <paramref name="remote"/> to connect to. The one on
    /// the same subnet as the asker is the one it can actually reach - "the first adapter" can be a
    /// VPN or virtual adapter (Radmin VPN, Hyper-V, VMware) that the club's PCs cannot route to. When
    /// no subnet matches, prefers a real address over a link-local 169.254.x.x one (what Windows
    /// self-assigns to an adapter with no DHCP lease), then takes the first. Null if there are none.
    /// </summary>
    public static IPAddress? ChooseAddressFor(IPAddress remote, IReadOnlyList<(IPAddress Address, IPAddress Mask)> candidates)
    {
        if (remote.IsIPv4MappedToIPv6)
            remote = remote.MapToIPv4();

        if (remote.AddressFamily == AddressFamily.InterNetwork)
        {
            foreach (var (address, mask) in candidates)
            {
                if (IsSameSubnet(remote, address, mask))
                    return address;
            }
        }

        foreach (var (address, _) in candidates)
        {
            if (!IsLinkLocal(address))
                return address;
        }

        return candidates.Count > 0 ? candidates[0].Address : null;
    }

    private static bool IsSameSubnet(IPAddress a, IPAddress b, IPAddress mask)
    {
        var aBytes = a.GetAddressBytes();
        var bBytes = b.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (aBytes.Length != bBytes.Length || aBytes.Length != maskBytes.Length)
            return false;

        for (var i = 0; i < aBytes.Length; i++)
        {
            if ((aBytes[i] & maskBytes[i]) != (bBytes[i] & maskBytes[i]))
                return false;
        }

        return true;
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }
}
