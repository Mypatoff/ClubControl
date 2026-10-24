using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Services;

/// <summary>
/// Reports (SPEC section 9): totals and breakdowns for sessions that started within a date range.
/// Income is the session's stored <see cref="Session.AmountUzs"/> - for a postpay/free session
/// still running, that is 0 until it ends, same as everywhere else in the app.
/// </summary>
public class ReportsService
{
    private readonly ClubDbContext _db;

    public ReportsService(ClubDbContext db)
    {
        _db = db;
    }

    public record Totals(int TotalIncomeUzs, int SessionCount, double TotalHours, double FreeMinutes);
    public record PcRow(int? PcNumber, int IncomeUzs, int SessionCount, double Hours);
    public record ClassRow(PcClass? Class, int IncomeUzs, int SessionCount, double Hours);
    public record TypeRow(SessionType Type, int IncomeUzs, int SessionCount, double Hours);
    public record Report(Totals Totals, List<PcRow> ByPc, List<ClassRow> ByClass, List<TypeRow> ByType);

    public async Task<Report> BuildReportAsync(DateTime startUtc, DateTime endUtc)
    {
        var sessions = await _db.Sessions
            .Include(s => s.Pc)
            .Where(s => s.StartUtc >= startUtc && s.StartUtc < endUtc)
            .ToListAsync();

        var now = DateTime.UtcNow;
        double HoursOf(Session s) => ((s.ActualEndUtc ?? now) - s.StartUtc).TotalHours;

        var totals = new Totals(
            TotalIncomeUzs: sessions.Sum(s => s.AmountUzs),
            SessionCount: sessions.Count,
            TotalHours: sessions.Sum(HoursOf),
            FreeMinutes: sessions.Where(s => s.IsFree).Sum(s => HoursOf(s) * 60));

        var byPc = sessions
            .GroupBy(s => s.Pc?.Number)
            .Select(g => new PcRow(g.Key, g.Sum(s => s.AmountUzs), g.Count(), g.Sum(HoursOf)))
            .OrderBy(r => r.PcNumber)
            .ToList();

        var byClass = sessions
            .GroupBy(s => s.Pc?.Class)
            .Select(g => new ClassRow(g.Key, g.Sum(s => s.AmountUzs), g.Count(), g.Sum(HoursOf)))
            .OrderBy(r => r.Class)
            .ToList();

        var byType = sessions
            .GroupBy(s => s.Type)
            .Select(g => new TypeRow(g.Key, g.Sum(s => s.AmountUzs), g.Count(), g.Sum(HoursOf)))
            .OrderBy(r => r.Type)
            .ToList();

        return new Report(totals, byPc, byClass, byType);
    }
}
