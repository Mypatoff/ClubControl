using System;
using System.Linq;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Services;

/// <summary>
/// Two things the admin does when a computer changes: "Delete" a PC for good, and "Windows was reinstalled".
/// Neither changes the database layout - they only change values of the existing <see cref="Pc"/> columns.
/// </summary>
public static class PcRetirement
{
    public enum Outcome { Deleted, Retired }

    /// <summary>
    /// Delete button. A PC or resource that never had a session is removed completely. One with history is
    /// kept, so reports still show its income, but it is renamed ("PC 6" becomes "*Old PC 6"), loses its number
    /// (so a new PC 6 can be added) and its machine id and token (so nothing can connect as it any more), and
    /// is disabled (so it disappears from the main panel). Refused while a session is running on it.
    /// </summary>
    public static async Task<Outcome> RemoveAsync(ClubDbContext db, Pc pc)
    {
        if (pc.IsRetired)
            throw new InvalidOperationException("This one is already deleted (kept only for the reports).");

        if (await db.Sessions.AnyAsync(s => s.PcId == pc.Id && s.Status == SessionStatus.Active))
            throw new InvalidOperationException("A session is still running on it. Stop the session first.");

        if (!await db.Sessions.AnyAsync(s => s.PcId == pc.Id))
        {
            db.Pcs.Remove(pc);
            await db.SaveChangesAsync();
            return Outcome.Deleted;
        }

        var label = RetiredName(pc);
        pc.Name = label;
        pc.Number = null;
        pc.Enabled = false;
        pc.IsOnline = false;
        pc.State = PcState.Free;
        pc.MachineGuid = Guid.NewGuid();
        pc.AuthTokenHash = null;

        db.EventLogEntries.Add(new EventLogEntry
        {
            Type = EventType.Warning,
            PcId = pc.Id,
            Message = $"Deleted ({label}): its history stays in the reports"
        });
        await db.SaveChangesAsync();
        return Outcome.Retired;
    }

    /// <summary>"PC 6" -> "*Old PC 6"; a named resource ("Table 3") -> "*Old Table 3".</summary>
    public static string RetiredName(Pc pc) =>
        Pc.RetiredPrefix + (pc.Kind == ResourceKind.Other && pc.Name != "" ? pc.Name : $"PC {pc.Number?.ToString() ?? "-"}");

    /// <summary>
    /// Can <paramref name="newPc"/> (a machine the server has not seen before) be the same computer as
    /// <paramref name="oldPc"/> after its Windows was reinstalled? The old one must be a networked computer that
    /// is not connected right now - a connected one is clearly a different machine.
    /// </summary>
    public static bool CanTakeOver(Pc oldPc, Pc newPc) =>
        oldPc.Id != newPc.Id
        && oldPc.Kind == ResourceKind.Computer && !oldPc.IsManual && !oldPc.IsRetired && !oldPc.IsOnline
        && !newPc.IsManual && !newPc.IsRetired;

    /// <summary>
    /// Windows reinstall: a reinstalled computer gets a new machine id, so its client shows up as a "New PC".
    /// This gives that identity to the old PC row, so the number, tariff, sessions and reports carry on, and
    /// removes the empty "New PC" row. The old auth token is cleared (the new client never had it); the next Save in
    /// Settings, while the client is connected, gives the new client a fresh one.
    /// </summary>
    public static async Task TakeOverAsync(ClubDbContext db, Pc oldPc, Pc newPc)
    {
        if (!CanTakeOver(oldPc, newPc))
            throw new InvalidOperationException("These two cannot be the same computer.");
        // Only a machine that is still a "New PC" in the database (the admin may have typed a number into the grid, not saved yet).
        if (db.Entry(newPc).Property(p => p.Number).OriginalValue is not null || await db.Sessions.AnyAsync(s => s.PcId == newPc.Id))
            throw new InvalidOperationException("The new machine is already set up.");

        await using var transaction = await db.Database.BeginTransactionAsync();

        var guid = newPc.MachineGuid;
        var ip = newPc.IpAddress;
        var mac = newPc.MacAddress;
        var host = newPc.Hostname;
        var online = newPc.IsOnline;
        var heartbeat = newPc.LastHeartbeatUtc;

        // The machine id is unique, so the empty row has to go before the old row can take its id.
        db.Pcs.Remove(newPc);
        await db.SaveChangesAsync();

        oldPc.MachineGuid = guid;
        oldPc.IpAddress = ip;
        oldPc.MacAddress = mac;
        oldPc.Hostname = host;
        oldPc.IsOnline = online;
        oldPc.LastHeartbeatUtc = heartbeat;
        oldPc.AuthTokenHash = null;

        db.EventLogEntries.Add(new EventLogEntry
        {
            Type = EventType.PcConnected,
            PcId = oldPc.Id,
            Message = $"PC #{oldPc.Number}: a new machine ({host}, {ip}) took over this PC (Windows reinstalled). Number, tariff and history are kept"
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
