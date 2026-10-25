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

            var text = Encoding.UTF8.GetString(result.Buffer);
            if (text != NetworkConstants.DiscoveryRequestText)
                continue;

            try
            {
                var response = await BuildResponseAsync();
                var bytes = Encoding.UTF8.GetBytes(response.ToString());
                await udpClient.SendAsync(bytes, bytes.Length, result.RemoteEndPoint);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // A failed reply should never take discovery down for every other client; keep listening.
            }
        }
    }

    private async Task<DiscoveryResponse> BuildResponseAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var setting = await SettingsHelper.GetOrCreateAsync(db);

        return new DiscoveryResponse
        {
            ServerId = setting.ServerId,
            ServerIp = GetLocalIpAddress(),
            Port = NetworkConstants.SignalRPort
        };
    }

    // Used only when no real LAN interface can be found (should not happen on a machine with any
    // network adapter at all) - matches the previous behavior's own final fallback value.
    private const string FallbackIpAddress = "127.0.0.1";

    /// <summary>
    /// Picks the address to advertise in discovery responses. Deliberately does not rely on
    /// successfully routing to an external address (the previous implementation opened a UDP
    /// socket "connected" to 8.8.8.8 purely to let the OS pick a local-route address) - a club LAN
    /// with no internet uplink, or one that's temporarily down, would make every discovery
    /// response fail even though the LAN itself works fine. Enumerates actual local interfaces
    /// instead, same approach as ClubControl.Client.Services.MachineIdentity.GetMacAddress.
    /// </summary>
    private static string GetLocalIpAddress()
    {
        try
        {
            var address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(addr => addr.Address)
                .FirstOrDefault(addr => addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr));

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
