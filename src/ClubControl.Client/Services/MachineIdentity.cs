using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

    public static string GetLocalIpAddress()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect("8.8.8.8", 65530);
        return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
    }

    public static string GetMacAddress()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                  n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

        return nic?.GetPhysicalAddress().ToString() ?? "";
    }
}
