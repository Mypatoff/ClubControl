using System;
using System.Linq;
using System.Threading.Tasks;
using ClubControl.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Data;

/// <summary>Settings is meant to be a strict single-row table (SPEC section 8): row Id 1 only.</summary>
public static class SettingsHelper
{
    public const int SettingsRowId = 1;

    public static async Task<Setting> GetOrCreateAsync(ClubDbContext db)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Id == SettingsRowId);
        if (setting is not null)
        {
            await RemoveStrayRowsAsync(db);
            return setting;
        }

        setting = new Setting { Id = SettingsRowId };
        db.Settings.Add(setting);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another concurrent call (a different DbContext/scope) already inserted row 1 between
            // our read and our write - that is the real, valid row, so forget the one we just tried
            // to add and read it back instead of propagating a crash from a benign race.
            db.Entry(setting).State = EntityState.Detached;
            setting = await db.Settings.FirstOrDefaultAsync(s => s.Id == SettingsRowId)
                ?? throw new InvalidOperationException("Settings row could not be created or read back.");
        }

        await RemoveStrayRowsAsync(db);
        return setting;
    }

    /// <summary>
    /// Settings must stay a true singleton. Any row other than Id 1 (e.g. left over from manual DB
    /// edits or a bad migration) is never read by the app, but leaving it in place risks someone
    /// editing the wrong row later and being confused when nothing changes - so clean it up here,
    /// the one place every caller already goes through.
    /// </summary>
    private static async Task RemoveStrayRowsAsync(ClubDbContext db)
    {
        var strayRows = await db.Settings.Where(s => s.Id != SettingsRowId).ToListAsync();
        if (strayRows.Count == 0)
            return;

        db.Settings.RemoveRange(strayRows);
        await db.SaveChangesAsync();
    }
}
