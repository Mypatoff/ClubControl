using System.Net;
using System.Net.Sockets;
using System.Text;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using ClubControl.TestClient;
using Microsoft.AspNetCore.SignalR.Client;

Console.WriteLine("ClubControl test client - a stand-in for the real client, used to check the server.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cts.Cancel();
};

var machineGuid = MachineIdentity.GetOrCreateMachineGuid();
Console.WriteLine($"Machine GUID: {machineGuid}");

Console.WriteLine($"Broadcasting discovery on UDP {NetworkConstants.DiscoveryPort}...");
var server = await DiscoverServerAsync(cts.Token);
if (server is null)
{
    Console.WriteLine("Cancelled before a server was found.");
    return;
}

Console.WriteLine($"Found server {server.ServerId} at {server.ServerIp}:{server.Port}");

var connection = new HubConnectionBuilder()
    .WithUrl($"http://{server.ServerIp}:{server.Port}{NetworkConstants.HubRoute}")
    .WithAutomaticReconnect()
    .Build();

connection.Reconnecting += _ => { Console.WriteLine("Reconnecting..."); return Task.CompletedTask; };
connection.Reconnected += _ => { Console.WriteLine("Reconnected."); return Task.CompletedTask; };
connection.Closed += error => { Console.WriteLine($"Connection closed: {error?.Message}"); return Task.CompletedTask; };

await connection.StartAsync(cts.Token);
Console.WriteLine("Connected. Sending a heartbeat every 5 seconds. Press Ctrl+C to stop.");

Console.WriteLine();
Console.WriteLine("This client only sends heartbeats now - it stands in for a real PC terminal to");
Console.WriteLine("exercise connection/offline-detection logic. The old command REPL was removed: it");
Console.WriteLine("drove hub RPCs (StartPrepayByMoney, SendTestCommand, etc.) that let ANY connected");
Console.WriteLine("client start/extend/end sessions or push an Unlock to any PC with no authorization");
Console.WriteLine("- a real security hole on the production network. The admin panel now calls");
Console.WriteLine("SessionService in-process instead, so those hub RPCs were removed, not reused.");

while (!cts.Token.IsCancellationRequested)
{
    if (connection.State == HubConnectionState.Connected)
    {
        try
        {
            await connection.InvokeAsync(HubMethodNames.Heartbeat, new HeartbeatRequest
            {
                MachineGuid = machineGuid,
                IpAddress = MachineIdentity.GetLocalIpAddress(),
                MacAddress = MachineIdentity.GetMacAddress(),
                Hostname = MachineIdentity.GetHostname(),
            }, cts.Token);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] heartbeat sent");
        }
        catch (Exception ex) when (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine($"Heartbeat failed: {ex.Message}");
        }
    }

    try
    {
        await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

await connection.DisposeAsync();
Console.WriteLine("Stopped.");

static async Task<DiscoveryResponse?> DiscoverServerAsync(CancellationToken cancellationToken)
{
    using var udpClient = new UdpClient { EnableBroadcast = true };
    var requestBytes = Encoding.UTF8.GetBytes(NetworkConstants.DiscoveryRequestText);
    var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, NetworkConstants.DiscoveryPort);

    while (!cancellationToken.IsCancellationRequested)
    {
        await udpClient.SendAsync(requestBytes, requestBytes.Length, broadcastEndpoint);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

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
            Console.WriteLine("No reply yet, retrying...");
        }
    }

    return null;
}
