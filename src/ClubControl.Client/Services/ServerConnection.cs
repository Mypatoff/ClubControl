using System;
using System.Threading;
using System.Threading.Tasks;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using Microsoft.AspNetCore.SignalR.Client;

namespace ClubControl.Client.Services;

/// <summary>Owns the SignalR connection to a discovered server and the 5-second heartbeat loop.</summary>
public class ServerConnection : IAsyncDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

    private readonly HubConnection _connection;
    private readonly Guid _machineGuid;
    private CancellationTokenSource? _heartbeatCts;

    public event Action<HubConnectionState>? StateChanged;
    public event Action<ClientCommand>? CommandReceived;

    public HubConnectionState State => _connection.State;

    public ServerConnection(string serverIp, int port, Guid machineGuid)
    {
        _machineGuid = machineGuid;
        _connection = new HubConnectionBuilder()
            .WithUrl($"http://{serverIp}:{port}{NetworkConstants.HubRoute}")
            .WithAutomaticReconnect()
            .Build();

        _connection.Reconnecting += _ => { StateChanged?.Invoke(_connection.State); return Task.CompletedTask; };
        _connection.Reconnected += _ => { StateChanged?.Invoke(_connection.State); return Task.CompletedTask; };
        _connection.Closed += _ => { StateChanged?.Invoke(_connection.State); return Task.CompletedTask; };

        _connection.On<ClientCommand>(HubMethodNames.ReceiveCommand, command => CommandReceived?.Invoke(command));
    }

    public async Task ConnectAsync()
    {
        await _connection.StartAsync();
        StateChanged?.Invoke(_connection.State);

        _heartbeatCts = new CancellationTokenSource();
        _ = RunHeartbeatLoopAsync(_heartbeatCts.Token);
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_connection.State == HubConnectionState.Connected)
            {
                try
                {
                    await _connection.InvokeAsync(HubMethodNames.Heartbeat, new HeartbeatRequest
                    {
                        MachineGuid = _machineGuid,
                        IpAddress = MachineIdentity.GetLocalIpAddress(),
                        MacAddress = MachineIdentity.GetMacAddress(),
                        Hostname = MachineIdentity.GetHostname(),
                    }, cancellationToken);
                }
                catch (Exception)
                {
                    // auto-reconnect will restore the connection; the next tick tries again
                }
            }

            await Task.Delay(HeartbeatInterval, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _heartbeatCts?.Cancel();
        await _connection.DisposeAsync();
    }
}
