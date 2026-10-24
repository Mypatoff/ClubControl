using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ClubControl.TestClient;

/// <summary>
/// Same job as ClubControl.Client's MachineIdentity, but works on any OS: the real client reads the
/// MachineGuid from the Windows registry, this tool caches a random one in a local file instead.
/// </summary>
public static class MachineIdentity
{
    private static readonly string GuidFilePath = Path.Combine(
        Path.GetTempPath(), "clubcontrol-testclient-guid.txt");

    public static Guid GetOrCreateMachineGuid()
    {
        if (File.Exists(GuidFilePath) && Guid.TryParse(File.ReadAllText(GuidFilePath), out var existing))
            return existing;

        var guid = Guid.NewGuid();
        File.WriteAllText(GuidFilePath, guid.ToString());
        return guid;
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
