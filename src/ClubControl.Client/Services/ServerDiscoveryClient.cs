using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClubControl.Shared;
using ClubControl.Shared.Dto;

namespace ClubControl.Client.Services;

/// <summary>Broadcasts on the discovery port until a server answers (or the caller gives up).</summary>
public static class ServerDiscoveryClient
{
    public static async Task<DiscoveryResponse?> DiscoverAsync(
        CancellationToken cancellationToken, TimeSpan perTryTimeout = default)
    {
        if (perTryTimeout == default)
            perTryTimeout = TimeSpan.FromSeconds(2);

        using var udpClient = new UdpClient { EnableBroadcast = true };
        var requestBytes = Encoding.UTF8.GetBytes(NetworkConstants.DiscoveryRequestText);
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, NetworkConstants.DiscoveryPort);

        while (!cancellationToken.IsCancellationRequested)
        {
            await udpClient.SendAsync(requestBytes, requestBytes.Length, broadcastEndpoint);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(perTryTimeout);

            try
            {
                var result = await udpClient.ReceiveAsync(timeoutCts.Token);
                var text = Encoding.UTF8.GetString(result.Buffer);
                var response = DiscoveryResponse.TryParse(text);
                if (response is not null)
                    return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // this attempt timed out; loop and try again
            }
        }

        return null;
    }
}
