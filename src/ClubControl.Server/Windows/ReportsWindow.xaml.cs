using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

/// <summary>Daily/weekly/monthly reports (SPEC section 9).</summary>
public partial class ReportsWindow : Window
{
    private readonly IServiceProvider _services;

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
        DailyButton.Content = Strings.Get("PeriodDaily");
        WeeklyButton.Content = Strings.Get("PeriodWeekly");
        MonthlyButton.Content = Strings.Get("PeriodMonthly");
        FromLabelText.Text = Strings.Get("FromLabel");
        ToLabelText.Text = Strings.Get("ToLabel");
        RefreshButton.Content = Strings.Get("Refresh");
        CloseButton.Content = Strings.Get("Close");

        TotalIncomeLabel.Text = Strings.Get("TotalIncome");
        TotalSessionsLabel.Text = Strings.Get("TotalSessions");
        TotalHoursLabel.Text = Strings.Get("TotalHours");
        FreeMinutesLabel.Text = Strings.Get("FreeMinutes");

        ByPcTab.Header = Strings.Get("ByPc");
        ByClassTab.Header = Strings.Get("ByClass");
        ByTypeTab.Header = Strings.Get("BySessionType");

        ByPcNumberColumn.Header = Strings.Get("ColumnPcNumber");
        ByClassNameColumn.Header = Strings.Get("ColumnClass");
        ByTypeNameColumn.Header = Strings.Get("ColumnType");
        foreach (var (income, sessions, hours) in new[]
                 {
                     (ByPcIncomeColumn, ByPcSessionsColumn, ByPcHoursColumn),
                     (ByClassIncomeColumn, ByClassSessionsColumn, ByClassHoursColumn),
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
        var report = await reports.BuildReportAsync(fromLocal.ToUniversalTime(), toLocalExclusive.ToUniversalTime());

        TotalIncomeValue.Text = $"{report.Totals.TotalIncomeUzs:N0} UZS";
        TotalSessionsValue.Text = report.Totals.SessionCount.ToString();
        TotalHoursValue.Text = report.Totals.TotalHours.ToString("N1");
        FreeMinutesValue.Text = report.Totals.FreeMinutes.ToString("N0");

        ByPcGrid.ItemsSource = report.ByPc.Select(r => new
        {
            PcLabel = r.PcNumber.HasValue ? Strings.Format("PcNumberFormat", r.PcNumber.Value) : "—",
            IncomeDisplay = $"{r.IncomeUzs:N0} UZS",
            r.SessionCount,
            HoursDisplay = r.Hours.ToString("N1")
        }).ToList();

        ByClassGrid.ItemsSource = report.ByClass.Select(r => new
        {
            ClassLabel = r.Class.HasValue ? Strings.ClassName(r.Class.Value) : "—",
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

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
