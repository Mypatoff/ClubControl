using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Services;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using ClubControl.Shared.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Hubs;

public class ClubHub : Hub
{
    // Single live connection per PC, with reconnect/disconnect kept consistent even when a fast
    // reconnect races ahead of the old connection's disconnect event - see ConnectionRegistry.
    private static readonly ConnectionRegistry<Guid> Connections = new();

    // Which client build each PC last reported. Memory only, on purpose: it is only meaningful
    // while the PC is connected, and a database column would need an EF migration for no benefit.
    private static readonly ConcurrentDictionary<Guid, string> ClientVersions = new();

    private readonly ClubDbContext _db;
    private readonly SessionService _sessionService;
    private readonly ClientUpdateTracker _updates;
    private readonly AuthRejectionTracker _rejections;

    public ClubHub(ClubDbContext db, SessionService sessionService, ClientUpdateTracker updates, AuthRejectionTracker rejections)
    {
        _db = db;
        _sessionService = sessionService;
        _updates = updates;
        _rejections = rejections;
    }

    public async Task Heartbeat(HeartbeatRequest request)
    {
        var pc = await _db.Pcs.FirstOrDefaultAsync(p => p.MachineGuid == request.MachineGuid);

        // Once a PC has been provisioned (AuthTokenHash set - see
        // SessionService.ProvisionPcIfNeededAsync), its GUID alone is no longer enough to speak
        // for it: every heartbeat must also present the matching secret token. Reject anything
        // else outright - no connection registration, no Pc field updates, no "what should this
        // PC be doing" resync - so an impostor claiming a real PC's GUID can never intercept the
        // commands meant for it. A PC that has never been configured yet (AuthTokenHash still
        // null) has no secret to check yet and is trusted at face value - the unavoidable
        // trust-on-first-use bootstrap (see PcTokenHasher.IsHeartbeatAuthorized).
        if (pc is not null && !PcTokenHasher.IsHeartbeatAuthorized(pc.AuthTokenHash, request.AuthToken))
        {
            // The same PC is rejected again every 5 seconds: the log gets a line now and then, the PC card says so meanwhile.
            if (_rejections.Record(request.MachineGuid, DateTime.UtcNow))
            {
                _db.EventLogEntries.Add(new EventLogEntry
                {
                    Type = EventType.AuthRejected,
                    PcId = pc.Id,
                    Message = $"Rejected heartbeat for PC #{pc.Number?.ToString() ?? "?"}: missing or wrong auth token (claimed from {request.IpAddress})",
                });
                await _db.SaveChangesAsync();
            }

            return;
        }

        // Registering a newer connection for this machine (if one already existed) immediately
        // retires the old one, so it can never again be addressed by a command and its eventual
        // disconnect event cannot evict this new, correct mapping.
        Connections.Register(request.MachineGuid, Context.ConnectionId);
        ClientVersions[request.MachineGuid] = request.ClientVersion;

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

        // After "Update client": the first heartbeat that shows a new version (or a restart) is the answer.
        if (!isNew && _updates.OnHeartbeat(request.MachineGuid, request.ClientVersion, Context.ConnectionId, DateTime.UtcNow) is { } update)
        {
            _db.EventLogEntries.Add(new EventLogEntry
            {
                Type = ClientUpdateTracker.IsProblem(update) ? EventType.Warning : EventType.PowerCommand,
                PcId = pc.Id,
                Message = ClientUpdateTracker.LogMessage($"PC #{pc.Number?.ToString() ?? "?"}", update),
            });
        }

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
            // Settings first: they carry the unlock-password hash the client caches for offline use,
            // and a freshly booted client otherwise only has built-in defaults until the admin saves Settings.
            await Clients.Caller.SendAsync(HubMethodNames.ReceiveCommand, await _sessionService.BuildApplySettingsCommandAsync());

            var command = await _sessionService.BuildCurrentCommandAsync(pc);
            if (command is not null)
                await Clients.Caller.SendAsync(HubMethodNames.ReceiveCommand, command);
        }

        // Deliberately does NOT auto-mint an auth token here on "the next heartbeat after the PC
        // becomes configured": that would let anyone who already knows or guessed this GUID (e.g.
        // from an earlier heartbeat sniffed on the LAN - see README "Security notes") win a race
        // against the real PC for its own permanent token, any time after the admin assigns it a
        // number/tariff, with nothing more than a single well-timed heartbeat. Provisioning only
        // ever happens in SessionService.ProvisionPcIfNeededAsync, triggered by the admin's own
        // Settings Save action and bound to whichever connection is actually registered for this
        // GUID at that exact moment - see that method for the full reasoning.
    }

    /// <summary>
    /// Log-only: a client reports that staff unlocked it with the hotkey. Same token check as
    /// <see cref="Heartbeat"/>, so only the real PC can write this entry; it changes no state and
    /// cannot start, extend or unlock anything (see the note below).
    /// </summary>
    public async Task ReportManualUnlock(Guid machineGuid, string? authToken)
    {
        var pc = await _db.Pcs.FirstOrDefaultAsync(p => p.MachineGuid == machineGuid);
        if (pc is null || !PcTokenHasher.IsHeartbeatAuthorized(pc.AuthTokenHash, authToken))
            return;

        var markedAsRepair = await _sessionService.MarkManualUnlockAsync(machineGuid);

        _db.EventLogEntries.Add(new EventLogEntry
        {
            Type = EventType.ManualUnlock,
            PcId = pc.Id,
            Message = $"PC #{pc.Number?.ToString() ?? "?"} unlocked manually with the unlock password" + (markedAsRepair ? " - now in Repair" : ""),
        });
        await _db.SaveChangesAsync();
    }

    // Session/billing logic (SPEC section 5) and ClientCommand delivery are never exposed as hub
    // RPCs: the hub is reachable by every PC terminal on the LAN, and a hub method has no notion of
    // "admin vs. terminal" caller. The admin panel calls SessionService directly in-process (see
    // MainWindow/StartSessionWindow/MoveSessionWindow/PostpayPaymentWindow) -
    // never through this hub - precisely so a terminal client can never invoke "start a free
    // session", "extend my own session for free", or "unlock this PC" by calling a hub method
    // itself. Earlier phases exposed these as hub RPCs for ClubControl.TestClient to drive by hand;
    // that made every one of them callable, with no authorization at all, by any terminal on the
    // club's network. Do not re-add them here - if a command needs to reach a client, route it
    // through SessionService's own SendCommandAsync (private, server-side only).

    /// <summary>The client build this PC last reported, or "" if it never has (older client, or not connected since the server started).</summary>
    public static string GetClientVersion(Guid machineGuid) =>
        ClientVersions.TryGetValue(machineGuid, out var version) ? version : "";

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
