using System;
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

    private readonly ClubDbContext _db;
    private readonly SessionService _sessionService;

    public ClubHub(ClubDbContext db, SessionService sessionService)
    {
        _db = db;
        _sessionService = sessionService;
    }

    public async Task Heartbeat(HeartbeatRequest request)
    {
        var pc = await _db.Pcs.FirstOrDefaultAsync(p => p.MachineGuid == request.MachineGuid);

        // Once a PC has been provisioned (AuthTokenHash set - see below), its GUID alone is no
        // longer enough to speak for it: every heartbeat must also present the matching secret
        // token. Reject anything else outright - no connection registration, no Pc field
        // updates, no "what should this PC be doing" resync - so an impostor claiming a real PC's
        // GUID can never intercept the commands meant for it. A PC that has never been
        // configured yet (AuthTokenHash still null) has no secret to check yet; see the
        // provisioning step at the end of this method for why that bootstrap window is accepted.
        if (pc is not null && pc.AuthTokenHash is not null &&
            (request.AuthToken is null || !PcTokenHasher.Verify(request.AuthToken, pc.AuthTokenHash)))
        {
            _db.EventLogEntries.Add(new EventLogEntry
            {
                Type = EventType.AuthRejected,
                PcId = pc.Id,
                Message = $"Rejected heartbeat for PC #{pc.Number?.ToString() ?? "?"}: missing or wrong auth token (claimed from {request.IpAddress})",
            });
            await _db.SaveChangesAsync();
            return;
        }

        // Registering a newer connection for this machine (if one already existed) immediately
        // retires the old one, so it can never again be addressed by a command and its eventual
        // disconnect event cannot evict this new, correct mapping.
        Connections.Register(request.MachineGuid, Context.ConnectionId);

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

        // The first heartbeat after the admin assigns this PC a number/tariff: mint its
        // permanent auth token now, while this connection is still the one we just finished
        // trusting at face value above (the same trust-on-first-use moment the admin already
        // relies on to recognise a "New PC" by its reported hostname/IP). From here on, this
        // GUID can no longer be claimed by a different connection without this token.
        if (pc.IsConfigured && pc.AuthTokenHash is null)
        {
            var token = PcTokenHasher.GenerateToken();
            pc.AuthTokenHash = PcTokenHasher.Hash(token);
            await _db.SaveChangesAsync();

            await Clients.Caller.SendAsync(HubMethodNames.ReceiveCommand,
                new ClientCommand { Type = CommandType.ProvisionToken, AuthToken = token });
        }
    }

    // Session/billing logic (SPEC section 5) and ClientCommand delivery are never exposed as hub
    // RPCs: the hub is reachable by every PC terminal on the LAN, and a hub method has no notion of
    // "admin vs. terminal" caller. The admin panel calls SessionService directly in-process (see
    // MainWindow/StartSessionWindow/ExtendSessionWindow/MoveSessionWindow/PostpayPaymentWindow) -
    // never through this hub - precisely so a terminal client can never invoke "start a free
    // session", "extend my own session for free", or "unlock this PC" by calling a hub method
    // itself. Earlier phases exposed these as hub RPCs for ClubControl.TestClient to drive by hand;
    // that made every one of them callable, with no authorization at all, by any terminal on the
    // club's network. Do not re-add them here - if a command needs to reach a client, route it
    // through SessionService's own SendCommandAsync (private, server-side only).

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
