using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClubControl.Server.Services;

/// <summary>
/// Daily database backup (SPEC section 10): one backup per calendar day in
/// %ProgramData%\ClubControl\Backups, keeping the newest <see cref="BackupRetention.KeepCount"/>.
/// Uses Sqlite's online backup API, so it is safe to run while the server is serving requests.
/// </summary>
public class BackupService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupService> _logger;

    // The check retries every hour, so a backup that keeps failing (disk full, folder not writable)
    // would add 24 event-log lines a day. Report the first failure in a row and stay quiet until
    // a backup has worked again.
    private bool _failureReported;

    public BackupService(IServiceScopeFactory scopeFactory, ILogger<BackupService> logger)
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
                await BackupIfDueAsync();
                _failureReported = false;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // A missed backup should never take the server down; the next hourly check tries again.
                _logger.LogError(ex, "Daily database backup failed; will retry at the next hourly check.");
                await ReportFailureOnceAsync(ex);
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    /// <summary>Puts a visible line in the admin panel's event log - nobody reads the server's console output.</summary>
    private async Task ReportFailureOnceAsync(Exception ex)
    {
        if (_failureReported)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            db.EventLogEntries.Add(new EventLogEntry
            {
                Type = EventType.Warning,
                Message = $"Daily database backup failed: {ex.Message}",
            });
            await db.SaveChangesAsync();
            _failureReported = true;
        }
        catch (Exception)
        {
            // If even the event log cannot be written (the same disk problem, most likely) there is
            // nowhere left to report to; the ILogger line above still has it.
        }
    }

    private async Task BackupIfDueAsync()
    {
        Directory.CreateDirectory(DbPaths.BackupDirectory);

        var todayFile = Path.Combine(DbPaths.BackupDirectory, $"club_{DateTime.Now:yyyy-MM-dd}.db");
        if (!File.Exists(todayFile))
            await BackupNowAsync(todayFile);

        DeleteOldBackups();
    }

    private async Task BackupNowAsync(string destinationPath)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var source = (SqliteConnection)db.Database.GetDbConnection();

        var wasClosed = source.State != ConnectionState.Open;
        if (wasClosed)
            await source.OpenAsync();

        try
        {
            DatabaseBackup.CopyTo(source, destinationPath);
        }
        finally
        {
            if (wasClosed)
                await source.CloseAsync();
        }
    }

    private static void DeleteOldBackups()
    {
        var directory = DbPaths.BackupDirectory;

        // Age comes from the date encoded in the file name, not filesystem creation time - that
        // metadata can change on copy/restore/backup-to-a-new-PC and would make retention
        // nondeterministic. Anything that doesn't match the expected "club_yyyy-MM-dd.db" pattern
        // is left alone entirely rather than guessed at.
        var entries = Directory.GetFiles(directory, "club_*.db")
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => (Name: name!, Date: BackupRetention.TryParseBackupDate(name!)))
            .Where(f => f.Date.HasValue)
            .Select(f => new BackupRetention.BackupEntry(f.Name, f.Date!.Value))
            .ToList();

        var namesToDelete = BackupRetention.FilesToDelete(entries);

        foreach (var name in namesToDelete)
            File.Delete(Path.Combine(directory, name));
    }
}
