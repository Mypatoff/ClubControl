using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Hubs;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClubControl.Server.Services;

/// <summary>
/// Catches PCs that went offline without a clean SignalR disconnect (cable pulled, PC crashed).
/// ClubHub.OnDisconnectedAsync handles the clean-disconnect case immediately; this is the backstop.
/// </summary>
public class OfflineMonitorService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    // A check runs every 2s; logging every single failure while something stays broken would
    // flood the log forever. Log the first occurrence right away, then at most this often while
    // it keeps failing, plus one more line when it eventually recovers.
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OfflineMonitorService> _logger;

    private int _consecutiveFailures;
    private DateTime _lastLoggedFailureUtc = DateTime.MinValue;

    public OfflineMonitorService(IServiceScopeFactory scopeFactory, ILogger<OfflineMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync();

                if (_consecutiveFailures > 0)
                {
                    _logger.LogInformation(
                        "Offline monitor recovered after {FailureCount} consecutive failed checks.", _consecutiveFailures);
                    _consecutiveFailures = 0;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _consecutiveFailures++;
                var now = DateTime.UtcNow;

                if (_consecutiveFailures == 1 || now - _lastLoggedFailureUtc >= FailureLogInterval)
                {
                    _logger.LogError(ex,
                        "Offline monitor check failed ({FailureCount} consecutive failures so far); will keep retrying.",
                        _consecutiveFailures);
                    _lastLoggedFailureUtc = now;
                }

                // A missed check should never take the whole server down; the next tick tries again.
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task CheckOnceAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        await CheckAsync(
            scope.ServiceProvider.GetRequiredService<ClubDbContext>(),
            scope.ServiceProvider.GetRequiredService<HeartbeatTracker>(),
            scope.ServiceProvider.GetRequiredService<ClientUpdateTracker>(),
            DateTime.UtcNow);
    }

    /// <summary>One pass: marks every online PC offline whose last heartbeat is too old. Public and static so a test can run it.</summary>
    public static async Task CheckAsync(ClubDbContext db, HeartbeatTracker heartbeats, ClientUpdateTracker updates, DateTime now)
    {
        var timer = SlowLog.Start("Offline monitor check");

        // Only three columns of the online PCs are read (nothing tracked); a PC is loaded for change only when it has gone quiet.
        var online = await db.Pcs.AsNoTracking()
            .Where(p => p.IsOnline)
            .Select(p => new { p.Id, p.MachineGuid, p.LastHeartbeatUtc })
            .ToListAsync();

        foreach (var candidate in online)
        {
            if (!OfflineDetector.IsOffline(heartbeats.Latest(candidate.MachineGuid, candidate.LastHeartbeatUtc), now))
                continue;

            var pc = await db.Pcs.FirstOrDefaultAsync(p => p.Id == candidate.Id);
            if (pc is null || !pc.IsOnline)
                continue;

            // Heartbeats are saved only about once a minute: keep the real last one, the "client missing" check counts from it.
            var lastHeard = heartbeats.Latest(pc.MachineGuid, pc.LastHeartbeatUtc);
            if (!OfflineDetector.IsOffline(lastHeard, now))
                continue; // it spoke up again while we were looking

            pc.LastHeartbeatUtc = lastHeard;
            pc.IsOnline = false;
            db.EventLogEntries.Add(new EventLogEntry
            {
                Type = EventType.PcDisconnected,
                PcId = pc.Id,
                Message = $"PC #{pc.Number?.ToString() ?? "?"} stopped sending heartbeats",
            });

            // The socket may still look "connected" at the transport level even though the PC
            // stopped heartbeating (crash, cable pull, hung OS). Evict it now so no command is ever
            // addressed to this dead connection - see ClubHub.ForgetMachine.
            ClubHub.ForgetMachine(pc.MachineGuid);
        }

        // "Update client" that got no answer in time (no restart, no new version): say so in the log.
        foreach (var (machineGuid, info) in updates.CheckTimeouts(now))
        {
            var pc = await db.Pcs.FirstOrDefaultAsync(p => p.MachineGuid == machineGuid);
            db.EventLogEntries.Add(new EventLogEntry
            {
                Type = EventType.Warning,
                PcId = pc?.Id,
                Message = ClientUpdateTracker.LogMessage($"PC #{pc?.Number?.ToString() ?? "?"}", info),
            });
        }

        timer.Step("checks");
        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();
        timer.Step("save");
        timer.Finish();
    }
}
