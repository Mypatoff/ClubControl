using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
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

        foreach (var (localAddress, broadcastAddress) in GetInterfaceBroadcastEndpoints())
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

    /// <summary>Every active, non-loopback IPv4 interface's own address paired with its subnet's directed broadcast address.</summary>
    private static List<(IPAddress Local, IPAddress Broadcast)> GetInterfaceBroadcastEndpoints()
    {
        var endpoints = new List<(IPAddress Local, IPAddress Broadcast)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                    continue;

                try
                {
                    var broadcast = WakeOnLan.GetDirectedBroadcastAddress(unicast.Address, unicast.IPv4Mask);
                    endpoints.Add((unicast.Address, broadcast));
                }
                catch (ArgumentException)
                {
                    // Malformed/unusual address-mask pairing for this adapter - skip it, not fatal.
                }
            }
        }

        return endpoints;
    }
}
