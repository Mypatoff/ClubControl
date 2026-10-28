using System.Net.Sockets;

namespace ClubControl.Shared;

public static class UdpSockets
{
    // The IOControl code SIO_UDP_CONNRESET.
    private const int SioUdpConnReset = -1744830452;

    /// <summary>
    /// On Windows a UDP socket that sent a reply to a port which has since been closed is told so (an ICMP
    /// "port unreachable") and its NEXT receive throws "connection forcibly closed". The discovery server
    /// meets this all the time: a client that gave up waiting closes its socket before the answer arrives.
    /// This switches that report off, so a receive only ever returns real datagrams.
    /// </summary>
    public static void IgnoreUnreachableReplies(UdpClient client)
    {
        if (OperatingSystem.IsWindows())
            client.Client.IOControl(SioUdpConnReset, new byte[] { 0 }, null);
    }
}
