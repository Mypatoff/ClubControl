using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClubControl.Server.Services;

/// <summary>Answers UDP discovery broadcasts so clients can find this server on the LAN.</summary>
public class UdpDiscoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public UdpDiscoveryService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udpClient = new UdpClient(NetworkConstants.DiscoveryPort);
        UdpSockets.IgnoreUnreachableReplies(udpClient);
        stoppingToken.Register(() => udpClient.Close());

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udpClient.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (!stoppingToken.IsCancellationRequested)
            {
                // Anything the socket reports must never end discovery: without it a PC that restarts
                // could not find the server again, and nothing on the server would show why.
                await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
                continue;
            }

            var text = Encoding.UTF8.GetString(result.Buffer);
            if (text != NetworkConstants.DiscoveryRequestText)
                continue;

            try
            {
                var response = await BuildResponseAsync(result.RemoteEndPoint.Address);
                var bytes = Encoding.UTF8.GetBytes(response.ToString());
                await udpClient.SendAsync(bytes, bytes.Length, result.RemoteEndPoint);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // A failed reply should never take discovery down for every other client; keep listening.
            }
        }
    }

    private async Task<DiscoveryResponse> BuildResponseAsync(IPAddress asker)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var setting = await SettingsHelper.GetOrCreateAsync(db);

        return new DiscoveryResponse
        {
            ServerId = setting.ServerId,
            ServerIp = GetLocalIpAddress(asker),
            Port = NetworkConstants.SignalRPort
        };
    }

    // Used only when no real LAN interface can be found (should not happen on a machine with any
    // network adapter at all) - matches the previous behavior's own final fallback value.
    private const string FallbackIpAddress = "127.0.0.1";

    /// <summary>
    /// Picks the address to advertise in a discovery response: the one on the asking PC's own
    /// subnet (see <see cref="LocalNetworkInterfaces.ChooseAddressFor"/>), so a VPN/virtual adapter
    /// on this machine can never be handed out instead of the real LAN address. Deliberately does
    /// not rely on routing to an external address (the original implementation "connected" a UDP
    /// socket to 8.8.8.8 to let the OS choose) - a club LAN with no internet uplink, or one that's
    /// temporarily down, would make every discovery response fail even though the LAN itself works.
    /// </summary>
    private static string GetLocalIpAddress(IPAddress asker)
    {
        try
        {
            var address = LocalNetworkInterfaces.ChooseAddressFor(asker, LocalNetworkInterfaces.GetIPv4Addresses());
            return address?.ToString() ?? FallbackIpAddress;
        }
        catch (NetworkInformationException)
        {
            // Enumerating interfaces can itself fail on some systems/permissions - never let that
            // take discovery down (BuildResponseAsync's caller already guards the rest).
            return FallbackIpAddress;
        }
    }
}
