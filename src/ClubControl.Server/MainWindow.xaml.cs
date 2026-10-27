using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Hubs;
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
        UpdateClientButton.Content = "⬇ " + Strings.Get("ToolbarUpdateClient");
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
        CtxUpdateClient.Header = Strings.Get("ToolbarUpdateClient");

        EventLogHeaderText.Text = Strings.Get("EventLogHeader");

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
        // The refresh rebuilds every card, which would break a rectangle selection in progress.
        if (_dragging)
            return;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        var tariffNames = await db.Tariffs.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);
        var snapshots = await sessionService.GetActiveSessionSnapshotsAsync();

        // Disabled resources are hidden from this operational panel entirely (SPEC section 8) -
        // they're still locked and excluded from "all" actions, but a disabled PC/resource has no
        // visible row here to be confused by. Settings > Computers is where they're re-enabled or
        // actually deleted. The one exception is a disabled PC that somehow still has an active
        // session (e.g. disabled mid-session) - that must stay visible, since hiding it would also
        // hide the only place to free/stop it and collect payment.
        var pcs = await db.Pcs.AsNoTracking().OrderBy(p => p.Number).ToListAsync();
        pcs = pcs.Where(p => p.Enabled || snapshots.ContainsKey(p.Id)).ToList();

        var selectedIds = PcList.SelectedItems.Cast<PcRow>().Select(p => p.Id).ToHashSet();
        var rows = pcs.Select(pc => BuildRow(pc, tariffNames, snapshots.GetValueOrDefault(pc.Id))).ToList();

        // Replacing ItemsSource rebuilds every card and resets the scroll position, so remember it.
        var scrollOffset = FindDescendant<ScrollViewer>(PcList)?.VerticalOffset ?? 0;
        PcList.ItemsSource = BuildCardView(rows);
        foreach (var row in rows.Where(r => selectedIds.Contains(r.Id)))
            PcList.SelectedItems.Add(row);
        if (scrollOffset > 0)
            _ = Dispatcher.BeginInvoke(() => FindDescendant<ScrollViewer>(PcList)?.ScrollToVerticalOffset(scrollOffset),
                DispatcherPriority.Loaded);

        UpdateCounters(rows);

        var events = await db.EventLogEntries.AsNoTracking()
            .OrderByDescending(e => e.TimestampUtc)
            .Take(200)
            .ToListAsync();
        EventLogList.ItemsSource = events.Select(FormatEvent).ToList();

        StatusText.Text = Strings.Format("StatusListening", NetworkConstants.SignalRPort, NetworkConstants.DiscoveryPort,
            pcs.Count(p => p.IsOnline), pcs.Count);
    }

    /// <summary>PCs first, then other resources - the list shows them as two separate sections (see MainWindow.xaml).</summary>
    private static ListCollectionView BuildCardView(List<PcRow> rows)
    {
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PcRow.IsOther)));
        view.SortDescriptions.Add(new SortDescription(nameof(PcRow.IsOther), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(PcRow.Number), ListSortDirection.Ascending));
        return view;
    }

    private void UpdateCounters(List<PcRow> rows)
    {
        var computers = rows.Where(r => !r.IsOther).ToList();
        var others = rows.Where(r => r.IsOther).ToList();

        var busy = computers.Count(r => r.HasActiveSession);
        var offline = computers.Count(r => r.IsDimmed && !r.HasActiveSession);

        StatFreeText.Text = $"{computers.Count - busy - offline} {Strings.Get("StateFree")}";
        StatBusyText.Text = $"{busy} {Strings.Get("StatBusy")}";
        StatOfflineText.Text = $"{offline} {Strings.Get("StatOffline")}";
        StatOtherText.Text = $"{others.Count(r => r.HasActiveSession)}/{others.Count} {Strings.Get("KindOtherPlural")}";
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
            // Prepay shows progress through the prepaid total; postpay shows only what the client owes right now.
            amount = snap.Type == SessionType.Prepay && snap.CapUzs > 0
                ? $"{snap.ConsumedUzs:N0} / {snap.CapUzs:N0} UZS"
                : $"{snap.AmountOrCostUzs:N0} UZS";
            start = snap.StartUtc.ToLocalTime().ToString("HH:mm:ss");

            if (snap.PlannedEndUtc.HasValue)
                end = snap.PlannedEndUtc.Value.ToLocalTime().ToString("HH:mm:ss");

            if (snap.Type == SessionType.Prepay)
                remaining = FormatTimeLeft(snap.RemainingSeconds);
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
            Name = pc.Name,
            TariffId = pc.TariffId,
            State = pc.State,
            KindIcon = pc.Kind == ResourceKind.Other ? "📦" : "🖥",
            TariffDisplay = pc.TariffId.HasValue ? tariffNames.GetValueOrDefault(pc.TariffId.Value, "") : "",
            StateDisplay = Strings.PcStateName(pc.State, pc.IsConfigured),
            SessionTypeDisplay = sessionType,
            AmountDisplay = amount,
            StartDisplay = start,
            EndDisplay = end,
            RemainingDisplay = remaining,
            ClientVersion = pc.IsOnline ? ClubHub.GetClientVersion(pc.MachineGuid) : ""
        };
    }

    private static string FormatEvent(EventLogEntry e) =>
        $"{e.TimestampUtc.ToLocalTime():HH:mm:ss} [{e.Type}] {e.Message}";

    // ---- right-click: select the row under the cursor unless it is already part of a multi-selection.
    // Right-clicking anywhere that isn't a row (empty space, the header) suppresses the context
    // menu entirely instead of opening it with nothing valid to act on. ----

    private bool _rightClickedCard;

    private void PcList_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var card = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        _rightClickedCard = card?.Content is PcRow;
        if (card?.Content is not PcRow pcRow)
        {
            e.Handled = true;
            return;
        }

        if (!PcList.SelectedItems.Contains(pcRow))
        {
            PcList.SelectedItems.Clear();
            PcList.SelectedItems.Add(pcRow);
        }
    }

    // Handling the mouse-down alone doesn't stop the menu: it opens on mouse-up, so cancel it here.
    // A negative CursorLeft means it was opened from the keyboard (menu key), which needs a selection.
    private void PcList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var fromKeyboard = e.CursorLeft < 0;
        if (fromKeyboard ? PcList.SelectedItems.Count == 0 : !_rightClickedCard)
            e.Handled = true;
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

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;

            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    // ---- keyboard shortcuts (Enter = Start, Delete = Stop) and the selection-count hint ----

    private void PcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = PcList.SelectedItems.Count;
        SelectionCountText.Text = count > 0 ? Strings.Format("SelectionCountFormat", count) : "";
    }

    // ---- drag-to-select: a plain click selects one card (the list's normal behaviour); pressing a card and
    // dragging to another selects every card from the first to the last, in list order (like PC 1 to PC 5).
    // Holding Ctrl while pressing keeps what was already selected, so several groups can be added one by one. ----

    private PcRow? _dragAnchor;
    private bool _dragging;
    private HashSet<PcRow> _selectionBeforeDrag = new();

    private void PcList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragging = false;
        _dragAnchor = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource)?.Content as PcRow;
        if (_dragAnchor is not null)
            _cursorId = _anchorId = _dragAnchor.Id;

        // A click on empty space (between cards, a section header, the blank area) deselects everything.
        // Ctrl-click is left alone so a slip while building a multi-selection doesn't wipe it, and the
        // scrollbar is not "empty space".
        var onScrollBar = FindAncestor<System.Windows.Controls.Primitives.ScrollBar>((DependencyObject)e.OriginalSource) is not null;
        if (_dragAnchor is null && !onScrollBar && !System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            PcList.SelectedItems.Clear();

        _selectionBeforeDrag = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)
            ? PcList.SelectedItems.Cast<PcRow>().ToHashSet()
            : new HashSet<PcRow>();
    }

    private void PcList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragAnchor is null)
            return;

        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        // The list captures the mouse while it is pressed, so find the card under the pointer by position.
        var position = e.GetPosition(PcList);
        var hovered = FindDescendants<ListBoxItem>(PcList)
            .FirstOrDefault(card => card.TransformToAncestor(PcList).TransformBounds(new Rect(card.RenderSize)).Contains(position))
            ?.Content as PcRow;
        if (hovered is null || (!_dragging && hovered == _dragAnchor))
            return;

        _dragging = true;

        // Stops the list's own drag handling so only this range logic changes the selection.
        e.Handled = true;

        var cards = PcList.Items.Cast<PcRow>().ToList();
        var from = cards.IndexOf(_dragAnchor);
        var to = cards.IndexOf(hovered);
        if (from < 0 || to < 0)
            return;

        var wanted = new HashSet<PcRow>(_selectionBeforeDrag);
        foreach (var row in cards.Skip(Math.Min(from, to)).Take(Math.Abs(from - to) + 1))
            wanted.Add(row);

        foreach (var row in PcList.SelectedItems.Cast<PcRow>().Where(r => !wanted.Contains(r)).ToList())
            PcList.SelectedItems.Remove(row);
        foreach (var row in wanted.Where(r => !PcList.SelectedItems.Contains(r)))
            PcList.SelectedItems.Add(row);
    }

    private void PcList_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _dragAnchor = null;
        _dragging = false;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (var nested in FindDescendants<T>(child))
                yield return nested;
        }
    }

    // Listened to on the whole window (not just the list), so it works whichever control has focus -
    // otherwise Enter would "click" a toolbar button that was focused by an earlier click.
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var isArrow = e.Key is System.Windows.Input.Key.Up or System.Windows.Input.Key.Down
            or System.Windows.Input.Key.Left or System.Windows.Input.Key.Right;
        if (!isArrow && e.Key is not (System.Windows.Input.Key.Enter or System.Windows.Input.Key.Delete))
            return;

        // Let real text boxes keep their own Enter/Delete/arrow behaviour.
        var focused = System.Windows.Input.Keyboard.FocusedElement;
        if (focused is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox
            or System.Windows.Controls.ComboBox)
            return;

        if (isArrow)
        {
            // Arrows inside another list (the event log) just scroll that list.
            if (focused is DependencyObject d && FindAncestor<ListBox>(d) is { } otherList && otherList != PcList)
                return;

            e.Handled = true;
            MoveSelection(e.Key);
            return;
        }

        e.Handled = true;
        if (e.IsRepeat)
            return;

        if (e.Key == System.Windows.Input.Key.Enter)
            OnStart(sender, e);
        else
            OnStop(sender, e);
    }

    // ---- arrow keys: move through the cards the way they look on screen.
    //   Arrow            select only the next card
    //   Shift + arrow    select every card (in list order) from the starting card to the next one
    //   Ctrl + arrow     add the next card to what is already selected
    //   Ctrl+Shift+arrow add that whole range to what is already selected ----

    private int? _cursorId;   // the card the last arrow press landed on
    private int? _anchorId;   // where a Shift range starts (set by clicks and plain/Ctrl arrows)

    private void MoveSelection(System.Windows.Input.Key key)
    {
        var cards = FindDescendants<ListBoxItem>(PcList).Where(c => c.Content is PcRow).ToList();
        if (cards.Count == 0)
            return;

        var shift = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        var ctrl = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);

        // Where we are now: the last card moved to, else the last selected card, else nothing (first press picks the first card).
        var current = cards.FirstOrDefault(c => ((PcRow)c.Content).Id == _cursorId)
            ?? cards.LastOrDefault(c => c.IsSelected);

        var target = current is null ? cards[0] : FindNeighbour(cards, current, key);
        if (target is null)
            return;

        var targetRow = (PcRow)target.Content;
        _cursorId = targetRow.Id;

        var wanted = new HashSet<PcRow>();
        if (ctrl)
            wanted.UnionWith(PcList.SelectedItems.Cast<PcRow>());

        if (shift)
        {
            // Same list-order range as drag-select: the anchor card to the target card.
            var ordered = PcList.Items.Cast<PcRow>().ToList();
            var anchor = ordered.FirstOrDefault(r => r.Id == _anchorId) ?? (current?.Content as PcRow) ?? targetRow;
            _anchorId = anchor.Id;
            var from = ordered.IndexOf(anchor);
            var to = ordered.IndexOf(targetRow);
            foreach (var row in ordered.Skip(Math.Min(from, to)).Take(Math.Abs(from - to) + 1))
                wanted.Add(row);
        }
        else
        {
            wanted.Add(targetRow);
            _anchorId = targetRow.Id;
        }

        foreach (var row in PcList.SelectedItems.Cast<PcRow>().Where(r => !wanted.Contains(r)).ToList())
            PcList.SelectedItems.Remove(row);
        foreach (var row in wanted.Where(r => !PcList.SelectedItems.Contains(r)))
            PcList.SelectedItems.Add(row);

        target.BringIntoView();
    }

    /// <summary>The closest card in the pressed direction: nearest along that axis, with sideways drift penalised.</summary>
    private ListBoxItem? FindNeighbour(List<ListBoxItem> cards, ListBoxItem current, System.Windows.Input.Key key)
    {
        Rect BoundsOf(ListBoxItem card) => card.TransformToAncestor(PcList).TransformBounds(new Rect(card.RenderSize));

        var from = BoundsOf(current);
        ListBoxItem? best = null;
        var bestScore = double.MaxValue;

        foreach (var card in cards.Where(c => c != current))
        {
            var to = BoundsOf(card);
            double gap, sideways;
            switch (key)
            {
                case System.Windows.Input.Key.Right:
                    gap = to.Left - from.Right;
                    sideways = Math.Abs((to.Top + to.Height / 2) - (from.Top + from.Height / 2));
                    break;
                case System.Windows.Input.Key.Left:
                    gap = from.Left - to.Right;
                    sideways = Math.Abs((to.Top + to.Height / 2) - (from.Top + from.Height / 2));
                    break;
                case System.Windows.Input.Key.Down:
                    gap = to.Top - from.Bottom;
                    sideways = Math.Abs((to.Left + to.Width / 2) - (from.Left + from.Width / 2));
                    break;
                default:
                    gap = from.Top - to.Bottom;
                    sideways = Math.Abs((to.Left + to.Width / 2) - (from.Left + from.Width / 2));
                    break;
            }

            // Must lie beyond the current card in that direction (a pixel of slack for margins/rounding).
            if (gap < -1)
                continue;

            var score = Math.Max(gap, 0) + 2 * sideways;
            if (score < bestScore)
            {
                bestScore = score;
                best = card;
            }
        }

        return best;
    }

    // ---- toolbar ----

    private PcRow? GetSingleSelectedPc()
    {
        var selected = PcList.SelectedItems.Cast<PcRow>().ToList();
        if (selected.Count == 1)
            return selected[0];

        MessageBox.Show(Strings.Get("SelectExactlyOnePc"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    /// <summary>
    /// Validates the current selection for Start/Free (SPEC extension: bulk-start several PCs or
    /// resources at once, as long as they all share one tariff - that's what makes "apply the same
    /// prepay/postpay/free parameters to each" unambiguous). Shows the relevant error and returns
    /// null if the selection isn't usable.
    /// </summary>
    private List<PcRow>? GetReadySelectionForStart()
    {
        var selected = PcList.SelectedItems.Cast<PcRow>().ToList();
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
        var selected = PcList.SelectedItems.Cast<PcRow>().ToList();
        if (selected.Count == 2)
        {
            await MoveBetweenAsync(selected[0], selected[1]);
            return;
        }

        if (selected.Count != 1)
        {
            MessageBox.Show(Strings.Get("MoveSelectOneOrTwo"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var pc = selected[0];
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

    /// <summary>
    /// Move with two cards selected: they trade places. If only one of them is running a session it
    /// moves onto the other (free) one; if both are running, the two sessions swap. Same kind and
    /// tariff only - SessionService enforces it too, this just gives the admin a clear message first.
    /// </summary>
    private async Task MoveBetweenAsync(PcRow a, PcRow b)
    {
        if (a.Kind != b.Kind || !a.TariffId.HasValue || a.TariffId != b.TariffId)
        {
            MessageBox.Show(Strings.Get("MoveSameTariffOnly"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        static bool Running(PcRow p) => p.State is PcState.PrepaySession or PcState.PostpaySession;
        if (!Running(a) && !Running(b))
        {
            MessageBox.Show(Strings.Get("MoveOnlyActiveSession"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            using var scope = Services.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
            if (Running(a) && Running(b))
                await sessionService.SwapSessionsAsync(a.MachineGuid, b.MachineGuid);
            else if (Running(a))
                await sessionService.MoveSessionAsync(a.MachineGuid, b.MachineGuid);
            else
                await sessionService.MoveSessionAsync(b.MachineGuid, a.MachineGuid);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Validates the current selection for Stop (SPEC extension: stop several PCs/resources at
    /// once, e.g. a group finishing together). All selected must have an active session, and all
    /// must be the same kind of session - prepay (simple confirm, no refund) and postpay/limit-
    /// reached (payment collection) use different flows, so mixing them in one action isn't
    /// well-defined.
    /// </summary>
    private List<PcRow>? GetReadySelectionForStop()
    {
        var selected = PcList.SelectedItems.Cast<PcRow>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(Strings.Get("SelectAtLeastOnePc"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        if (selected.Any(p => !p.HasActiveSession))
        {
            MessageBox.Show(Strings.Get("PcHasNoActiveSession"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var isPrepayGroup = selected[0].State == PcState.PrepaySession;
        if (selected.Any(p => (p.State == PcState.PrepaySession) != isPrepayGroup))
        {
            MessageBox.Show(Strings.Get("SelectedMustShareSessionType"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        return selected;
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        var targets = GetReadySelectionForStop();
        if (targets is null)
            return;

        if (targets[0].State == PcState.PrepaySession)
        {
            using var scope = Services.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

            var (lines, totalMoney) = await BuildStopPrepayLinesAsync(sessionService, targets);
            if (new StopPrepayWindow(lines, totalMoney) { Owner = this }.ShowDialog() != true)
                return;

            var failures = new List<string>();

            foreach (var target in targets)
            {
                try
                {
                    await sessionService.EndSessionEarlyAsync(target.MachineGuid);
                }
                catch (Exception ex)
                {
                    failures.Add($"#{target.Number}: {ex.Message}");
                }
            }

            if (failures.Count > 0)
                MessageBox.Show(string.Join("\n", failures), Strings.Get("CannotStopSessionTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            var window = new PostpayPaymentWindow(Services, targets) { Owner = this };
            window.ShowDialog();
        }

        await RefreshAsync();
    }

    /// <summary>
    /// What the stop dialog shows for prepay: for each PC, how much of its prepaid money is still unused
    /// and how much time that is, plus the total. The stop itself still gives no refund (SPEC 5).
    /// </summary>
    private static async Task<(List<StopPrepayLine> Lines, string TotalMoney)> BuildStopPrepayLinesAsync(
        SessionService sessionService, List<PcRow> targets)
    {
        var lines = new List<StopPrepayLine>();
        var totalUzs = 0;

        foreach (var target in targets)
        {
            var snapshot = await sessionService.GetActiveSessionSnapshotAsync(target.MachineGuid);
            if (snapshot is null)
                continue;

            totalUzs += snapshot.RemainingMoneyUzs;
            lines.Add(new StopPrepayLine(
                target.Label,
                $"{snapshot.RemainingMoneyUzs:N0} UZS",
                $"{Strings.Get("ColumnRemaining")}: {FormatTimeLeft(snapshot.RemainingSeconds)}",
                Strings.Get("StopPrepayUnused")));
        }

        return (lines, $"{totalUzs:N0} UZS");
    }

    /// <summary>mm:ss under an hour, h:mm:ss from an hour up (a plain mm:ss would show 90 minutes as 30:00).</summary>
    private static string FormatTimeLeft(int seconds)
    {
        var left = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"mm\:ss");
    }

    // ---- power control (SPEC section 6) ----

    private async void OnTurnOn(object sender, RoutedEventArgs e) =>
        await RunPowerActionAsync((power, pcs) => power.TurnOnAsync(pcs), checkActiveSessions: false);

    private async void OnRestart(object sender, RoutedEventArgs e) =>
        await RunPowerActionAsync((power, pcs) => power.RestartAsync(pcs), checkActiveSessions: true);

    private async void OnShutDown(object sender, RoutedEventArgs e) =>
        await RunPowerActionAsync((power, pcs) => power.ShutdownAsync(pcs), checkActiveSessions: true);

    // The client briefly drops its lock screen while it swaps files and restarts itself - treat it
    // like Restart/Shutdown and ask first if a PC is mid-session.
    private async void OnUpdateClient(object sender, RoutedEventArgs e) =>
        await RunPowerActionAsync((power, pcs) => power.UpdateClientAsync(pcs), checkActiveSessions: true);

    private async Task RunPowerActionAsync(Func<PowerService, List<Pc>, Task<int>> action, bool checkActiveSessions)
    {
        var selected = PcList.SelectedItems.Cast<PcRow>().ToList();
        List<PcRow> targets;

        if (selected.Count == 0)
        {
            if (MessageBox.Show(Strings.Get("NoPcsSelectedConfirmAll"), Strings.Get("ConfirmTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            targets = PcList.ItemsSource.Cast<PcRow>().Where(p => p.Enabled).ToList();
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
