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

    // The last schema before the bar (AddBar).
    private const string BeforeBarMigration = "20261028181744_AddQueryIndexes";

    // The last schema before bar sales were tied to shifts (AddBarShifts).
    private const string BeforeBarShiftsMigration = "20261107155458_AddBar";

    // The last schema before a PC's tariff became a real foreign key (AddPcTariffForeignKey).
    private const string BeforeTariffForeignKeyMigration = "20261107163706_AddBarShifts";

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
    public async Task A_database_from_before_the_bar_gets_the_bar_tables_and_keeps_its_sessions_and_items()
    {
        using (var old = NewDb())
            await old.GetService<IMigrator>().MigrateAsync(BeforeBarMigration);

        // Written by the build before the bar: a PC with a finished session that had one item on it.
        Exec("INSERT INTO Tariffs (Name, Kind, PricePerHourUzs, MinPaymentUzs, MaxPaymentUzs) VALUES ('Standard', 0, 10000, 500, 50000)");
        Exec("INSERT INTO Pcs (MachineGuid, Kind, IsManual, Number, Name, TariffId, Enabled, IpAddress, MacAddress, Hostname, State, IsOnline, LastHeartbeatUtc) " +
             "VALUES ('5A65BF0C-0BB8-4129-A8BE-EE1FB00332AF', 0, 0, 1, '', 1, 1, '', '', '', 0, 0, '0001-01-01 00:00:00')");
        Exec("INSERT INTO Sessions (PcId, Type, Status, StartUtc, AmountUzs, IsFree, CreatedAtUtc) VALUES (1, 1, 1, '2026-10-30 10:00:00', 4000, 0, '2026-10-30 10:00:00')");
        Exec("INSERT INTO SessionExtras (SessionId, Description, AmountUzs, CreatedUtc) VALUES (1, 'Cola', 5000, '2026-10-30 10:30:00')");

        using (var db = NewDb())
            await DatabaseUpgrade.MigrateAsync(db, _backupDirectory);

        using var upgraded = NewDb();
        Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
        Assert.Equal("Cola", (await upgraded.SessionExtras.AsNoTracking().SingleAsync()).Description);
        Assert.Equal(4_000, (await upgraded.Sessions.AsNoTracking().SingleAsync()).AmountUzs);

        // The bar is there and usable, and the new setting starts at 0.
        Assert.Empty(upgraded.BarProducts);
        Assert.Empty(upgraded.BarSales);
        Assert.Empty(upgraded.BarStockMovements);
        Assert.Equal(0, (await SettingsHelper.GetOrCreateAsync(upgraded)).BarCupPriceUzs);
        Assert.Single(Directory.GetFiles(_backupDirectory, "club_before-upgrade_*.db"));
    }

    [Fact]
    public async Task Bar_sales_made_before_shifts_were_tracked_get_the_shift_that_was_open_at_the_time()
    {
        using (var old = NewDb())
            await old.GetService<IMigrator>().MigrateAsync(BeforeBarShiftsMigration);

        Exec("INSERT INTO Shifts (OpenedUtc, ClosedUtc) VALUES ('2026-11-01 08:00:00', '2026-11-01 20:00:00')");   // shift 1
        Exec("INSERT INTO Shifts (OpenedUtc, ClosedUtc) VALUES ('2026-11-02 08:00:00', NULL)");                   // shift 2, still open
        Exec("INSERT INTO BarProducts (Name, PriceUzs, Stock, Archived, CreatedUtc) VALUES ('Cola', 5000, 10, 0, '2026-10-01 00:00:00')");

        void Sale(string created, string? paid, int unpaid) => Exec(
            "INSERT INTO BarSales (BarProductId, ProductName, UnitPriceUzs, Quantity, Cups, CupPriceUzs, TotalUzs, CreatedUtc, PaidUtc, " +
            "PcLabel, IsUnpaid, UnpaidComment) VALUES (1, 'Cola', 5000, 1, 0, 0, 5000, '" + created + "', " +
            (paid is null ? "NULL" : "'" + paid + "'") + ", '', " + unpaid + ", '')");

        Sale("2026-11-01 10:00:00", null, 0);                       // inside shift 1
        Sale("2026-11-01 11:00:00", "2026-11-02 09:00:00", 1);      // credit from shift 1, paid during shift 2
        Sale("2026-11-02 12:00:00", null, 0);                       // inside shift 2
        Sale("2026-10-15 12:00:00", null, 0);                       // before any shift (old history)

        using (var db = NewDb())
            await DatabaseUpgrade.MigrateAsync(db, _backupDirectory);

        using var upgraded = NewDb();
        var sales = await upgraded.BarSales.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(new int?[] { 1, 1, 2, null }, sales.Select(s => s.ShiftId));
        Assert.Equal(new int?[] { null, 2, null, null }, sales.Select(s => s.PaidShiftId));
    }

    [Fact]
    public async Task A_PC_whose_tariff_is_gone_becomes_a_new_PC_when_the_tariff_foreign_key_is_added()
    {
        using (var old = NewDb())
            await old.GetService<IMigrator>().MigrateAsync(BeforeTariffForeignKeyMigration);

        Exec("INSERT INTO Tariffs (Name, Kind, PricePerHourUzs, MinPaymentUzs, MaxPaymentUzs) VALUES ('Standard', 0, 10000, 500, 50000)");
        void Pc(string guid, int number, string tariffId) => Exec(
            "INSERT INTO Pcs (MachineGuid, Kind, IsManual, Number, Name, TariffId, Enabled, IpAddress, MacAddress, Hostname, State, IsOnline, LastHeartbeatUtc) " +
            $"VALUES ('{guid}', 0, 0, {number}, '', {tariffId}, 1, '', '', '', 0, 0, '0001-01-01 00:00:00')");
        Pc("5A65BF0C-0BB8-4129-A8BE-EE1FB00332A1", 1, "1");    // tariff exists
        Pc("5A65BF0C-0BB8-4129-A8BE-EE1FB00332A2", 2, "99");   // tariff 99 was deleted long ago
        Pc("5A65BF0C-0BB8-4129-A8BE-EE1FB00332A3", 3, "NULL"); // never had one

        using (var db = NewDb())
            await DatabaseUpgrade.MigrateAsync(db, _backupDirectory);

        using var upgraded = NewDb();
        var pcs = await upgraded.Pcs.AsNoTracking().OrderBy(p => p.Number).ToListAsync();
        Assert.Equal(new int?[] { 1, null, null }, pcs.Select(p => p.TariffId));
        Assert.Equal(new int?[] { 1, 2, 3 }, pcs.Select(p => p.Number)); // nothing else about them changed
    }

    [Fact]
    public async Task The_database_refuses_a_PC_with_a_missing_tariff_and_the_deletion_of_a_tariff_in_use()
    {
        using (var db = NewDb())
            await DatabaseUpgrade.MigrateAsync(db, _backupDirectory);

        using (var db = NewDb())
        {
            db.Pcs.Add(new Pc { MachineGuid = Guid.NewGuid(), Number = 1, TariffId = 999 });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        int tariffId;
        using (var db = NewDb())
        {
            var tariff = new Tariff { Name = "Standard", Kind = ResourceKind.Computer, PricePerHourUzs = 10_000, MinPaymentUzs = 500, MaxPaymentUzs = 50_000 };
            db.Tariffs.Add(tariff);
            await db.SaveChangesAsync();
            db.Pcs.Add(new Pc { MachineGuid = Guid.NewGuid(), Number = 1, TariffId = tariff.Id });
            await db.SaveChangesAsync();
            tariffId = tariff.Id;
        }

        // Like the Settings window: the tariff is deleted without any PC loaded (EF would clear a tracked PC's tariff by itself).
        using (var db = NewDb())
        {
            var tariff = await db.Tariffs.SingleAsync(t => t.Id == tariffId);
            db.Tariffs.Remove(tariff);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
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
