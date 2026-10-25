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
    private string? _authToken = TokenStore.Load();

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
        _connection.Reconnected += async _ =>
        {
            StateChanged?.Invoke(_connection.State);

            // Treat every reconnect as a resync, not a wait-for-the-next-tick: the heartbeat loop
            // may be mid-delay for up to HeartbeatInterval, during which the server still thinks
            // this PC is offline and the client is still showing stale/offline-unlocked state.
            // Sending one immediately re-registers the connection mapping and gets the server's
            // "what should this PC be doing right now" command back without that lag.
            await SendHeartbeatAsync(CancellationToken.None);
        };
        _connection.Closed += _ => { StateChanged?.Invoke(_connection.State); return Task.CompletedTask; };

        _connection.On<ClientCommand>(HubMethodNames.ReceiveCommand, command =>
        {
            // ProvisionToken is connection-level plumbing (see ClubHub.Heartbeat), not a visible
            // app state change - handle and persist it here rather than routing it through
            // AppController's state machine.
            if (command.Type == CommandType.ProvisionToken && command.AuthToken is not null)
            {
                _authToken = command.AuthToken;
                TokenStore.Save(command.AuthToken);
                return;
            }

            CommandReceived?.Invoke(command);
        });
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
            await SendHeartbeatAsync(cancellationToken);
            await Task.Delay(HeartbeatInterval, cancellationToken);
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (_connection.State != HubConnectionState.Connected)
            return;

        try
        {
            await _connection.InvokeAsync(HubMethodNames.Heartbeat, new HeartbeatRequest
            {
                MachineGuid = _machineGuid,
                IpAddress = MachineIdentity.GetLocalIpAddress(),
                MacAddress = MachineIdentity.GetMacAddress(),
                Hostname = MachineIdentity.GetHostname(),
                AuthToken = _authToken,
            }, cancellationToken);
        }
        catch (Exception)
        {
            // auto-reconnect will restore the connection; the next tick (or the next Reconnected
            // event) tries again.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _heartbeatCts?.Cancel();
        await _connection.DisposeAsync();
    }
}
