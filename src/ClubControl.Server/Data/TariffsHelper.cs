using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Data;

public static class TariffsHelper
{
    /// <summary>Default tariffs so a fresh install has something billable before the admin edits them.</summary>
    public static async Task EnsureDefaultsAsync(ClubDbContext db)
    {
        if (!await db.Tariffs.AnyAsync())
        {
            db.Tariffs.AddRange(
                new Tariff { Name = "Standard", Kind = ResourceKind.Computer, PricePerHourUzs = 10_000, MinPaymentUzs = 2_000, MaxPaymentUzs = 50_000 },
                new Tariff { Name = "VIP", Kind = ResourceKind.Computer, PricePerHourUzs = 15_000, MinPaymentUzs = 3_000, MaxPaymentUzs = 75_000 });
            await db.SaveChangesAsync();
        }
    }

    public static Task<List<Tariff>> GetAllAsync(ClubDbContext db) =>
        db.Tariffs.OrderBy(t => t.Kind).ThenBy(t => t.Name).ToListAsync();

    public static Task<List<Tariff>> GetByKindAsync(ClubDbContext db, ResourceKind kind) =>
        db.Tariffs.Where(t => t.Kind == kind).OrderBy(t => t.Name).ToListAsync();

    public static Task<Tariff?> GetAsync(ClubDbContext db, int tariffId) =>
        db.Tariffs.FirstOrDefaultAsync(t => t.Id == tariffId);
}
