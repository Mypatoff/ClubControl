using ClubControl.Server.Services;

namespace ClubControl.Server.Tests;

public class ClientUpdateFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ClubControl.ClientUpdateFileTests." + Guid.NewGuid().ToString("N"));

    public ClientUpdateFileTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Zip(string name, DateTime? writtenUtc = null)
    {
        var path = Path.Combine(_root, name, "ClubControl.Client.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, name);
        if (writtenUtc is { } time)
            File.SetLastWriteTimeUtc(path, time);
        return path;
    }

    private string NoZip(string name) => Path.Combine(_root, name, "ClubControl.Client.zip");

    // ---- which zip is handed out ----

    [Fact]
    public void Serves_the_one_that_exists()
    {
        var server = Zip("server");
        var legacy = NoZip("legacy");
        Assert.Equal(server, ClientUpdateFile.PickToServe(server, legacy));

        var server2 = NoZip("server2");
        var legacy2 = Zip("legacy2");
        Assert.Equal(legacy2, ClientUpdateFile.PickToServe(server2, legacy2));
    }

    [Fact]
    public void Serves_the_newer_one_when_both_exist()
    {
        var server = Zip("a-server", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        var legacy = Zip("a-legacy", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(legacy, ClientUpdateFile.PickToServe(server, legacy));

        var server2 = Zip("b-server", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        var legacy2 = Zip("b-legacy", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(server2, ClientUpdateFile.PickToServe(server2, legacy2));
    }

    [Fact]
    public void With_no_zip_at_all_it_names_the_server_folder_one()
    {
        var server = NoZip("c-server");
        Assert.Equal(server, ClientUpdateFile.PickToServe(server, NoZip("c-legacy")));
    }

    // ---- where a new zip is written ----

    [Fact]
    public void Writes_next_to_the_server_when_that_folder_is_writable()
    {
        var server = NoZip("d-server");

        var target = ClientUpdateFile.PickToWrite(server, NoZip("d-legacy"));

        Assert.Equal(server, target);
        Assert.True(Directory.Exists(Path.GetDirectoryName(server)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(server)!)); // the write test leaves nothing behind
    }

    [Fact]
    public void Falls_back_to_the_old_place_when_the_server_folder_cannot_be_written()
    {
        // A file where the folder should be makes the preferred folder impossible to create (like a protected folder).
        var blocker = Path.Combine(_root, "e-blocked");
        File.WriteAllText(blocker, "not a folder");
        var server = Path.Combine(blocker, "ClientUpdate", "ClubControl.Client.zip");
        var legacy = NoZip("e-legacy");

        var target = ClientUpdateFile.PickToWrite(server, legacy);

        Assert.Equal(legacy, target);
        Assert.True(Directory.Exists(Path.GetDirectoryName(legacy)));
    }
}
