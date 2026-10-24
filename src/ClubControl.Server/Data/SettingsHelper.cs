using System.Threading.Tasks;
using ClubControl.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Data;

public static class SettingsHelper
{
    public static async Task<Setting> GetOrCreateAsync(ClubDbContext db)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Id == 1);
        if (setting is null)
        {
            setting = new Setting { Id = 1 };
            db.Settings.Add(setting);
            await db.SaveChangesAsync();
        }

        return setting;
    }
}
