using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Win32;

namespace ClubControl.Client.Services;

public static class MachineIdentity
{
    public static Guid GetMachineGuid()
    {
        var text = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string;

        return Guid.TryParse(text, out var guid)
            ? guid
            : throw new InvalidOperationException("Could not read MachineGuid from the registry.");
    }

    public static string GetHostname() => Dns.GetHostName();

    /// <summary>
    /// "1.0.0+f4545fa": the assembly version plus the git commit it was built from (the SDK adds the
    /// commit automatically), cut to 7 characters. The commit is what tells two builds apart - nobody
    /// bumps the version number by hand for every fix.
    /// </summary>
    public static string GetClientVersion()
    {
        var info = typeof(MachineIdentity).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

        var plus = info.IndexOf('+');
        return plus >= 0 && info.Length > plus + 8 ? info[..(plus + 8)] : info;
    }

    public static string GetLocalIpAddress()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect("8.8.8.8", 65530);
        return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
    }

    public static string GetMacAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToList();

        // Prefer the real Ethernet/Wi-Fi adapter that actually has a default gateway (the real LAN
        // uplink) over a virtual adapter added by VPN software (Radmin VPN, Hyper-V, VMware, etc.) -
        // those often report as NetworkInterfaceType.Ethernet too, but don't carry a gateway of
        // their own, so this is what actually tells them apart. Falls back to the first "up"
        // adapter if nothing matches, same as before, rather than ever reporting no MAC at all.
        var nic = candidates.FirstOrDefault(IsRealLanAdapter) ?? candidates.FirstOrDefault();

        return nic?.GetPhysicalAddress().ToString() ?? "";
    }

    private static bool IsRealLanAdapter(NetworkInterface nic) =>
        (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
         nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
        nic.GetIPProperties().GatewayAddresses.Count > 0;
}
