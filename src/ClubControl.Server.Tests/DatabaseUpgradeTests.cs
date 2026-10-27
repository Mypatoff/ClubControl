using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClubControl.Server.Tests;

/// <summary>Starting the server on a club.db written by an older build (see <see cref="DatabaseUpgrade"/>).</summary>
public class DatabaseUpgradeTests : IDisposable
{
    // The last schema before Pc.Name and ResourceKind.Other existed.
    private const string PreviousMigration = "20261026162846_AddClientUnlockPasswordHash";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _backupDirectory = Path.Combine(Path.GetTempPath(), "clubcontrol-upgrade-" + Guid.NewGuid());

    public DatabaseUpgradeTests() => _connection.Open();

    private ClubDbContext NewDb() => new(new DbContextOptionsBuilder<ClubDbContext>().UseSqlite(_connection).Options);

    private void Exec(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task An_old_database_with_billiard_rows_upgrades_and_keeps_them_as_Other_resources()
    {
        using (var old = NewDb())
            await old.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        // Written by the old build: a billiard tariff and a billiard table (Kind 1), no Name column yet.
        Exec("INSERT INTO Tariffs (Name, Kind, PricePerHourUzs, MinPaymentUzs, MaxPaymentUzs) VALUES ('Biliard', 1, 40000, 500, 5000000)");
        Exec("INSERT INTO Pcs (MachineGuid, Kind, IsManual, Number, TariffId, Enabled, IpAddress, MacAddress, Hostname, State, IsOnline, LastHeartbeatUtc) " +
             "VALUES ('5A65BF0C-0BB8-4129-A8BE-EE1FB00332AF', 1, 1, 31, 1, 1, '', '', '', 0, 0, '0001-01-01 00:00:00')");

        using (var db = NewDb())
            await DatabaseUpgrade.MigrateAsync(db, _backupDirectory);

        using var upgraded = NewDb();
        var table = await upgraded.Pcs.AsNoTracking().SingleAsync();
        Assert.Equal(ResourceKind.Other, table.Kind);
        Assert.Equal("", table.Name);
        Assert.Equal(31, table.Number);
        Assert.Equal(ResourceKind.Other, (await upgraded.Tariffs.AsNoTracking().SingleAsync()).Kind);
        Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());

        // A copy of the old database was saved first.
        Assert.Single(Directory.GetFiles(_backupDirectory, "club_before-upgrade_*.db"));
    }

    [Fact]
    public async Task A_new_database_is_created_without_a_backup()
    {
        using (var db = NewDb())
            await DatabaseUpgrade.MigrateAsync(db, _backupDirectory);

        using var created = NewDb();
        Assert.Empty(await created.Database.GetPendingMigrationsAsync());
        Assert.False(Directory.Exists(_backupDirectory));
    }

    [Fact]
    public async Task A_database_from_before_migrations_existed_is_refused_with_an_explanation()
    {
        using (var legacy = NewDb())
            await legacy.Database.EnsureCreatedAsync(); // tables, but no migration history

        using var db = NewDb();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseUpgrade.MigrateAsync(db, _backupDirectory));
        Assert.Contains("pre-migrations", error.Message);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_backupDirectory))
            Directory.Delete(_backupDirectory, recursive: true);
    }
}
