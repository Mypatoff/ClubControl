using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Services;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Hubs;

public class ClubHub : Hub
{
    // Maps a live SignalR connection to the PC it belongs to, so OnDisconnectedAsync
    // knows which PC went offline without needing another round trip to the client.
    private static readonly ConcurrentDictionary<string, Guid> ConnectionToMachine = new();

    // Reverse of the map above, so a command can be addressed to a machine by its Guid.
    private static readonly ConcurrentDictionary<Guid, string> MachineToConnection = new();

    private readonly ClubDbContext _db;
    private readonly SessionService _sessionService;

    public ClubHub(ClubDbContext db, SessionService sessionService)
    {
        _db = db;
        _sessionService = sessionService;
    }

    public async Task Heartbeat(HeartbeatRequest request)
    {
        ConnectionToMachine[Context.ConnectionId] = request.MachineGuid;
        MachineToConnection[request.MachineGuid] = Context.ConnectionId;

        var pc = await _db.Pcs.FirstOrDefaultAsync(p => p.MachineGuid == request.MachineGuid);
        var isNew = pc is null;

        if (pc is null)
        {
            pc = new Pc { MachineGuid = request.MachineGuid };
            _db.Pcs.Add(pc);
        }

        pc.IpAddress = request.IpAddress;
        pc.MacAddress = request.MacAddress;
        pc.Hostname = request.Hostname;
        pc.LastHeartbeatUtc = DateTime.UtcNow;

        var wasOnline = pc.IsOnline;
        pc.IsOnline = true;

        if (isNew)
        {
            _db.EventLogEntries.Add(new EventLogEntry
            {
                Type = EventType.PcConnected,
                Message = $"New PC connected: {request.Hostname} ({request.IpAddress})",
            });
        }
        else if (!wasOnline)
        {
            _db.EventLogEntries.Add(new EventLogEntry
            {
                Type = EventType.PcConnected,
                PcId = pc.Id,
                Message = $"PC #{pc.Number?.ToString() ?? "?"} reconnected",
            });
        }

        await _db.SaveChangesAsync();

        // Just came online (first contact, or back from an offline gap / server restart): tell it
        // what it should actually be showing right now, instead of waiting for the next admin action
        // (SPEC 4 "Offline rule", SPEC 5 "Power cuts / restarts").
        if (isNew || !wasOnline)
        {
            var command = await _sessionService.BuildCurrentCommandAsync(pc);
            if (command is not null)
                await Clients.Caller.SendAsync(HubMethodNames.ReceiveCommand, command);
        }
    }

    // ---- session/billing RPCs (SPEC section 5). These will become the admin panel's API in Phase 4;
    // for now ClubControl.TestClient calls them directly so this logic can be tested manually. ----

    public Task StartPrepayByMoney(Guid machineGuid, int moneyUzs) =>
        _sessionService.StartPrepayByMoneyAsync(machineGuid, moneyUzs);

    public Task StartPrepayByTime(Guid machineGuid, int minutes) =>
        _sessionService.StartPrepayByTimeAsync(machineGuid, minutes);

    public Task StartPostpay(Guid machineGuid) =>
        _sessionService.StartPostpayAsync(machineGuid);

    public Task StartFree(Guid machineGuid, string? note) =>
        _sessionService.StartFreeAsync(machineGuid, note);

    public Task ExtendPrepayByMoney(Guid machineGuid, int addMoneyUzs) =>
        _sessionService.ExtendPrepayByMoneyAsync(machineGuid, addMoneyUzs);

    public Task ExtendPrepayByTime(Guid machineGuid, int addMinutes) =>
        _sessionService.ExtendPrepayByTimeAsync(machineGuid, addMinutes);

    public Task MoveSession(Guid fromMachineGuid, Guid toMachineGuid) =>
        _sessionService.MoveSessionAsync(fromMachineGuid, toMachineGuid);

    public Task EndSessionEarly(Guid machineGuid) =>
        _sessionService.EndSessionEarlyAsync(machineGuid);

    /// <summary>
    /// Dev/test only: lets ClubControl.TestClient push a command to a specific client so Phase 2
    /// (lock screen, timer, commands) can be tested manually before the admin panel (Phase 4) exists.
    /// </summary>
    public async Task SendTestCommand(Guid targetMachineGuid, ClientCommand command)
    {
        if (MachineToConnection.TryGetValue(targetMachineGuid, out var connectionId))
            await Clients.Client(connectionId).SendAsync(HubMethodNames.ReceiveCommand, command);
    }

    /// <summary>Lets session logic (SessionService) find a connected client's SignalR connection by its machine GUID.</summary>
    public static bool TryGetConnectionId(Guid machineGuid, out string connectionId) =>
        MachineToConnection.TryGetValue(machineGuid, out connectionId);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionToMachine.TryRemove(Context.ConnectionId, out var machineGuid))
        {
            MachineToConnection.TryRemove(machineGuid, out _);
            var pc = await _db.Pcs.FirstOrDefaultAsync(p => p.MachineGuid == machineGuid);
            if (pc is not null && pc.IsOnline)
            {
                pc.IsOnline = false;
                _db.EventLogEntries.Add(new EventLogEntry
                {
                    Type = EventType.PcDisconnected,
                    PcId = pc.Id,
                    Message = $"PC #{pc.Number?.ToString() ?? "?"} disconnected",
                });
                await _db.SaveChangesAsync();
            }
        }

        await base.OnDisconnectedAsync(exception);
    }
}
