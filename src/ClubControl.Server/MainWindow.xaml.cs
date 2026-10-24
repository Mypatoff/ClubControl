using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Server.Windows;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;

    // Set once the host has started (see App.OnStartup) - MainWindow is only ever created after that.
    private IServiceProvider Services => ((App)Application.Current).Services!;

    public MainWindow()
    {
        InitializeComponent();
        ApplyLocalization();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
    }

    private void ApplyLocalization()
    {
        StartButton.Content = Strings.Get("ToolbarStart");
        ExtendButton.Content = Strings.Get("ToolbarExtend");
        StopButton.Content = Strings.Get("ToolbarStop");
        MoveButton.Content = Strings.Get("ToolbarMove");
        TurnOnButton.Content = Strings.Get("ToolbarTurnOn");
        RestartButton.Content = Strings.Get("ToolbarRestart");
        ShutDownButton.Content = Strings.Get("ToolbarShutDown");
        SettingsButton.Content = Strings.Get("ToolbarSettings");
        ReportsButton.Content = Strings.Get("ToolbarReports");

        EventLogHeaderText.Text = Strings.Get("EventLogHeader");

        PcNumberColumn.Header = Strings.Get("ColumnPcNumber");
        ClassColumn.Header = Strings.Get("ColumnClass");
        StateColumn.Header = Strings.Get("ColumnState");
        SessionColumn.Header = Strings.Get("ColumnSession");
        AmountColumn.Header = Strings.Get("ColumnAmount");
        StartColumn.Header = Strings.Get("ColumnStart");
        EndColumn.Header = Strings.Get("ColumnEnd");
        RemainingColumn.Header = Strings.Get("ColumnRemaining");
    }

    // The window only ever hides - the tray's "Exit" (password-protected) is the only way to really quit (SPEC 1).
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private async Task RefreshAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        var pcs = await db.Pcs.AsNoTracking().OrderBy(p => p.Number).ToListAsync();
        var snapshots = await sessionService.GetActiveSessionSnapshotsAsync();

        var selectedIds = PcGrid.SelectedItems.Cast<PcRow>().Select(p => p.Id).ToHashSet();
        var rows = pcs.Select(pc => BuildRow(pc, snapshots.GetValueOrDefault(pc.Id))).ToList();
        PcGrid.ItemsSource = rows;
        foreach (var row in rows.Where(r => selectedIds.Contains(r.Id)))
            PcGrid.SelectedItems.Add(row);

        var events = await db.EventLogEntries.AsNoTracking()
            .OrderByDescending(e => e.TimestampUtc)
            .Take(200)
            .ToListAsync();
        EventLogList.ItemsSource = events.Select(FormatEvent).ToList();

        StatusText.Text = Strings.Format("StatusListening", NetworkConstants.SignalRPort, NetworkConstants.DiscoveryPort,
            pcs.Count(p => p.IsOnline), pcs.Count);
    }

    private static PcRow BuildRow(Pc pc, SessionService.ActiveSessionSnapshot? snap)
    {
        var amount = "";
        var start = "";
        var end = "";
        var remaining = "";
        var sessionType = "";

        if (snap is not null)
        {
            sessionType = Strings.SessionTypeName(snap.Type, snap.IsFree);
            amount = $"{snap.AmountOrCostUzs:N0} UZS";
            start = snap.StartUtc.ToLocalTime().ToString("HH:mm:ss");

            if (snap.PlannedEndUtc.HasValue)
                end = snap.PlannedEndUtc.Value.ToLocalTime().ToString("HH:mm:ss");

            if (snap.Type == SessionType.Prepay)
                remaining = TimeSpan.FromSeconds(Math.Max(0, snap.RemainingSeconds)).ToString(@"mm\:ss");
        }

        return new PcRow
        {
            Id = pc.Id,
            MachineGuid = pc.MachineGuid,
            IsOnline = pc.IsOnline,
            Enabled = pc.Enabled,
            Number = pc.Number,
            Class = pc.Class,
            State = pc.State,
            ClassDisplay = pc.Class.HasValue ? Strings.ClassName(pc.Class.Value) : "",
            StateDisplay = Strings.PcStateName(pc.State, pc.IsConfigured),
            SessionTypeDisplay = sessionType,
            AmountDisplay = amount,
            StartDisplay = start,
            EndDisplay = end,
            RemainingDisplay = remaining
        };
    }

    private static string FormatEvent(EventLogEntry e) =>
        $"{e.TimestampUtc.ToLocalTime():HH:mm:ss} [{e.Type}] {e.Message}";

    // ---- toolbar ----

    private PcRow? GetSingleSelectedPc()
    {
        var selected = PcGrid.SelectedItems.Cast<PcRow>().ToList();
        if (selected.Count == 1)
            return selected[0];

        MessageBox.Show(Strings.Get("SelectExactlyOnePc"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        var pc = GetSingleSelectedPc();
        if (pc is null)
            return;

        if (!pc.Number.HasValue || !pc.Class.HasValue)
        {
            MessageBox.Show(Strings.Get("AssignNumberAndClassFirst"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (pc.State is PcState.PrepaySession or PcState.PostpaySession or PcState.LimitReached)
        {
            MessageBox.Show(Strings.Get("PcAlreadyHasSession"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new StartSessionWindow(Services, pc) { Owner = this };
        if (window.ShowDialog() == true)
            await RefreshAsync();
    }

    private async void OnExtend(object sender, RoutedEventArgs e)
    {
        var pc = GetSingleSelectedPc();
        if (pc is null)
            return;

        if (pc.State != PcState.PrepaySession)
        {
            MessageBox.Show(Strings.Get("ExtendOnlyActivePrepay"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new ExtendSessionWindow(Services, pc) { Owner = this };
        if (window.ShowDialog() == true)
            await RefreshAsync();
    }

    private async void OnMove(object sender, RoutedEventArgs e)
    {
        var pc = GetSingleSelectedPc();
        if (pc is null)
            return;

        if (pc.State is not (PcState.PrepaySession or PcState.PostpaySession))
        {
            MessageBox.Show(Strings.Get("MoveOnlyActiveSession"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new MoveSessionWindow(Services, pc) { Owner = this };
        if (window.ShowDialog() == true)
            await RefreshAsync();
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        var pc = GetSingleSelectedPc();
        if (pc is null)
            return;

        if (pc.State is not (PcState.PrepaySession or PcState.PostpaySession or PcState.LimitReached))
        {
            MessageBox.Show(Strings.Get("PcHasNoActiveSession"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (pc.State == PcState.PrepaySession)
        {
            if (MessageBox.Show(Strings.Get("StopPrepayConfirm"), Strings.Get("ConfirmTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            using var scope = Services.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
            try
            {
                await sessionService.EndSessionEarlyAsync(pc.MachineGuid);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Strings.Get("CannotStopSessionTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else
        {
            var window = new PostpayPaymentWindow(Services, pc) { Owner = this };
            window.ShowDialog();
        }

        await RefreshAsync();
    }

    // ---- power control (SPEC section 6) ----

    private async void OnTurnOn(object sender, RoutedEventArgs e) =>
        await RunPowerActionAsync((power, pcs) => power.TurnOnAsync(pcs), checkActiveSessions: false);

    private async void OnRestart(object sender, RoutedEventArgs e) =>
        await RunPowerActionAsync((power, pcs) => power.RestartAsync(pcs), checkActiveSessions: true);

    private async void OnShutDown(object sender, RoutedEventArgs e) =>
        await RunPowerActionAsync((power, pcs) => power.ShutdownAsync(pcs), checkActiveSessions: true);

    private async Task RunPowerActionAsync(Func<PowerService, List<Pc>, Task<int>> action, bool checkActiveSessions)
    {
        var selected = PcGrid.SelectedItems.Cast<PcRow>().ToList();
        List<PcRow> targets;

        if (selected.Count == 0)
        {
            if (MessageBox.Show(Strings.Get("NoPcsSelectedConfirmAll"), Strings.Get("ConfirmTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            targets = PcGrid.ItemsSource.Cast<PcRow>().Where(p => p.Enabled).ToList();
        }
        else
        {
            targets = selected;
        }

        if (checkActiveSessions && targets.Any(p => p.State is PcState.PrepaySession or PcState.PostpaySession or PcState.LimitReached))
        {
            if (MessageBox.Show(Strings.Get("SelectedHaveActiveSessionConfirm"), Strings.Get("ConfirmTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var power = scope.ServiceProvider.GetRequiredService<PowerService>();

        var ids = targets.Select(p => p.Id).ToHashSet();
        var pcs = await db.Pcs.Where(p => ids.Contains(p.Id)).ToListAsync();

        var sent = await action(power, pcs);
        if (sent == 0)
            MessageBox.Show(Strings.Get("NoEligiblePcs"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);

        await RefreshAsync();
    }

    private async void OnSettings(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(Services) { Owner = this };
        if (window.ShowDialog() == true)
            await RefreshAsync();
    }

    private void OnReports(object sender, RoutedEventArgs e)
    {
        var window = new ReportsWindow(Services) { Owner = this };
        window.ShowDialog();
    }
}
