using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ClubControl.Client.Native;
using ClubControl.Client.Resources;
using ClubControl.Client.Windows;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using Microsoft.AspNetCore.SignalR.Client;

namespace ClubControl.Client.Services;

/// <summary>
/// The client's state machine (SPEC section 4). Owns the lock windows, the timer window, the
/// key-blocking hook, and the unlock-hotkey prompt. Everything here reacts to commands from the
/// server; it never computes money or decides when a session ends - the server is the only
/// source of truth for that (see CLAUDE.md). The one thing it does on its own is let staff unlock
/// a locked PC with a password (Ctrl+Shift+F12), connected to the server or not.
/// </summary>
public class AppController
{
    private ServerConnection? _connection;

    private readonly List<LockWindow> _lockWindows = new();
    private TimerWindow? _timerWindow;
    private ToastWindow? _toastWindow;
    private NativeKeyboardHook? _blockingHook;

    private PcState _state = PcState.Locked;
    private LockInfo? _lastLockInfo;
    private ClientSettingsDto _settings = new();

    private SessionType _sessionType;
    private int _sessionBaselineRemaining;
    private int _sessionBaselineElapsed;
    private int _sessionCurrentCostUzs;
    private int _sessionMaxPaymentUzs;
    private int _sessionClosedCostUzs;
    private int _sessionBaselineOpenElapsed;
    private int _sessionPricePerHourUzs;
    private int _sessionMinPaymentUzs;
    private readonly Stopwatch _sessionStopwatch = new();
    private bool _warnedMinutes1;
    private bool _warnedMinutes2;
    private bool _warnedCost;

    private readonly DispatcherTimer _tickTimer;

    // Staff unlock (SPEC section 4): the hash is cached on disk so it works with no server, e.g.
    // right after a reboot. The server sends the current one with every ApplySettings.
    private string? _unlockPasswordHash = UnlockPasswordStore.Load();
    private UnlockWindow? _unlockWindow;
    private bool _pendingUnlockReport; // unlocked while offline - tell the server once we can

    // Saves electricity: an idle PC - locked, waiting for the next customer, nobody mid-payment -
    // shuts itself down after this long. Never runs for LimitReached (someone is still there about
    // to pay) or while in a session. See Setting.AutoShutdownAfterMinutes on the server.
    private readonly DispatcherTimer _autoShutdownTimer;

    public AppController()
    {
        Strings.Apply(_settings.LockScreenLanguage);

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => OnTick();

        _autoShutdownTimer = new DispatcherTimer();
        _autoShutdownTimer.Tick += (_, _) => OnAutoShutdownElapsed();
    }

    /// <summary>Locks the PC immediately, with or without a server.</summary>
    public void Start()
    {
        EnterLocked(null);
        _tickTimer.Start();
        UpdateAutoShutdownTimer();
    }

    public void AttachConnection(ServerConnection connection)
    {
        _connection = connection;
        _connection.CommandReceived += command => Application.Current.Dispatcher.Invoke(() => Handle(command));
        _connection.StateChanged += state => Application.Current.Dispatcher.Invoke(() => OnConnectionStateChanged(state));
    }

    // ---- commands from the server ----

    private void Handle(ClientCommand command)
    {
        // A manual unlock lasts until the server sends a command that changes what the PC is
        // doing (Lock, Unlock, session...). ApplySettings does not, so it leaves the unlock alone.
        switch (command.Type)
        {
            case CommandType.Lock:
                EnterLocked(command.Lock);
                break;
            case CommandType.Unlock:
                EnterUnlockedIdle();
                break;
            case CommandType.StartSession:
                EnterSession(command.Session, isNewSession: true);
                break;
            case CommandType.UpdateSession:
                EnterSession(command.Session, isNewSession: false);
                break;
            case CommandType.EndSession:
                EndSession();
                break;
            case CommandType.Shutdown:
                PowerControl.Shutdown();
                break;
            case CommandType.Restart:
                PowerControl.Restart();
                break;
            case CommandType.UpdateClient:
                if (_connection is not null)
                    _ = ClientUpdater.ApplyUpdateAsync(_connection.ServerIp, _connection.Port, command);
                break;
            case CommandType.ApplySettings:
                if (command.Settings is not null)
                {
                    _settings = command.Settings;
                    Strings.Apply(_settings.LockScreenLanguage);

                    if (_settings.UnlockPasswordHash != _unlockPasswordHash)
                    {
                        _unlockPasswordHash = _settings.UnlockPasswordHash;
                        UnlockPasswordStore.Save(_unlockPasswordHash);
                    }
                }
                RefreshLockWindows();
                break;
        }

        UpdateAutoShutdownTimer();
    }

