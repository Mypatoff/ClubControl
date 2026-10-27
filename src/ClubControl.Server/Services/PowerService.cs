using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Hubs;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using ClubControl.Shared.Security;
using Microsoft.AspNetCore.SignalR;

namespace ClubControl.Server.Services;

/// <summary>
/// Power control (SPEC section 6): Wake-on-LAN magic packets for "Turn on", and Shutdown/Restart
/// commands for everything else. Each action can target one PC, several, or all of them.
/// </summary>
public class PowerService
{
    private readonly ClubDbContext _db;
    private readonly IHubContext<ClubHub> _hub;
    private readonly ClientUpdateTracker _updates;

    public PowerService(ClubDbContext db, IHubContext<ClubHub> hub, ClientUpdateTracker updates)
    {
        _db = db;
        _hub = hub;
        _updates = updates;
    }

    /// <summary>Sends a WoL magic packet to every PC with a known MAC address. Works whether or not the PC is online.</summary>
    public async Task<int> TurnOnAsync(IEnumerable<Pc> pcs)
    {
        var sent = 0;
        foreach (var pc in pcs.Where(p => !string.IsNullOrWhiteSpace(p.MacAddress)))
        {
            await SendMagicPacketAsync(pc.MacAddress);
            sent++;
        }

        if (sent > 0)
            await LogEventAsync($"Wake-on-LAN sent to {sent} PC(s)");

        return sent;
    }

    public Task<int> ShutdownAsync(IEnumerable<Pc> pcs) =>
        SendCommandToOnlineAsync(pcs, _ => new ClientCommand { Type = CommandType.Shutdown }, "Shutdown");

    public Task<int> RestartAsync(IEnumerable<Pc> pcs) =>
        SendCommandToOnlineAsync(pcs, _ => new ClientCommand { Type = CommandType.Restart }, "Restart");

    /// <summary>
    /// Tells each online PC to pull and install whatever build is currently staged at ClientUpdateFile.
    /// The command carries the zip's SHA-256 signed for that one PC (see ClientUpdateAuth), so a PC
    /// only installs what this server staged. A PC that has no auth token yet cannot be sent a
    /// verifiable command and is skipped.
    /// </summary>
    public async Task<int> UpdateClientAsync(IEnumerable<Pc> pcs)
    {
        if (!File.Exists(ClientUpdateFile.FilePath))
        {
            await LogEventAsync($"Update client: no build staged at {ClientUpdateFile.FilePath}");
            return 0;
        }

        var sha256 = ClientUpdateAuth.ComputeSha256Hex(await File.ReadAllBytesAsync(ClientUpdateFile.FilePath));

        return await SendCommandToOnlineAsync(pcs, pc => pc.AuthTokenHash is null
            ? null
            : new ClientCommand
            {
                Type = CommandType.UpdateClient,
                UpdateSha256 = sha256,
                UpdateSignature = ClientUpdateAuth.Sign(pc.AuthTokenHash, sha256),
            }, "Update client",
            // From here on the tracker watches the PC: new version = updated, restart = unchanged, silence = no response.
            afterSend: (pc, connectionId) => _updates.Started(pc.MachineGuid, ClubHub.GetClientVersion(pc.MachineGuid), connectionId, DateTime.UtcNow));
    }

    /// <summary>Sends one command per online PC; <paramref name="buildCommand"/> returning null skips that PC.</summary>
    private async Task<int> SendCommandToOnlineAsync(IEnumerable<Pc> pcs, Func<Pc, ClientCommand?> buildCommand, string actionName,
        Action<Pc, string>? afterSend = null)
    {
        var sent = 0;
        foreach (var pc in pcs.Where(p => p.IsOnline))
        {
            if (!ClubHub.TryGetConnectionId(pc.MachineGuid, out var connectionId))
                continue;

            var command = buildCommand(pc);
            if (command is null)
                continue;

            await _hub.Clients.Client(connectionId).SendAsync(HubMethodNames.ReceiveCommand, command);
            afterSend?.Invoke(pc, connectionId);
            sent++;
        }

        if (sent > 0)
            await LogEventAsync($"{actionName} sent to {sent} PC(s)");

        return sent;
    }

    private async Task LogEventAsync(string message)
    {
        _db.EventLogEntries.Add(new EventLogEntry { Type = EventType.PowerCommand, Message = message });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Sends the magic packet as a directed broadcast on every active IPv4 interface's own subnet
    /// (handles multi-NIC/VLAN servers where the target PC is only reachable via one specific
    /// adapter, and where routers/switches do not forward the generic broadcast across subnets).
    /// Falls back to the plain 255.255.255.255 broadcast if no interface yielded a usable subnet -
    /// better to try the naive approach than to silently send nothing on an unusual network.
    /// </summary>
    private static async Task SendMagicPacketAsync(string macAddress)
    {
        var packet = WakeOnLan.BuildMagicPacket(macAddress);
        var sentFromAnyInterface = false;

        foreach (var (localAddress, broadcastAddress) in LocalNetworkInterfaces.GetBroadcastEndpoints())
        {
            try
            {
                using var udpClient = new UdpClient(new IPEndPoint(localAddress, 0)) { EnableBroadcast = true };
                await udpClient.SendAsync(packet, packet.Length, new IPEndPoint(broadcastAddress, WakeOnLan.Port));
                sentFromAnyInterface = true;
            }
            catch (SocketException)
            {
                // This interface couldn't send a broadcast (e.g. disabled broadcast, link down
                // mid-send) - other interfaces are still tried below.
            }
        }

        if (!sentFromAnyInterface)
        {
            try
            {
                using var udpClient = new UdpClient { EnableBroadcast = true };
                await udpClient.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, WakeOnLan.Port));
            }
            catch (SocketException)
            {
                // No usable network path for a broadcast at all - nothing more can be done here;
                // the caller only logs how many WoL packets were attempted.
            }
        }
    }
}
