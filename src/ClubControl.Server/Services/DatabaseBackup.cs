using System.IO;
using Microsoft.Data.Sqlite;

namespace ClubControl.Server.Services;

/// <summary>
/// One database backup, written so a backup file with its final name is always a complete one.
/// </summary>
public static class DatabaseBackup
{
    /// <summary>
    /// Copies the open <paramref name="source"/> database to <paramref name="destinationPath"/> with
    /// Sqlite's online backup API (safe while the server is running). The copy is written to a
    /// ".tmp" file first and only renamed once it is complete: BackupService treats "the file for
    /// today exists" as "today's backup is done", so a half-written file under the real name would
    /// stop it retrying and could even push older good backups out of the retention window.
    /// </summary>
    public static void CopyTo(SqliteConnection source, string destinationPath)
    {
        var tempPath = destinationPath + ".tmp";

        try
        {
            File.Delete(tempPath); // left behind by an earlier crash, if any

            // Pooling off: otherwise Dispose keeps the file open in the pool and the rename below fails.
            using (var destination = new SqliteConnection($"Data Source={tempPath};Pooling=False"))
            {
                destination.Open();
                source.BackupDatabase(destination);
            }

            File.Move(tempPath, destinationPath);
        }
        catch
        {
            try { File.Delete(tempPath); } catch (IOException) { }
            throw;
        }
    }
}
