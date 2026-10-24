using System;
using System.Net;
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

    private static string GetLocalIpAddress()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect("8.8.8.8", 65530);
        return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
    }
}
