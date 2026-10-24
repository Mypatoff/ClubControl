using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Hubs;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
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

    public PowerService(ClubDbContext db, IHubContext<ClubHub> hub)
    {
        _db = db;
        _hub = hub;
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

    public Task<int> ShutdownAsync(IEnumerable<Pc> pcs) => SendCommandToOnlineAsync(pcs, CommandType.Shutdown, "Shutdown");

    public Task<int> RestartAsync(IEnumerable<Pc> pcs) => SendCommandToOnlineAsync(pcs, CommandType.Restart, "Restart");

    private async Task<int> SendCommandToOnlineAsync(IEnumerable<Pc> pcs, CommandType type, string actionName)
    {
        var sent = 0;
        foreach (var pc in pcs.Where(p => p.IsOnline))
        {
            if (!ClubHub.TryGetConnectionId(pc.MachineGuid, out var connectionId))
                continue;

            await _hub.Clients.Client(connectionId).SendAsync(HubMethodNames.ReceiveCommand, new ClientCommand { Type = type });
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

    private static async Task SendMagicPacketAsync(string macAddress)
    {
        var packet = WakeOnLan.BuildMagicPacket(macAddress);
        using var udpClient = new UdpClient { EnableBroadcast = true };
        await udpClient.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, WakeOnLan.Port));
    }
}
