using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClubControl.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Data;

/// <summary>
/// Brings an existing club.db up to the current schema at startup. Before any pending migration
/// touches a database that already holds data, a full copy is saved next to the daily backups, so
/// a failed or unwanted upgrade never costs the club its history.
/// </summary>
public static class DatabaseUpgrade
{
    public static async Task MigrateAsync(ClubDbContext db, string backupDirectory)
    {
        var pending = await db.Database.GetPendingMigrationsAsync();
        if (pending.Any() && await HasTablesAsync(db))
        {
            // Tables but no migration history: made by a very old build that created the schema
            // directly. Its exact columns are unknown, so applying the migrations would fail
            // halfway with "table already exists" - stop with an explanation instead.
            if (!(await db.Database.GetAppliedMigrationsAsync()).Any())
                throw new InvalidOperationException(
                    "club.db was created by a pre-migrations version of ClubControl and cannot be upgraded in place. " +
                    "Copy it somewhere safe, then delete it so a fresh database is created (see README).");

            BackupBeforeUpgrade(db, backupDirectory);
        }

        await db.Database.MigrateAsync();
    }

    private static async Task<bool> HasTablesAsync(ClubDbContext db)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Pcs'";
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static void BackupBeforeUpgrade(ClubDbContext db, string backupDirectory)
    {
        Directory.CreateDirectory(backupDirectory);
        // Not the "club_yyyy-MM-dd.db" pattern, so the daily-backup cleanup never deletes it.
        var path = Path.Combine(backupDirectory, $"club_before-upgrade_{DateTime.Now:yyyy-MM-dd_HHmmss}.db");
        DatabaseBackup.CopyTo((SqliteConnection)db.Database.GetDbConnection(), path);
    }
}
