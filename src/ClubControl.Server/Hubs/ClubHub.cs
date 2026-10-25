using System;
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
    // Single live connection per PC, with reconnect/disconnect kept consistent even when a fast
    // reconnect races ahead of the old connection's disconnect event - see ConnectionRegistry.
    private static readonly ConnectionRegistry<Guid> Connections = new();

    private readonly ClubDbContext _db;
    private readonly SessionService _sessionService;

    public ClubHub(ClubDbContext db, SessionService sessionService)
    {
        _db = db;
        _sessionService = sessionService;
    }

    public async Task Heartbeat(HeartbeatRequest request)
    {
        // Registering a newer connection for this machine (if one already existed) immediately
        // retires the old one, so it can never again be addressed by a command and its eventual
        // disconnect event cannot evict this new, correct mapping.
        Connections.Register(request.MachineGuid, Context.ConnectionId);

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
        if (Connections.TryGetConnectionId(targetMachineGuid, out var connectionId))
            await Clients.Client(connectionId).SendAsync(HubMethodNames.ReceiveCommand, command);
    }

    /// <summary>Lets session logic (SessionService) find a connected client's SignalR connection by its machine GUID.</summary>
    public static bool TryGetConnectionId(Guid machineGuid, out string connectionId) =>
        Connections.TryGetConnectionId(machineGuid, out connectionId);

    /// <summary>
    /// Forcibly evicts a machine's connection mapping, e.g. when <see cref="OfflineMonitorService"/>
    /// decides a PC is offline because its heartbeats stopped even though the socket never cleanly
    /// disconnected (cable pulled, PC crashed/hung). Without this, commands could keep being sent to
    /// that dead connection id until SignalR's own transport-level timeout eventually fires.
    /// </summary>
    public static void ForgetMachine(Guid machineGuid) => Connections.Forget(machineGuid);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var (found, machineGuid, wasActiveConnection) = Connections.Disconnect(Context.ConnectionId);

        // wasActiveConnection is false when a fast reconnect already replaced this connection's
        // mapping with a newer one via Heartbeat - that means this disconnect is for a superseded
        // socket and must not be treated as "the machine went offline".
        if (found && wasActiveConnection)
        {
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
