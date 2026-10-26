using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Hubs;
using ClubControl.Server.Services;
using ClubControl.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>
/// Gives each test its own in-memory SQLite database (same engine as production, so transactions and
/// constraints behave for real) with two tariffs and three PCs: #1 and #2 on Standard, #3 on VIP.
/// Every PC is offline, so SessionService never tries to push commands to a client.
/// </summary>
public abstract class SessionTestBase : IDisposable
{
    protected const int StandardPrice = 10_000;
    protected const int StandardMin = 2_000;
    protected const int StandardMax = 50_000;
    protected const int VipPrice = 15_000;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ClubDbContext> _options;

    protected Guid Pc1 { get; } = Guid.NewGuid();
    protected Guid Pc2 { get; } = Guid.NewGuid();
    protected Guid VipPc { get; } = Guid.NewGuid();

    protected SessionTestBase()
    {
        // The in-memory database lives only as long as this connection stays open.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<ClubDbContext>().UseSqlite(_connection).Options;

        using var db = NewDb();
        db.Database.EnsureCreated();

        var standard = new Tariff { Name = "Standard", Kind = ResourceKind.Computer, PricePerHourUzs = StandardPrice, MinPaymentUzs = StandardMin, MaxPaymentUzs = StandardMax };
        var vip = new Tariff { Name = "VIP", Kind = ResourceKind.Computer, PricePerHourUzs = VipPrice, MinPaymentUzs = 3_000, MaxPaymentUzs = 75_000 };
        db.Tariffs.AddRange(standard, vip);
        db.SaveChanges();

        db.Pcs.AddRange(
            new Pc { MachineGuid = Pc1, Number = 1, TariffId = standard.Id },
            new Pc { MachineGuid = Pc2, Number = 2, TariffId = standard.Id },
            new Pc { MachineGuid = VipPc, Number = 3, TariffId = vip.Id });
        db.SaveChanges();
    }

    /// <summary>A fresh context on the same database - reading through it proves what was really saved.</summary>
    protected ClubDbContext NewDb() => new(_options);

    protected static SessionService ServiceFor(ClubDbContext db) => new(db, new UnusedHubContext());

    protected async Task<Pc> GetPcAsync(Guid machineGuid)
    {
        using var db = NewDb();
        return await db.Pcs.AsNoTracking().SingleAsync(p => p.MachineGuid == machineGuid);
    }

    protected async Task<Session> GetOnlySessionAsync()
    {
        using var db = NewDb();
        return await db.Sessions.AsNoTracking().Include(s => s.Segments).SingleAsync();
    }

    /// <summary>
    /// Pretends the session began <paramref name="shift"/> earlier (the server clock is the only clock).
    /// Tests use shifts a few seconds short of a whole minute so a slow test run cannot tip a
    /// charge into the next started minute.
    /// </summary>
    protected async Task BackdateSessionAsync(TimeSpan shift)
    {
        using var db = NewDb();
        var session = await db.Sessions.Include(s => s.Segments).SingleAsync();
        session.StartUtc -= shift;
        if (session.PlannedEndUtc.HasValue)
            session.PlannedEndUtc -= shift;
        foreach (var segment in session.Segments)
            segment.StartUtc -= shift;
        await db.SaveChangesAsync();
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>All test PCs are offline, so SessionService must never touch the hub; fail loudly if it does.</summary>
    private sealed class UnusedHubContext : IHubContext<ClubHub>
    {
        public IHubClients Clients => throw new InvalidOperationException("Offline PCs must not receive commands.");
        public IGroupManager Groups => throw new InvalidOperationException("Not used.");
    }
}