    private void EnterLocked(LockInfo? info)
    {
        _state = info?.LimitReached == true ? PcState.LimitReached : PcState.Locked;
        _lastLockInfo = info;

        HideTimerWindow();
        ShowLockWindows();
        UpdateHook();
    }

    private void EnterUnlockedIdle()
    {
        _state = PcState.Free;

        CloseUnlockPrompt();
        HideLockWindows();
        HideTimerWindow();
        UpdateHook();
    }

    private void EnterSession(SessionInfo? info, bool isNewSession)
    {
        if (info is null)
            return;

        // This PC locked itself at the cost limit while the server was unreachable. The update the
        // server sends on reconnect already shows the capped cost - stay locked until the server's
        // own Lock arrives, instead of opening the PC for the second or two before it does.
        if (!isNewSession && _state == PcState.LimitReached &&
            info.MaxPaymentUzs > 0 && info.CurrentCostUzs >= info.MaxPaymentUzs)
            return;

        _state = info.Type == SessionType.Prepay ? PcState.PrepaySession : PcState.PostpaySession;

        _sessionType = info.Type;
        _sessionBaselineRemaining = info.RemainingSeconds;
        _sessionBaselineElapsed = info.ElapsedSeconds;
        _sessionCurrentCostUzs = info.CurrentCostUzs;
        _sessionMaxPaymentUzs = info.MaxPaymentUzs;
        _sessionClosedCostUzs = info.ClosedSegmentsCostUzs;
        _sessionBaselineOpenElapsed = info.OpenSegmentElapsedSeconds;
        _sessionPricePerHourUzs = info.PricePerHourUzs;
        _sessionMinPaymentUzs = info.MinPaymentUzs;
        _sessionStopwatch.Restart();

        if (isNewSession)
        {
            _warnedMinutes1 = false;
            _warnedMinutes2 = false;
            _warnedCost = false;
        }

        CloseUnlockPrompt();
        HideLockWindows();
        UpdateHook();
        ShowTimerWindow();
        _timerWindow!.SetManualUnlockBadgeVisible(false); // the timer window may still be up from a manual unlock
        RenderTimer();
    }

    private void EndSession()
    {
        switch (_settings.AfterSessionAction)
        {
            case AfterSessionAction.Restart:
                PowerControl.Restart();
                break;
            case AfterSessionAction.Shutdown:
                PowerControl.Shutdown();
                break;
            default:
                ProcessCloser.CloseAll(_settings.ProcessListCsv);
                break;
        }

        EnterLocked(null);
    }

    // ---- local countdown/elapsed between server updates ----

    private void OnTick()
    {
        if (_state is PcState.PrepaySession or PcState.PostpaySession)
            RenderTimer();
    }

    private void RenderTimer()
    {
        if (_timerWindow is null)
            return;

        var elapsedSinceSync = (int)_sessionStopwatch.Elapsed.TotalSeconds;

        if (_sessionType == SessionType.Prepay)
        {
            var remaining = Math.Max(0, _sessionBaselineRemaining - elapsedSinceSync);
            _timerWindow.ShowSession(_sessionType, remaining, 0, 0);

            // Normally the server ends the session (a second or two later). With no server to do it,
            // the PC must not stay open past the paid time - end it the same way the server would.
            if (remaining == 0 && !IsServerReachable)
            {
                EndSession();
                return;
            }

            CheckMinuteWarnings(remaining);
        }
        else
        {
            var elapsed = _sessionBaselineElapsed + elapsedSinceSync;
            var cost = EstimatePostpayCost(elapsedSinceSync);
            _timerWindow.ShowSession(_sessionType, 0, elapsed, cost);

            // Same rule as the server at the tariff maximum (SPEC section 5): lock and wait for the admin.
            if (_sessionMaxPaymentUzs > 0 && cost >= _sessionMaxPaymentUzs && !IsServerReachable)
            {
                LockAtCostLimit();
                return;
            }

            CheckCostWarning(cost);
        }
    }

