using System;
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
    private ResourceKind _kind = ResourceKind.Computer;
    private ReportsService.Report? _lastReport;

    public ReportsWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        ApplyLocalization();

        SetRange(DateTime.Today, DateTime.Today.AddDays(1));
        Loaded += async (_, _) => await RefreshAsync();
    }

    private void ApplyLocalization()
    {
        Title = Strings.Get("ReportsTitle");
        ComputersKindButton.Content = "🖥 " + Strings.ResourceKindName(ResourceKind.Computer);
        OtherKindButton.Content = "📦 " + Strings.ResourceKindName(ResourceKind.Other);
        DailyButton.Content = Strings.Get("PeriodDaily");
        WeeklyButton.Content = Strings.Get("PeriodWeekly");
        MonthlyButton.Content = Strings.Get("PeriodMonthly");
        FromLabelText.Text = Strings.Get("FromLabel");
        ToLabelText.Text = Strings.Get("ToLabel");
        RefreshButton.Content = Strings.Get("Refresh");
        ExportButton.Content = Strings.Get("Export");
        CloseButton.Content = Strings.Get("Close");

        TotalIncomeLabel.Text = Strings.Get("TotalIncome");
        TotalSessionsLabel.Text = Strings.Get("TotalSessions");
        TotalHoursLabel.Text = Strings.Get("TotalHours");
        ExtrasLabel.Text = Strings.Get("ReportItemsSold");

        ByPcTab.Header = Strings.Get("ByPc");
        ByTariffTab.Header = Strings.Get("ByTariff");
        ByTypeTab.Header = Strings.Get("BySessionType");

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

    private void SetRange(DateTime fromLocal, DateTime toLocalExclusive)
    {
        FromDatePicker.SelectedDate = fromLocal;
        ToDatePicker.SelectedDate = toLocalExclusive.AddDays(-1);
    }

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

    private async void OnDaily(object sender, RoutedEventArgs e)
    {
        SetRange(DateTime.Today, DateTime.Today.AddDays(1));
        await RefreshAsync();
    }

    private async void OnWeekly(object sender, RoutedEventArgs e)
    {
        var startOfWeek = DateTime.Today.AddDays(-6);
        SetRange(startOfWeek, DateTime.Today.AddDays(1));
        await RefreshAsync();
    }

    private async void OnMonthly(object sender, RoutedEventArgs e)
    {
        var startOfMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        SetRange(startOfMonth, DateTime.Today.AddDays(1));
        await RefreshAsync();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        var fromLocal = FromDatePicker.SelectedDate ?? DateTime.Today;
        var toLocalExclusive = (ToDatePicker.SelectedDate ?? DateTime.Today).AddDays(1);

        using var scope = _services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<ReportsService>();
        var report = await reports.BuildReportAsync(fromLocal.ToUniversalTime(), toLocalExclusive.ToUniversalTime(), _kind);
        _lastReport = report;

        TotalIncomeValue.Text = $"{report.Totals.TotalIncomeUzs:N0} UZS";
        TotalSessionsValue.Text = report.Totals.SessionCount.ToString();
        TotalHoursValue.Text = report.Totals.TotalHours.ToString("N1");
        ExtrasValue.Text = $"{report.Totals.ExtrasUzs:N0} UZS";

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

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"ClubControl-report-{DateTime.Today:yyyy-MM-dd}.csv"
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
