using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClubControl.Server.Services;

/// <summary>
/// Notices a gaming PC that is switched on while its ClubControl client is gone (files deleted,
/// uninstalled, process killed). A PC that stops sending heartbeats is probed on the network
/// (<see cref="PcPowerProbe"/>); the verdict is kept in <see cref="ClientMissingTracker"/> and shown on
/// the PC's card. A separate loop from <see cref="OfflineMonitorService"/>, because a probe can take a
/// few seconds and must never delay marking PCs offline.
/// </summary>
public class ClientMissingMonitorService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ClientMissingTracker _tracker;
    private readonly ILogger<ClientMissingMonitorService> _logger;

    // After a server restart every PC looks offline until its client reconnects, so the offline time
    // is counted from the start of the server at the earliest.
    private readonly DateTime _startedUtc = DateTime.UtcNow;

    public ClientMissingMonitorService(IServiceScopeFactory scopeFactory, ClientMissingTracker tracker, ILogger<ClientMissingMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _tracker = tracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Same rule as OfflineMonitorService: a missed check never takes the server down.
                _logger.LogWarning(ex, "Client-missing check failed; will keep retrying.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task CheckOnceAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var updates = scope.ServiceProvider.GetRequiredService<ClientUpdateTracker>();
        var rejections = scope.ServiceProvider.GetRequiredService<AuthRejectionTracker>();

        var now = DateTime.UtcNow;
        var pcs = await db.Pcs
            .Where(p => !p.IsManual && p.Kind == ResourceKind.Computer && p.Enabled && p.Number != null)
            .ToListAsync();

        var due = pcs.Where(pc => ShouldCheck(pc, updates, rejections, now)).ToList();
        if (due.Count > 0)
        {
            var answers = await Task.WhenAll(due.Select(pc => PcPowerProbe.IsPoweredOnAsync(pc.IpAddress, pc.MacAddress)));

            for (var i = 0; i < due.Count; i++)
            {
                if (!_tracker.RecordProbe(due[i].MachineGuid, answers[i], now))
                    continue;

                db.EventLogEntries.Add(new EventLogEntry
                {
                    Type = EventType.Warning,
                    PcId = due[i].Id,
                    Message = $"PC #{due[i].Number} is switched on but the ClubControl client is not running (deleted, uninstalled or closed). Check this PC",
                });
            }
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();
    }

    private bool ShouldCheck(Pc pc, ClientUpdateTracker updates, AuthRejectionTracker rejections, DateTime now)
    {
        // A rejected client is running, it just is not let in: "client missing" would be the wrong diagnosis.
        if (pc.IsOnline || rejections.IsRejected(pc.MachineGuid, now))
        {
            _tracker.Forget(pc.MachineGuid);
            return false;
        }

        // "Update client" restarts the client on purpose: that gap is not a missing client.
        if (updates.Get(pc.MachineGuid, now)?.Status is ClientUpdateStatus.Waiting or ClientUpdateStatus.Updating)
            return false;

        var offlineSince = pc.LastHeartbeatUtc > _startedUtc ? pc.LastHeartbeatUtc : _startedUtc;
        return _tracker.ShouldProbe(pc.MachineGuid, offlineSince, now);
    }
}