    private bool IsServerReachable => _connection?.State == HubConnectionState.Connected;

    /// <summary>
    /// The postpay cost right now, extrapolated from the server's last update with the shared billing
    /// math so it matches what the server will charge. Falls back to the server's number when no
    /// price was sent (free sessions).
    /// </summary>
    private int EstimatePostpayCost(int secondsSinceSync)
    {
        if (_sessionPricePerHourUzs <= 0)
            return _sessionCurrentCostUzs;

        var cost = BillingCalculator.PostpayCostWithOpenSegment(
            _sessionClosedCostUzs, _sessionBaselineOpenElapsed + secondsSinceSync, _sessionPricePerHourUzs, _sessionMinPaymentUzs);

        return _sessionMaxPaymentUzs > 0 ? Math.Min(cost, _sessionMaxPaymentUzs) : cost;
    }

    private void LockAtCostLimit() =>
        EnterLocked(new LockInfo
        {
            LimitReached = true,
            PcNumber = _lastLockInfo?.PcNumber,
            TariffName = _lastLockInfo?.TariffName,
            PricePerHourUzs = _lastLockInfo?.PricePerHourUzs ?? 0
        });

    private void CheckMinuteWarnings(int remainingSeconds)
    {
        if (remainingSeconds <= 0)
            return;

        if (!_warnedMinutes1 && remainingSeconds <= _settings.WarningMinutes1 * 60)
        {
            _warnedMinutes1 = true;
            ShowWarning(Strings.Format("WarningMinutesLeft", _settings.WarningMinutes1));
        }

        if (!_warnedMinutes2 && remainingSeconds <= _settings.WarningMinutes2 * 60)
        {
            _warnedMinutes2 = true;
            ShowWarning(Strings.Format("WarningMinutesLeft", _settings.WarningMinutes2));
        }
    }

    private void CheckCostWarning(int costUzs)
    {
        if (_warnedCost || _sessionMaxPaymentUzs <= 0)
            return;

        if (costUzs >= _sessionMaxPaymentUzs * 0.9)
        {
            _warnedCost = true;
            ShowWarning(Strings.Get("WarningCostApproaching"));
        }
    }

    private void ShowWarning(string message)
    {
        System.Media.SystemSounds.Exclamation.Play();

        // At most one on screen at a time - a new warning replaces whatever is still showing.
        _toastWindow?.Close();
        _toastWindow = new ToastWindow(message);
        _toastWindow.Closed += (_, _) => _toastWindow = null;
        _toastWindow.Show();
    }

    // ---- staff unlock (SPEC section 4): Ctrl+Shift+F12 on a locked PC asks for the unlock password ----

    private void OnConnectionStateChanged(HubConnectionState state)
    {
        if (state == HubConnectionState.Connected)
            _ = TrySendPendingUnlockReportAsync();
    }

    private void ShowUnlockPrompt()
    {
        if (_state is not (PcState.Locked or PcState.LimitReached))
            return;

        if (_unlockWindow is not null)
        {
            _unlockWindow.Activate();
            return;
        }

        // The lock windows keep re-asserting topmost every 2 s, which would bury the prompt and
        // steal its focus mid-typing - hold them still while it is open.
        foreach (var window in _lockWindows)
            window.PauseTopmost = true;

        _unlockWindow = new UnlockWindow(_unlockPasswordHash);
        _unlockWindow.Unlocked += ManualUnlock;
        _unlockWindow.Closed += (_, _) =>
        {
            _unlockWindow = null;
            foreach (var window in _lockWindows)
                window.PauseTopmost = false;
            UpdateAutoShutdownTimer();
        };

        UpdateAutoShutdownTimer(); // do not power off while someone is typing the password
        _unlockWindow.Show();
        _unlockWindow.Activate();
    }

