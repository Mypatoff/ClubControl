using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Services;

/// <summary>
/// Reports (SPEC section 9, extended for non-PC resources): totals and breakdowns for sessions
/// that started within a date range, scoped to one resource kind (Computer or Other) so the
/// two never mix in one report. Income is the session's stored <see cref="Session.AmountUzs"/> -
/// for a postpay/free session still running, that is 0 until it ends, same as everywhere else.
/// </summary>
public class ReportsService
{
    private readonly ClubDbContext _db;

    public ReportsService(ClubDbContext db)
    {
        _db = db;
    }

    public record Totals(int TotalIncomeUzs, int SessionCount, double TotalHours, int ExtrasUzs);
    public record PcRow(int? PcNumber, string PcName, int IncomeUzs, int SessionCount, double Hours);
    public record TariffRow(string TariffName, int IncomeUzs, int SessionCount, double Hours);
    public record TypeRow(SessionType Type, int IncomeUzs, int SessionCount, double Hours);
    /// <summary>One session: which PC / other resource, when it started and ended (null while still running), what it cost.</summary>
    public record SessionLine(int? PcNumber, string PcName, DateTime StartUtc, DateTime? EndUtc, SessionType Type, bool IsFree,
        string TariffName, int AmountUzs, int ExtrasUzs, string Note);
    public record Report(Totals Totals, List<PcRow> ByPc, List<TariffRow> ByTariff, List<TypeRow> ByType, List<SessionLine> Sessions);

    /// <summary>A shift for the report's shift picker; ClosedUtc is null while it is still open.</summary>
    public record ShiftInfo(int Id, DateTime OpenedUtc, DateTime? ClosedUtc);

    /// <summary>Every shift, newest first.</summary>
    public async Task<List<ShiftInfo>> GetShiftsAsync() =>
        await _db.Shifts.AsNoTracking()
            .OrderByDescending(s => s.Id)
            .Select(s => new ShiftInfo(s.Id, s.OpenedUtc, s.ClosedUtc))
            .ToListAsync();

    public async Task<Report> BuildReportAsync(DateTime startUtc, DateTime endUtc, ResourceKind kind) =>
        await BuildAsync(s => s.StartUtc >= startUtc && s.StartUtc < endUtc, kind);

    /// <summary>The same report for one working shift: every session that started while it was open, even past midnight.</summary>
    public async Task<Report> BuildShiftReportAsync(int shiftId, ResourceKind kind) =>
        await BuildAsync(s => s.ShiftId == shiftId, kind);

    private async Task<Report> BuildAsync(Expression<Func<Session, bool>> filter, ResourceKind kind)
    {
        var sessions = await _db.Sessions
            .Include(s => s.Pc)
            .Include(s => s.Extras)
            .Where(filter)
            .Where(s => s.Pc!.Kind == kind)
            .ToListAsync();

        var tariffNames = await _db.Tariffs.ToDictionaryAsync(t => t.Id, t => t.Name);

        var now = DateTime.UtcNow;
        double HoursOf(Session s) => ((s.ActualEndUtc ?? now) - s.StartUtc).TotalHours;
        string TariffNameOf(Session s) => s.Pc?.TariffId is int id ? tariffNames.GetValueOrDefault(id, "") : "";

        var totals = new Totals(
            TotalIncomeUzs: sessions.Sum(s => s.AmountUzs),
            SessionCount: sessions.Count,
            TotalHours: sessions.Sum(HoursOf),
            ExtrasUzs: sessions.Sum(s => s.Extras.Sum(x => x.AmountUzs)));

        var byPc = sessions
            .GroupBy(s => (Number: s.Pc?.Number, Name: s.Pc?.Name ?? ""))
            .Select(g => new PcRow(g.Key.Number, g.Key.Name, g.Sum(s => s.AmountUzs), g.Count(), g.Sum(HoursOf)))
            .OrderBy(r => r.PcNumber)
            .ToList();

        var byTariff = sessions
            .GroupBy(TariffNameOf)
            .Select(g => new TariffRow(g.Key, g.Sum(s => s.AmountUzs), g.Count(), g.Sum(HoursOf)))
            .OrderBy(r => r.TariffName)
            .ToList();

        var byType = sessions
            .GroupBy(s => s.Type)
            .Select(g => new TypeRow(g.Key, g.Sum(s => s.AmountUzs), g.Count(), g.Sum(HoursOf)))
            .OrderBy(r => r.Type)
            .ToList();

        // Newest first, so the admin sees the latest sessions at the top.
        var bySession = sessions
            .OrderByDescending(s => s.StartUtc)
            .Select(s => new SessionLine(s.Pc?.Number, s.Pc?.Name ?? "", s.StartUtc, s.ActualEndUtc, s.Type, s.IsFree,
                TariffNameOf(s), s.AmountUzs, s.Extras.Sum(x => x.AmountUzs), s.Note ?? ""))
            .ToList();

        return new Report(totals, byPc, byTariff, byType, bySession);
    }
}
