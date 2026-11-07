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

    /// <summary>How long the bar's shift history is shown: the last month.</summary>
    public const int BarHistoryDays = 30;

    /// <summary>What one bar product sold during a shift: units and money (sales on credit included).</summary>
    public record BarProductRow(string ProductName, int Units, long AmountUzs);

    /// <summary>
    /// The bar for one working shift (smena). Income is the money that came in during the shift: counter sales,
    /// sales added to a PC's bill (also inside the sessions report's "Items sold"), and debts paid in the shift.
    /// CreditGivenUzs is what was handed out on credit during the shift - not income until it is paid.
    /// Cancelled sales count for nothing.
    /// </summary>
    public record BarShiftReport(int ShiftId, DateTime OpenedUtc, DateTime? ClosedUtc, long CounterPaidUzs, long SessionItemsUzs,
        long DebtsPaidUzs, long CreditGivenUzs, int UnitsSold, List<BarProductRow> ByProduct)
    {
        public long TotalIncomeUzs => CounterPaidUzs + SessionItemsUzs + DebtsPaidUzs;
    }

    /// <summary>
    /// The bar report of every shift opened in the last <see cref="BarHistoryDays"/> days (and the open one), newest
    /// first. Nothing is deleted: older sales stay in the database, they are just not listed here.
    /// </summary>
    public async Task<List<BarShiftReport>> GetBarShiftHistoryAsync(DateTime nowUtc)
    {
        var since = nowUtc.AddDays(-BarHistoryDays);
        var shifts = await _db.Shifts.AsNoTracking()
            .Where(s => s.OpenedUtc >= since || s.ClosedUtc == null)
            .OrderByDescending(s => s.Id)
            .ToListAsync();
        if (shifts.Count == 0)
            return new List<BarShiftReport>();

        var ids = shifts.Select(s => (int?)s.Id).ToList();
        var sales = await _db.BarSales.AsNoTracking()
            .Where(s => s.VoidedUtc == null && (ids.Contains(s.ShiftId) || ids.Contains(s.PaidShiftId)))
            .ToListAsync();

        return shifts.Select(shift =>
        {
            var given = sales.Where(s => s.ShiftId == shift.Id).ToList();
            var byProduct = given
                .GroupBy(s => s.ProductName)
                .Select(g => new BarProductRow(g.Key, g.Sum(s => s.Quantity), g.Sum(s => (long)s.TotalUzs)))
                .OrderByDescending(r => r.Units).ThenBy(r => r.ProductName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new BarShiftReport(
                shift.Id, shift.OpenedUtc, shift.ClosedUtc,
                CounterPaidUzs: given.Where(s => !s.IsUnpaid && s.SessionId == null).Sum(s => (long)s.TotalUzs),
                SessionItemsUzs: given.Where(s => !s.IsUnpaid && s.SessionId != null).Sum(s => (long)s.TotalUzs),
                DebtsPaidUzs: sales.Where(s => s.IsUnpaid && s.PaidShiftId == shift.Id).Sum(s => (long)s.TotalUzs),
                CreditGivenUzs: given.Where(s => s.IsUnpaid).Sum(s => (long)s.TotalUzs),
                UnitsSold: given.Sum(s => s.Quantity),
                ByProduct: byProduct);
        }).ToList();
    }

    /// <summary>Everything still owed at the bar, whenever it was sold.</summary>
    public async Task<long> GetUnpaidNowAsync() =>
        await _db.BarSales.AsNoTracking()
            .Where(s => s.IsUnpaid && s.PaidUtc == null && s.VoidedUtc == null)
            .SumAsync(s => (long?)s.TotalUzs) ?? 0;

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

    /// <summary>The few columns of a session a report needs (so no whole entities are loaded and tracked).</summary>
    private record SessionRow(DateTime StartUtc, DateTime? ActualEndUtc, SessionType Type, bool IsFree, int AmountUzs, string? Note,
        int? PcNumber, string PcName, int? PcTariffId, int ExtrasUzs);

    private async Task<Report> BuildAsync(Expression<Func<Session, bool>> filter, ResourceKind kind)
    {
        // Read-only and only the needed columns: a Month report is thousands of sessions, and this used to load them
        // all (with their PC and every item) as tracked entities.
        var sessions = await _db.Sessions.AsNoTracking()
            .Where(filter)
            .Where(s => s.Pc!.Kind == kind)
            .Select(s => new SessionRow(s.StartUtc, s.ActualEndUtc, s.Type, s.IsFree, s.AmountUzs, s.Note,
                s.Pc!.Number, s.Pc.Name, s.Pc.TariffId, s.Extras.Sum(x => x.AmountUzs)))
            .ToListAsync();

        var tariffNames = await _db.Tariffs.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);

        var now = DateTime.UtcNow;
        double HoursOf(SessionRow s) => ((s.ActualEndUtc ?? now) - s.StartUtc).TotalHours;
        string TariffNameOf(SessionRow s) => s.PcTariffId is int id ? tariffNames.GetValueOrDefault(id, "") : "";

        var totals = new Totals(
            TotalIncomeUzs: sessions.Sum(s => s.AmountUzs),
            SessionCount: sessions.Count,
            TotalHours: sessions.Sum(HoursOf),
            ExtrasUzs: sessions.Sum(s => s.ExtrasUzs));

        var byPc = sessions
            .GroupBy(s => (Number: s.PcNumber, Name: s.PcName))
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
            .Select(s => new SessionLine(s.PcNumber, s.PcName, s.StartUtc, s.ActualEndUtc, s.Type, s.IsFree,
                TariffNameOf(s), s.AmountUzs, s.ExtrasUzs, s.Note ?? ""))
            .ToList();

        return new Report(totals, byPc, byTariff, byType, bySession);
    }
}
