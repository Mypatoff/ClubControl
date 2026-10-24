using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        StartButton.Content = "▶ " + Strings.Get("ToolbarStart");
        StartButton.ToolTip = Strings.Get("TooltipStart");
        StopButton.Content = "⏹ " + Strings.Get("ToolbarStop");
        StopButton.ToolTip = Strings.Get("TooltipStop");
        MoveButton.Content = "↔ " + Strings.Get("ToolbarMove");
        ExtendButton.Content = "⏱ " + Strings.Get("ToolbarExtend");
        TurnOnButton.Content = "⚡ " + Strings.Get("ToolbarTurnOn");
        RestartButton.Content = "🔁 " + Strings.Get("ToolbarRestart");
        ShutDownButton.Content = "⏻ " + Strings.Get("ToolbarShutDown");
        AddComputerButton.Content = "🖥 " + Strings.Get("AddComputerTitle");
        AddBilliardButton.Content = "🎱 " + Strings.Get("AddBilliardTitle");
        SettingsButton.Content = "⚙ " + Strings.Get("ToolbarSettings");
        ReportsButton.Content = "📊 " + Strings.Get("ToolbarReports");

        CtxStart.Header = Strings.Get("ToolbarStart");
        CtxFree.Header = Strings.Get("FreeSessionTitle");
        CtxStop.Header = Strings.Get("ToolbarStop");
        CtxMove.Header = Strings.Get("ToolbarMove");
        CtxExtend.Header = Strings.Get("ToolbarExtend");
        CtxTurnOn.Header = Strings.Get("ToolbarTurnOn");
        CtxRestart.Header = Strings.Get("ToolbarRestart");
        CtxShutDown.Header = Strings.Get("ToolbarShutDown");

        EventLogHeaderText.Text = Strings.Get("EventLogHeader");

        KindIconColumn.Header = "";
        PcNumberColumn.Header = Strings.Get("ColumnPcNumber");
        TariffColumn.Header = Strings.Get("ColumnTariff");
        StateColumn.Header = Strings.Get("ColumnState");
        SessionColumn.Header = Strings.Get("ColumnSession");
        AmountColumn.Header = Strings.Get("ColumnAmount");
        StartColumn.Header = Strings.Get("ColumnStart");
        EndColumn.Header = Strings.Get("ColumnEnd");
        RemainingColumn.Header = Strings.Get("ColumnRemaining");
    }

    private bool _allowClose;

    // The window only ever hides - the tray's "Exit" (password-protected) is the only way to really
    // quit (SPEC 1) - except when the App is recreating this window for a live theme switch, which
    // calls CloseForReal instead of Close.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            _refreshTimer.Stop();
            return;
        }

        e.Cancel = true;
        Hide();
    }

    /// <summary>Used only when the App is replacing this window outright (e.g. a theme switch); never by the user.</summary>
    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    private async Task RefreshAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        var pcs = await db.Pcs.AsNoTracking().OrderBy(p => p.Number).ToListAsync();
        var tariffNames = await db.Tariffs.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);
        var snapshots = await sessionService.GetActiveSessionSnapshotsAsync();

        var selectedIds = PcGrid.SelectedItems.Cast<PcRow>().Select(p => p.Id).ToHashSet();
        var rows = pcs.Select(pc => BuildRow(pc, tariffNames, snapshots.GetValueOrDefault(pc.Id))).ToList();
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

    private static PcRow BuildRow(Pc pc, Dictionary<int, string> tariffNames, SessionService.ActiveSessionSnapshot? snap)
    {
        var amount = "";
        var start = "";
        var end = "";
        var remaining = "";
        var sessionType = "";

        if (snap is not null)
        {
            sessionType = Strings.SessionTypeName(snap.Type, snap.IsFree);
            amount = snap.CapUzs > 0
                ? $"{snap.ConsumedUzs:N0} / {snap.CapUzs:N0} UZS"
                : $"{snap.AmountOrCostUzs:N0} UZS";
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
            IsManual = pc.IsManual,
            Kind = pc.Kind,
            Number = pc.Number,
            TariffId = pc.TariffId,
            State = pc.State,
            KindIcon = pc.Kind == ResourceKind.Billiard ? "🎱" : "🖥",
            TariffDisplay = pc.TariffId.HasValue ? tariffNames.GetValueOrDefault(pc.TariffId.Value, "") : "",
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

    // ---- right-click: select the row under the cursor unless it is already part of a multi-selection.
    // Right-clicking anywhere that isn't a row (empty space, the header) suppresses the context
    // menu entirely instead of opening it with nothing valid to act on. ----

    private void PcGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
        if (row?.Item is not PcRow pcRow)
        {
            e.Handled = true;
            return;
        }

        if (!PcGrid.SelectedItems.Contains(pcRow))
        {
            PcGrid.SelectedItems.Clear();
            PcGrid.SelectedItems.Add(pcRow);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    // ---- keyboard shortcuts (Enter = Start, Delete = Stop) and the selection-count hint ----

    private void PcGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = PcGrid.SelectedItems.Count;
        SelectionCountText.Text = count > 0 ? Strings.Format("SelectionCountFormat", count) : "";
    }

    private void PcGrid_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter:
                OnStart(sender, e);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Delete:
                OnStop(sender, e);
                e.Handled = true;
                break;
        }
    }

    // ---- toolbar ----

    private PcRow? GetSingleSelectedPc()
    {
        var selected = PcGrid.SelectedItems.Cast<PcRow>().ToList();
        if (selected.Count == 1)
            return selected[0];

        MessageBox.Show(Strings.Get("SelectExactlyOnePc"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    /// <summary>
    /// Validates the current selection for Start/Free (SPEC extension: bulk-start several PCs or
    /// billiards at once, as long as they all share one tariff - that's what makes "apply the same
    /// prepay/postpay/free parameters to each" unambiguous). Shows the relevant error and returns
    /// null if the selection isn't usable.
    /// </summary>
    private List<PcRow>? GetReadySelectionForStart()
    {
        var selected = PcGrid.SelectedItems.Cast<PcRow>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(Strings.Get("SelectAtLeastOnePc"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        if (selected.Any(p => !p.Number.HasValue || !p.TariffId.HasValue || p.HasActiveSession))
        {
            MessageBox.Show(Strings.Get("SelectedNotReadyToStart"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        if (selected.Select(p => p.TariffId!.Value).Distinct().Count() > 1)
        {
            MessageBox.Show(Strings.Get("SelectedMustShareTariff"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        return selected;
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        var targets = GetReadySelectionForStart();
        if (targets is null)
            return;

        var window = new StartSessionWindow(Services, targets) { Owner = this };
        if (window.ShowDialog() == true)
            await RefreshAsync();
    }

    private async void OnFree(object sender, RoutedEventArgs e)
    {
        var targets = GetReadySelectionForStart();
        if (targets is null)
            return;

        var window = new FreeSessionWindow(Services, targets) { Owner = this };
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

        if (!pc.HasActiveSession)
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

        if (checkActiveSessions && targets.Any(p => p.HasActiveSession))
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

    // ---- manual resources (SPEC extension: offline PCs and non-computer resources like billiard tables) ----

    private async void OnAddComputer(object sender, RoutedEventArgs e) => await AddResourceAsync(ResourceKind.Computer);

    private async void OnAddBilliard(object sender, RoutedEventArgs e) => await AddResourceAsync(ResourceKind.Billiard);

    private async Task AddResourceAsync(ResourceKind kind)
    {
        var window = new AddResourceWindow(Services, kind) { Owner = this };
        if (window.ShowDialog() == true)
            await RefreshAsync();
    }

    private async void OnSettings(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(Services) { Owner = this };
        var saved = window.ShowDialog() == true;

        // The dialog is fully closed by this point, so it is safe to replace this window outright
        // (see SettingsWindow.NewThemeIfChanged - doing this while the dialog was still open would
        // risk WPF closing it as a side effect of closing its owner).
        if (window.NewThemeIfChanged is { } newTheme)
            ((App)Application.Current).ApplyThemeAndRefreshWindows(newTheme);
        else if (saved)
            await RefreshAsync();
    }

    private void OnReports(object sender, RoutedEventArgs e)
    {
        var window = new ReportsWindow(Services) { Owner = this };
        window.ShowDialog();
    }
}
