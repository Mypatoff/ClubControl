using ClubControl.Server.Data;
using Microsoft.Data.Sqlite;

namespace ClubControl.Server.Tests;

public class DatabaseHealthTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ClubControl.DbHealthTests." + Guid.NewGuid().ToString("N"));

    public DatabaseHealthTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private string GoodDb(string name, DateTime? writtenUtc = null)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Pcs (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Pcs (Name) VALUES ('PC 1');";
            command.ExecuteNonQuery();
        }

        if (writtenUtc is { } time)
            File.SetLastWriteTimeUtc(path, time);
        return path;
    }

    private string BrokenDb(string name, DateTime? writtenUtc = null)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "this is not a database, it is just some text that a disk error left behind");
        if (writtenUtc is { } time)
            File.SetLastWriteTimeUtc(path, time);
        return path;
    }

    private static string FirstPcName(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Pcs LIMIT 1";
        return (string)command.ExecuteScalar()!;
    }

    // ---- Check ----

    [Fact]
    public void A_healthy_database_passes()
    {
        Assert.Null(DatabaseHealth.Check(GoodDb("ok.db")));
    }

    [Fact]
    public void A_missing_file_is_not_damage_the_first_start_creates_it()
    {
        Assert.Null(DatabaseHealth.Check(Path.Combine(_root, "not-there.db")));
    }

    [Fact]
    public void A_file_that_is_not_a_database_is_reported()
    {
        Assert.NotNull(DatabaseHealth.Check(BrokenDb("broken.db")));
    }

    [Fact]
    public void A_database_with_overwritten_pages_is_reported()
    {
        var path = GoodDb("hurt.db");
        SqliteConnection.ClearAllPools();

        // Overwrite the start of the file (the header and the first page) with garbage, like a disk error would.
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
            stream.Write(new byte[200].Select(_ => (byte)0x5A).ToArray());

        Assert.NotNull(DatabaseHealth.Check(path));
    }

    // ---- backups ----

    [Fact]
    public void The_newest_good_backup_is_chosen_and_damaged_or_older_ones_are_skipped()
    {
        var folder = Path.Combine(_root, "Backups");
        GoodDb(Path.Combine("Backups", "club_2026-10-20.db"), new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        var newestGood = GoodDb(Path.Combine("Backups", "club_2026-10-24.db"), new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc));
        BrokenDb(Path.Combine("Backups", "club_2026-10-27.db"), new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc)); // newest, but damaged
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a backup");

        Assert.Equal(newestGood, DatabaseHealth.FindNewestGoodBackup(folder));
    }

    [Fact]
    public void No_backup_folder_or_only_damaged_backups_means_no_choice()
    {
        Assert.Null(DatabaseHealth.FindNewestGoodBackup(Path.Combine(_root, "nothing-here")));

        var folder = Path.Combine(_root, "Backups2");
        BrokenDb(Path.Combine("Backups2", "club_2026-10-27.db"));
        Assert.Null(DatabaseHealth.FindNewestGoodBackup(folder));
    }

    // ---- restore ----

    [Fact]
    public void Restoring_keeps_the_damaged_file_and_puts_the_backup_in_its_place()
    {
        var database = BrokenDb("club.db");
        File.WriteAllText(database + "-journal", "leftover");
        var backup = GoodDb(Path.Combine("Backups", "club_2026-10-24.db"));

        var kept = DatabaseHealth.RestoreFromBackup(database, backup);

        Assert.Null(DatabaseHealth.Check(database));
        Assert.Equal("PC 1", FirstPcName(database));
        Assert.StartsWith("this is not a database", File.ReadAllText(kept));  // the damaged file is kept as it was
        Assert.True(File.Exists(kept + "-journal"));                          // and so are its leftover files
        Assert.False(File.Exists(database + "-journal"));                     // none left next to the restored database
        Assert.True(File.Exists(backup));                                     // the backup itself is untouched
        Assert.Equal(_root, Path.GetDirectoryName(kept));                     // kept next to the database, not in Backups
    }
}
