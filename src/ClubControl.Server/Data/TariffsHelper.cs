using System.Threading.Tasks;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Data;

public static class TariffsHelper
{
    /// <summary>Default prices so the server has something billable before the admin edits them (Phase 4).</summary>
    public static async Task EnsureDefaultsAsync(ClubDbContext db)
    {
        if (!await db.Tariffs.AnyAsync(t => t.Class == PcClass.Standard))
            db.Tariffs.Add(new Tariff { Class = PcClass.Standard, PricePerHourUzs = 10_000, MinPaymentUzs = 2_000, MaxPaymentUzs = 50_000 });

        if (!await db.Tariffs.AnyAsync(t => t.Class == PcClass.Vip))
            db.Tariffs.Add(new Tariff { Class = PcClass.Vip, PricePerHourUzs = 15_000, MinPaymentUzs = 3_000, MaxPaymentUzs = 75_000 });

        await db.SaveChangesAsync();
    }

    public static Task<Tariff?> GetAsync(ClubDbContext db, PcClass pcClass) =>
        db.Tariffs.FirstOrDefaultAsync(t => t.Class == pcClass);
}
