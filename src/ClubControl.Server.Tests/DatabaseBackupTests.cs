using ClubControl.Server.Services;
using Microsoft.Data.Sqlite;

namespace ClubControl.Server.Tests;

/// <summary>DatabaseBackup against real SQLite files in a temp folder.</summary>
public class DatabaseBackupTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ClubControl.BackupTests." + Guid.NewGuid().ToString("N"));

    public DatabaseBackupTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // pooled connections keep files open and block the delete
        Directory.Delete(_folder, recursive: true);
    }

    private SqliteConnection OpenSourceWithOneRow()
    {
        var source = new SqliteConnection($"Data Source={Path.Combine(_folder, "source.db")};Pooling=False");
        source.Open();

        using var command = source.CreateCommand();
        command.CommandText = "CREATE TABLE Pcs (Id INTEGER, Name TEXT); INSERT INTO Pcs VALUES (1, 'PC one');";
        command.ExecuteNonQuery();
        return source;
    }

    [Fact]
    public void CopyTo_WritesACompleteBackupAndLeavesNoTempFile()
    {
        using var source = OpenSourceWithOneRow();
        var destination = Path.Combine(_folder, "club_2026-10-27.db");

        DatabaseBackup.CopyTo(source, destination);

        Assert.False(File.Exists(destination + ".tmp"));
        using var backup = new SqliteConnection($"Data Source={destination};Pooling=False");
        backup.Open();
        using var command = backup.CreateCommand();
        command.CommandText = "SELECT Name FROM Pcs WHERE Id = 1";
        Assert.Equal("PC one", command.ExecuteScalar());
    }

    [Fact]
    public void CopyTo_WhenTheCopyFails_LeavesNoFileUnderTheBackupName()
    {
        // The bug: a failed copy left a partial file with the real name, so "today's backup exists"
        // was true forever and the backup was never retried. A closed source makes the copy throw
        // after the destination file has already been created.
        using var closedSource = new SqliteConnection($"Data Source={Path.Combine(_folder, "source.db")};Pooling=False");
        var destination = Path.Combine(_folder, "club_2026-10-27.db");

        Assert.ThrowsAny<Exception>(() => DatabaseBackup.CopyTo(closedSource, destination));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".tmp"));
    }

    [Fact]
    public void CopyTo_ReplacesAStaleTempFileFromAnEarlierCrash()
    {
        using var source = OpenSourceWithOneRow();
        var destination = Path.Combine(_folder, "club_2026-10-27.db");
        File.WriteAllText(destination + ".tmp", "garbage from a crashed run");

        DatabaseBackup.CopyTo(source, destination);

        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(destination + ".tmp"));
    }

    [Fact]
    public void CopyTo_NeverOverwritesAnExistingBackup()
    {
        using var source = OpenSourceWithOneRow();
        var destination = Path.Combine(_folder, "club_2026-10-27.db");
        File.WriteAllText(destination, "existing good backup");

        Assert.ThrowsAny<IOException>(() => DatabaseBackup.CopyTo(source, destination));

        Assert.Equal("existing good backup", File.ReadAllText(destination));
        Assert.False(File.Exists(destination + ".tmp"));
    }
}
