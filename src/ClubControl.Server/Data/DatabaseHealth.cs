using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace ClubControl.Server.Data;

/// <summary>
/// Looks at club.db before the server opens it for real, so a file damaged by a power cut, a failing
/// disk or a virus is noticed at startup instead of showing up as strange errors later. A damaged file
/// is never deleted: it is moved aside and the newest backup that passes the same check takes its place,
/// but only when the admin says so.
/// </summary>
public static class DatabaseHealth
{
    // SQLite result codes for "the file is damaged" / "this is not a database file".
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    /// <summary>
    /// null when the file is fine (or does not exist yet: a first start creates it). Otherwise what is
    /// wrong, in SQLite's words. Any other problem (file locked, folder unreadable) is not "damage" and is thrown as it is.
    /// </summary>
    public static string? Check(string databasePath)
    {
        if (!File.Exists(databasePath))
            return null;

        try
        {
            // Opened normally (not read-only): after a power cut SQLite finishes the interrupted write itself on open,
            // and a read-only connection could not and would report a healthy file as broken.
            var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var first = command.ExecuteScalar()?.ToString();
            return first == "ok" ? null : first ?? "the integrity check gave no answer";
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
        {
            return ex.Message;
        }
    }

    /// <summary>The newest backup in the folder that passes <see cref="Check"/>, or null if there is none.</summary>
    public static string? FindNewestGoodBackup(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
            return null;

        return Directory.GetFiles(backupDirectory, "club_*.db")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault(IsGood);
    }

    private static bool IsGood(string path)
    {
        try
        {
            return Check(path) is null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Puts <paramref name="backupPath"/> in place of the damaged database. The damaged file (and its
    /// leftover journal files) is kept next to it as <c>club_damaged_&lt;date&gt;.db</c>, never deleted.
    /// Returns where it was kept.
    /// </summary>
    public static string RestoreFromBackup(string databasePath, string backupPath)
    {
        // Let go of any file handle SQLite may still hold before moving the file.
        SqliteConnection.ClearAllPools();

        var folder = Path.GetDirectoryName(databasePath)!;
        var kept = Path.Combine(folder, $"club_damaged_{DateTime.Now:yyyy-MM-dd_HHmmss}.db");

        File.Move(databasePath, kept);
        foreach (var leftover in new[] { "-journal", "-wal", "-shm" })
        {
            if (File.Exists(databasePath + leftover))
                File.Move(databasePath + leftover, kept + leftover);
        }

        File.Copy(backupPath, databasePath);
        return kept;
    }
}
