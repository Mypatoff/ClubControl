using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace ClubControl.Server.Windows;

/// <summary>Daily/weekly/monthly reports (SPEC section 9), scoped to one resource kind at a time.</summary>
public partial class ReportsWindow : Window
{
    private readonly IServiceProvider _services;
    private enum Period { Shift, Day, Week, Month }

    private ResourceKind _kind = ResourceKind.Computer;
    private Period _period = Period.Day;
    private DateTime _day = DateTime.Today; // the day shown in Day mode; the arrows move it
    private List<ReportsService.ShiftInfo> _shifts = new(); // newest first
    private int _shiftIndex;                                // the shift shown in Shift mode; the arrows move it
    private ReportsService.Report? _lastReport;

    public ReportsWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        ApplyLocalization();

        Loaded += async (_, _) =>
        {
            // A club that works in shifts wants to see the current shift first, not a calendar day.
            using (var scope = _services.CreateScope())
                _shifts = await scope.ServiceProvider.GetRequiredService<ReportsService>().GetShiftsAsync();
            if (_shifts.Count > 0)
                _period = Period.Shift;

            await RefreshAsync();
        };
    }

    private void ApplyLocalization()
    {
        Title = Strings.Get("ReportsTitle");
        ComputersKindButton.Content = "🖥 " + Strings.ResourceKindName(ResourceKind.Computer);
        OtherKindButton.Content = "📦 " + Strings.ResourceKindName(ResourceKind.Other);
        ShiftButton.Content = Strings.Get("PeriodShift");
        DailyButton.Content = Strings.Get("PeriodDaily");
        WeeklyButton.Content = Strings.Get("PeriodWeekly");
        MonthlyButton.Content = Strings.Get("PeriodMonthly");
        ExportButton.Content = Strings.Get("Export");
        CloseButton.Content = Strings.Get("Close");

        TotalIncomeLabel.Text = Strings.Get("TotalIncome");
        TotalSessionsLabel.Text = Strings.Get("TotalSessions");
        TotalHoursLabel.Text = Strings.Get("TotalHours");
        ExtrasLabel.Text = Strings.Get("ReportItemsSold");

        SessionsTab.Header = Strings.Get("SessionsTab");
        ByPcTab.Header = Strings.Get("ByPc");
        ByTariffTab.Header = Strings.Get("ByTariff");
        ByTypeTab.Header = Strings.Get("BySessionType");

        SessionResourceColumn.Header = Strings.Get("ColumnPcNumber");
        SessionStartColumn.Header = Strings.Get("ColumnStart");
        SessionEndColumn.Header = Strings.Get("ColumnEnd");
        SessionDurationColumn.Header = Strings.Get("ColumnDuration");
        SessionTypeColumn.Header = Strings.Get("ColumnType");
        SessionTariffColumn.Header = Strings.Get("ColumnTariff");
        SessionIncomeColumn.Header = Strings.Get("ColumnIncome");
        SessionItemsColumn.Header = Strings.Get("ReportItemsSold");
        SessionNoteColumn.Header = Strings.Get("ColumnNote");

        ByPcNumberColumn.Header = Strings.Get("ColumnPcNumber");
        ByTariffNameColumn.Header = Strings.Get("ColumnTariff");
        ByTypeNameColumn.Header = Strings.Get("ColumnType");
        foreach (var (income, sessions, hours) in new[]
                 {
                     (ByPcIncomeColumn, ByPcSessionsColumn, ByPcHoursColumn),
                     (ByTariffIncomeColumn, ByTariffSessionsColumn, ByTariffHoursColumn),
                     (ByTypeIncomeColumn, ByTypeSessionsColumn, ByTypeHoursColumn)
                 })
        {
            income.Header = Strings.Get("ColumnIncome");
            sessions.Header = Strings.Get("TotalSessions");
            hours.Header = Strings.Get("ColumnHours");
        }
    }

    /// <summary>The first day and the day after the last day (local time) of what the buttons currently select.</summary>
    private (DateTime From, DateTime ToExclusive) CurrentRange() => _period switch
    {
        Period.Week => (DateTime.Today.AddDays(-6), DateTime.Today.AddDays(1)),
        Period.Month => (new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today.AddDays(1)),
        _ => (_day, _day.AddDays(1)),
    };

    // ← goes back in time: to the previous day, or to the shift before the one shown (shifts are listed newest first).
    private async void OnPrevious(object sender, RoutedEventArgs e)
    {
        if (_period == Period.Shift)
            _shiftIndex++;
        else
            _day = _day.AddDays(-1);
        await RefreshAsync();
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_period == Period.Shift)
            _shiftIndex--;
        else
            _day = _day.AddDays(1);
        await RefreshAsync();
    }

    /// <summary>Shows which button is on and what it covers; the arrows only make sense for a single shift or day.</summary>
    private void UpdatePeriodControls()
    {
        ShiftButton.FontWeight = _period == Period.Shift ? FontWeights.Bold : FontWeights.Normal;
        DailyButton.FontWeight = _period == Period.Day ? FontWeights.Bold : FontWeights.Normal;
        WeeklyButton.FontWeight = _period == Period.Week ? FontWeights.Bold : FontWeights.Normal;
        MonthlyButton.FontWeight = _period == Period.Month ? FontWeights.Bold : FontWeights.Normal;
        ComputersKindButton.FontWeight = _kind == ResourceKind.Computer ? FontWeights.Bold : FontWeights.Normal;
        OtherKindButton.FontWeight = _kind == ResourceKind.Other ? FontWeights.Bold : FontWeights.Normal;

        if (_period == Period.Shift)
        {
            PreviousButton.IsEnabled = _shiftIndex < _shifts.Count - 1;
            NextButton.IsEnabled = _shiftIndex > 0;
            PeriodText.Text = _shifts.Count == 0 ? Strings.Get("ShiftNone") : ShiftText(_shifts[_shiftIndex]);
            return;
        }

        PreviousButton.IsEnabled = NextButton.IsEnabled = _period == Period.Day;

        var (from, toExclusive) = CurrentRange();
        var last = toExclusive.AddDays(-1);
        PeriodText.Text = from == last ? from.ToString("dd.MM.yyyy") : $"{from:dd.MM.yyyy} – {last:dd.MM.yyyy}";
    }

    /// <summary>"Shift #12: 28.10 14:05 – 29.10 02:30" (or "open" while it has not been closed).</summary>
    private static string ShiftText(ReportsService.ShiftInfo shift) =>
        Strings.Format("ShiftLabel", shift.Id, shift.OpenedUtc.ToLocalTime().ToString("dd.MM HH:mm"),
            shift.ClosedUtc is { } closed ? closed.ToLocalTime().ToString("dd.MM HH:mm") : Strings.Get("ShiftStillOpen"));

    private async void OnComputersKind(object sender, RoutedEventArgs e)
    {
        _kind = ResourceKind.Computer;
        await RefreshAsync();
    }

    private async void OnOtherKind(object sender, RoutedEventArgs e)
    {
        _kind = ResourceKind.Other;
        await RefreshAsync();
    }

    // "Shift" always goes to the newest shift (the open one, if there is one); the arrows walk to older ones.
    private async void OnShift(object sender, RoutedEventArgs e)
    {
        _period = Period.Shift;
        _shiftIndex = 0;
        await RefreshAsync();
    }

    // "Day" always goes back to today, from where the arrows walk to other days.
    private async void OnDaily(object sender, RoutedEventArgs e)
    {
        _period = Period.Day;
        _day = DateTime.Today;
        await RefreshAsync();
    }

    // The last 7 days up to today.
    private async void OnWeekly(object sender, RoutedEventArgs e)
    {
        _period = Period.Week;
        await RefreshAsync();
    }

    // From the 1st of this month up to today.
    private async void OnMonthly(object sender, RoutedEventArgs e)
    {
        _period = Period.Month;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        using var scope = _services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<ReportsService>();

        // Re-read each time: a shift may have been opened or closed since this window opened.
        _shifts = await reports.GetShiftsAsync();
        _shiftIndex = Math.Clamp(_shiftIndex, 0, Math.Max(0, _shifts.Count - 1));
        UpdatePeriodControls();

        ReportsService.Report report;
        if (_period == Period.Shift)
        {
            // With no shifts at all the report is simply empty (no session has shift id 0).
            report = await reports.BuildShiftReportAsync(_shifts.Count == 0 ? 0 : _shifts[_shiftIndex].Id, _kind);
        }
        else
        {
            var (fromLocal, toLocalExclusive) = CurrentRange();
            report = await reports.BuildReportAsync(fromLocal.ToUniversalTime(), toLocalExclusive.ToUniversalTime(), _kind);
        }

        _lastReport = report;

        TotalIncomeValue.Text = $"{report.Totals.TotalIncomeUzs:N0} UZS";
        TotalSessionsValue.Text = report.Totals.SessionCount.ToString();
        TotalHoursValue.Text = report.Totals.TotalHours.ToString("N1");
        ExtrasValue.Text = $"{report.Totals.ExtrasUzs:N0} UZS";

        SessionsGrid.ItemsSource = report.Sessions.Select(s => new
        {
            ResourceLabel = s.PcNumber.HasValue ? Strings.ResourceLabel(_kind, s.PcName, s.PcNumber) : "—",
            StartDisplay = s.StartUtc.ToLocalTime().ToString(TimeFormat),
            EndDisplay = EndText(s),
            DurationDisplay = TimeText.Duration((s.EndUtc ?? DateTime.UtcNow) - s.StartUtc),
            TypeLabel = Strings.SessionTypeName(s.Type, s.IsFree),
            TariffLabel = string.IsNullOrEmpty(s.TariffName) ? "—" : s.TariffName,
            IncomeDisplay = $"{s.AmountUzs:N0} UZS",
            ItemsDisplay = s.ExtrasUzs > 0 ? $"{s.ExtrasUzs:N0} UZS" : "",
            s.Note
        }).ToList();

        ByPcGrid.ItemsSource = report.ByPc.Select(r => new
        {
            PcLabel = r.PcNumber.HasValue ? Strings.ResourceLabel(_kind, r.PcName, r.PcNumber) : "—",
            IncomeDisplay = $"{r.IncomeUzs:N0} UZS",
            r.SessionCount,
            HoursDisplay = r.Hours.ToString("N1")
        }).ToList();

        ByTariffGrid.ItemsSource = report.ByTariff.Select(r => new
        {
            TariffLabel = string.IsNullOrEmpty(r.TariffName) ? "—" : r.TariffName,
            IncomeDisplay = $"{r.IncomeUzs:N0} UZS",
            r.SessionCount,
            HoursDisplay = r.Hours.ToString("N1")
        }).ToList();

        ByTypeGrid.ItemsSource = report.ByType.Select(r => new
        {
            TypeLabel = Strings.SessionTypeName(r.Type, r.Type == SessionType.Free),
            IncomeDisplay = $"{r.IncomeUzs:N0} UZS",
            r.SessionCount,
            HoursDisplay = r.Hours.ToString("N1")
        }).ToList();
    }

    private const string TimeFormat = "dd.MM.yyyy HH:mm";

    /// <summary>The end time, or "running" for a session that has not ended yet.</summary>
    private static string EndText(ReportsService.SessionLine s) =>
        s.EndUtc is { } end ? end.ToLocalTime().ToString(TimeFormat) : Strings.Get("ReportStillRunning");

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = _period == Period.Shift && _shifts.Count > 0
                ? $"ClubControl-report-shift-{_shifts[_shiftIndex].Id}.csv"
                : $"ClubControl-report-{DateTime.Today:yyyy-MM-dd}.csv"
        };
        if (dialog.ShowDialog() != true)
            return;

        File.WriteAllText(dialog.FileName, BuildCsv(_lastReport), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private string BuildCsv(ReportsService.Report report)
    {
        var sb = new StringBuilder();
        void WriteRow(params object[] values) => sb.AppendLine(string.Join(",", values.Select(EscapeCsv)));

        WriteRow(Strings.Get("TotalIncome"), Strings.Get("TotalSessions"), Strings.Get("TotalHours"), Strings.Get("ReportItemsSold"));
        WriteRow(report.Totals.TotalIncomeUzs, report.Totals.SessionCount,
            report.Totals.TotalHours.ToString("F1", CultureInfo.InvariantCulture),
            report.Totals.ExtrasUzs);
        sb.AppendLine();

        WriteRow(Strings.Get("SessionsTab"));
        WriteRow(Strings.Get("ColumnPcNumber"), Strings.Get("ColumnStart"), Strings.Get("ColumnEnd"), Strings.Get("ColumnDuration"),
            Strings.Get("ColumnType"), Strings.Get("ColumnTariff"), Strings.Get("ColumnIncome"), Strings.Get("ReportItemsSold"), Strings.Get("ColumnNote"));
        foreach (var s in report.Sessions)
            WriteRow(s.PcNumber.HasValue ? Strings.ResourceLabel(_kind, s.PcName, s.PcNumber) : "-",
                s.StartUtc.ToLocalTime().ToString(TimeFormat), EndText(s), TimeText.Duration((s.EndUtc ?? DateTime.UtcNow) - s.StartUtc),
                Strings.SessionTypeName(s.Type, s.IsFree), string.IsNullOrEmpty(s.TariffName) ? "-" : s.TariffName,
                s.AmountUzs, s.ExtrasUzs, s.Note);
        sb.AppendLine();

        WriteRow(Strings.Get("ByPc"));
        WriteRow(Strings.Get("ColumnPcNumber"), Strings.Get("ColumnIncome"), Strings.Get("TotalSessions"), Strings.Get("ColumnHours"));
        foreach (var r in report.ByPc)
            WriteRow(r.PcName != "" ? r.PcName : r.PcNumber?.ToString() ?? "-", r.IncomeUzs, r.SessionCount, r.Hours.ToString("F1", CultureInfo.InvariantCulture));
        sb.AppendLine();

        WriteRow(Strings.Get("ByTariff"));
        WriteRow(Strings.Get("ColumnTariff"), Strings.Get("ColumnIncome"), Strings.Get("TotalSessions"), Strings.Get("ColumnHours"));
        foreach (var r in report.ByTariff)
            WriteRow(string.IsNullOrEmpty(r.TariffName) ? "-" : r.TariffName, r.IncomeUzs, r.SessionCount, r.Hours.ToString("F1", CultureInfo.InvariantCulture));
        sb.AppendLine();

        WriteRow(Strings.Get("BySessionType"));
        WriteRow(Strings.Get("ColumnType"), Strings.Get("ColumnIncome"), Strings.Get("TotalSessions"), Strings.Get("ColumnHours"));
        foreach (var r in report.ByType)
            WriteRow(Strings.SessionTypeName(r.Type, r.Type == SessionType.Free), r.IncomeUzs, r.SessionCount, r.Hours.ToString("F1", CultureInfo.InvariantCulture));

        return sb.ToString();
    }

    private static string EscapeCsv(object value)
    {
        var text = value.ToString() ?? "";
        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
