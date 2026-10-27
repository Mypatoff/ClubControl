using System;
using System.Collections.Generic;
using System.Linq;
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

        var requestBytes = Encoding.UTF8.GetBytes(NetworkConstants.DiscoveryRequestText);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Re-enumerated on every try, not just once before the loop - a NIC that is still
                // coming up (DHCP not finished, slow link negotiation) when the client starts at
                // logon would otherwise be missed forever, since the interface snapshot taken on
                // the very first try would never be retaken once that NIC actually comes up.
                using var sockets = new BroadcastSockets();
                await sockets.SendAsync(requestBytes);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(perTryTimeout);

                var response = await sockets.ReceiveOneAsync(timeoutCts.Token);
                if (response is not null)
                    return response;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The caller (MainWindow) starts this fire-and-forget and never awaits/observes
                // the result, so letting anything escape here - e.g. NetworkInterface.GetAll-
                // NetworkInterfaces() throwing during a transient adapter state change (a VPN
                // adapter coming up/down) - would silently kill discovery forever with no retry
                // and no visible error, leaving the PC stuck until something restarts the process.
                // This loop's whole job is "keep trying until the server is found or we're told to
                // stop" - a bad try must not end that, just this one attempt.
                ClientLog.Error("ServerDiscoveryClient.DiscoverAsync", ex);

                try
                {
                    await Task.Delay(perTryTimeout, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Caller cancelled while we were waiting to retry - fine, the outer loop's
                    // condition check handles exiting cleanly.
                }
            }
        }

        return null;
    }

    /// <summary>
    /// One UdpClient per active network interface, each bound to that interface's own address.
    /// A single unbound socket sending to 255.255.255.255 lets Windows pick whichever interface
    /// its default route favors, which can be the wrong one on a PC with more than one active
    /// adapter (a second NIC, or a virtual one added by VPN software like Radmin VPN, Hyper-V, or
    /// VMware) - the broadcast then never reaches the real LAN. Binding one socket per interface
    /// and sending that interface's own directed broadcast address forces it out every adapter, the
    /// same fix already applied to Wake-on-LAN in ClubControl.Server.Services.PowerService.
    /// </summary>
    private sealed class BroadcastSockets : IDisposable
    {
        private readonly List<(UdpClient Socket, IPAddress Broadcast)> _perInterface = new();
        private readonly UdpClient? _fallback;

        public BroadcastSockets()
        {
            foreach (var (localAddress, broadcastAddress) in LocalNetworkInterfaces.GetBroadcastEndpoints())
            {
                try
                {
                    var socket = new UdpClient(new IPEndPoint(localAddress, 0)) { EnableBroadcast = true };
                    _perInterface.Add((socket, broadcastAddress));
                }
                catch (SocketException)
                {
                    // This interface couldn't be bound (e.g. link down mid-enumeration) - others are still tried.
                }
            }

            // No usable interface found any usable address at all - fall back to the naive approach
            // rather than silently never sending anything on an unusual network.
            if (_perInterface.Count == 0)
                _fallback = new UdpClient { EnableBroadcast = true };
        }

        public async Task SendAsync(byte[] requestBytes)
        {
            foreach (var (socket, broadcastAddress) in _perInterface)
            {
                try
                {
                    await socket.SendAsync(requestBytes, requestBytes.Length, new IPEndPoint(broadcastAddress, NetworkConstants.DiscoveryPort));
                }
                catch (SocketException)
                {
                    // This interface couldn't send right now; others may still get through.
                }
            }

            if (_fallback is not null)
                await _fallback.SendAsync(requestBytes, requestBytes.Length, new IPEndPoint(IPAddress.Broadcast, NetworkConstants.DiscoveryPort));
        }

        public async Task<DiscoveryResponse?> ReceiveOneAsync(CancellationToken cancellationToken)
        {
            var sockets = _perInterface.Select(p => p.Socket).Concat(_fallback is null ? Enumerable.Empty<UdpClient>() : new[] { _fallback }).ToList();
            var pending = sockets.Select(socket => ReceiveFromAsync(socket, cancellationToken)).ToList();

            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);

                DiscoveryResponse? response;
                try
                {
                    response = await completed;
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
                catch (SocketException)
                {
                    continue;
                }

                if (response is not null)
                    return response;
            }

            return null;
        }

        private static async Task<DiscoveryResponse?> ReceiveFromAsync(UdpClient socket, CancellationToken cancellationToken)
        {
            var result = await socket.ReceiveAsync(cancellationToken);
            var text = Encoding.UTF8.GetString(result.Buffer);
            return DiscoveryResponse.TryParse(text);
        }

        public void Dispose()
        {
            foreach (var (socket, _) in _perInterface)
                socket.Dispose();

            _fallback?.Dispose();
        }
    }
}