    private void CloseUnlockPrompt() => _unlockWindow?.Close();

    /// <summary>Correct password entered: open the PC for free use (no session, no billing) until the server says otherwise.</summary>
    private void ManualUnlock()
    {
        if (_state is not (PcState.Locked or PcState.LimitReached))
            return;

        _state = PcState.ManualUnlocked;

        CloseUnlockPrompt();
        HideLockWindows();
        UpdateHook();
        ShowTimerWindow();
        _timerWindow!.SetManualUnlockBadgeVisible(true);
        UpdateAutoShutdownTimer();

        _pendingUnlockReport = true;
        _ = TrySendPendingUnlockReportAsync();
    }

    /// <summary>Tells the server about a manual unlock for its event log; retried on reconnect if the server was unreachable.</summary>
    private async Task TrySendPendingUnlockReportAsync()
    {
        if (!_pendingUnlockReport || _connection is null)
            return;

        if (await _connection.ReportManualUnlockAsync())
            _pendingUnlockReport = false;
    }

    // ---- auto-shutdown for idle locked PCs (not LimitReached, not mid-session) ----

    private void UpdateAutoShutdownTimer()
    {
        if (_state == PcState.Locked && _unlockWindow is null && _settings.AutoShutdownAfterMinutes > 0)
        {
            var interval = TimeSpan.FromMinutes(_settings.AutoShutdownAfterMinutes);

            // Only (re)start on a genuinely new lock or a changed interval - an incidental re-entry
            // into the same Locked state (e.g. a resent command) must not keep resetting the clock.
            if (!_autoShutdownTimer.IsEnabled || _autoShutdownTimer.Interval != interval)
            {
                _autoShutdownTimer.Stop();
                _autoShutdownTimer.Interval = interval;
                _autoShutdownTimer.Start();
            }
        }
        else
        {
            _autoShutdownTimer.Stop();
        }
    }

    private void OnAutoShutdownElapsed()
    {
        _autoShutdownTimer.Stop();

        if (_state == PcState.Locked)
            PowerControl.Shutdown();
    }

    // ---- windows ----

    private void ShowLockWindows()
    {
        if (_lockWindows.Count == 0)
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var window = new LockWindow(screen.Bounds);
                window.Show();
                _lockWindows.Add(window);
            }
        }

        RefreshLockWindows();
    }

    private void HideLockWindows()
    {
        foreach (var window in _lockWindows)
            window.Close();

        _lockWindows.Clear();
    }

    private void RefreshLockWindows()
    {
        foreach (var window in _lockWindows)
            window.Render(_settings, _lastLockInfo);
    }

    private void ShowTimerWindow()
    {
        _timerWindow ??= new TimerWindow();
        if (!_timerWindow.IsVisible)
            _timerWindow.Show();
    }

    private void HideTimerWindow()
    {
        _timerWindow?.Close();
        _timerWindow = null;
    }

    // ---- keyboard hook ----

    /// <summary>Runs inside the low-level hook, so it must return fast: the prompt is opened later, on the UI queue.</summary>
    private bool OnLockedKeyDown(int vkCode)
    {
        if (UnlockHotkey.IsPressed(vkCode))
        {
            Application.Current.Dispatcher.BeginInvoke(ShowUnlockPrompt);
            return true;
        }

        return BlockedKeys.IsBlockedCombo(vkCode);
    }

    private void UpdateHook()
    {
        var shouldBlock = _state is PcState.Locked or PcState.LimitReached;

        if (shouldBlock && _blockingHook is null)
        {
            _blockingHook = new NativeKeyboardHook(OnLockedKeyDown);
            _blockingHook.Install();
        }
        else if (!shouldBlock && _blockingHook is not null)
        {
            _blockingHook.Uninstall();
            _blockingHook = null;
        }
    }
}
