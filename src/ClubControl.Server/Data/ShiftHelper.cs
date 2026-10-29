using System;
using System.Linq;
using System.Threading.Tasks;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Data;

/// <summary>
/// Opening and closing the working shift. Only one shift is open at a time. A session started while no
/// shift is open opens one by itself, so a forgotten "Open shift" never leaves sessions out of the reports.
/// A shift cannot be closed while any session is still running (including one waiting to be paid).
/// </summary>
public static class ShiftHelper
{
    public static Task<Shift?> GetOpenAsync(ClubDbContext db) =>
        db.Shifts.Where(s => s.ClosedUtc == null).OrderByDescending(s => s.Id).FirstOrDefaultAsync();

    public static Task<int> CountRunningSessionsAsync(ClubDbContext db) =>
        db.Sessions.CountAsync(s => s.Status == SessionStatus.Active);

    public static async Task<Shift> OpenAsync(ClubDbContext db, bool automatic = false)
    {
        if (await GetOpenAsync(db) is not null)
            throw new InvalidOperationException("A shift is already open.");

        var shift = new Shift();
        db.Shifts.Add(shift);
        db.EventLogEntries.Add(new EventLogEntry
        {
            Type = EventType.ShiftOpened,
            Message = automatic ? "Shift opened automatically (a session was started)" : "Shift opened"
        });
        await db.SaveChangesAsync(); // the shift and its event log entry are saved together
        return shift;
    }

    /// <summary>The open shift, or a new one if none is open - what every newly started session is attached to.</summary>
    public static async Task<Shift> GetOrOpenAsync(ClubDbContext db) =>
        await GetOpenAsync(db) ?? await OpenAsync(db, automatic: true);

    public static async Task<Shift> CloseAsync(ClubDbContext db)
    {
        var shift = await GetOpenAsync(db)
            ?? throw new InvalidOperationException("No shift is open.");

        var running = await CountRunningSessionsAsync(db);
        if (running > 0)
            throw new InvalidOperationException($"{running} session(s) are still running; stop them before closing the shift.");

        shift.ClosedUtc = DateTime.UtcNow;
        db.EventLogEntries.Add(new EventLogEntry { Type = EventType.ShiftClosed, Message = "Shift closed" });
        await db.SaveChangesAsync();
        return shift;
    }
}
