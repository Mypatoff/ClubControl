using System.Net;
using System.Net.Sockets;
using ClubControl.Shared;

namespace ClubControl.Tests;

public class UdpSocketsTests
{
    [Fact]
    public async Task A_reply_to_a_closed_port_does_not_break_the_next_receive()
    {
        // The discovery server's situation: it answers a client that has already closed its socket.
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        UdpSockets.IgnoreUnreachableReplies(server);
        var serverEndPoint = (IPEndPoint)server.Client.LocalEndPoint!;

        var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await client.SendAsync(new byte[] { 1 }, 1, serverEndPoint);
        var request = await server.ReceiveAsync();
        client.Close();

        await server.SendAsync(new byte[] { 2 }, 1, request.RemoteEndPoint);
        await Task.Delay(300);

        // Nothing more is coming: the receive has to simply wait (and be cancelled), not throw a SocketException.
        using var cts = new CancellationTokenSource(500);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await server.ReceiveAsync(cts.Token));
    }
}
