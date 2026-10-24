using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// key-blocking hook, and the offline-unlock timer. Everything here reacts to commands from the
/// server; it never computes money or decides when a session ends - the server is the only
/// source of truth for that (see CLAUDE.md).
/// </summary>
public class AppController
{
    private ServerConnection? _connection;

    private readonly List<LockWindow> _lockWindows = new();
    private TimerWindow? _timerWindow;
    private NativeKeyboardHook? _blockingHook;

    private PcState _state = PcState.Locked;
    private LockInfo? _lastLockInfo;
    private ClientSettingsDto _settings = new();

    private SessionType _sessionType;
    private int _sessionBaselineRemaining;
    private int _sessionBaselineElapsed;
    private int _sessionCurrentCostUzs;
    private int _sessionMaxPaymentUzs;
    private readonly Stopwatch _sessionStopwatch = new();
    private bool _warnedMinutes1;
    private bool _warnedMinutes2;
    private bool _warnedCost;

    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _offlineWaitTimer;
    private bool _offlineUnlocked;

    // Saves electricity: an idle PC - locked, waiting for the next customer, nobody mid-payment -
    // shuts itself down after this long. Never runs for LimitReached (someone is still there about
    // to pay) or while in a session. See Setting.AutoShutdownAfterMinutes on the server.
    private readonly DispatcherTimer _autoShutdownTimer;

    public AppController()
    {
        Strings.Apply(_settings.LockScreenLanguage);

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => OnTick();

        _offlineWaitTimer = new DispatcherTimer();
        _offlineWaitTimer.Tick += (_, _) => OnOfflineWaitElapsed();

        _autoShutdownTimer = new DispatcherTimer();
        _autoShutdownTimer.Tick += (_, _) => OnAutoShutdownElapsed();
    }

    /// <summary>Locks the PC immediately and starts the offline-wait clock, with or without a server.</summary>
    public void Start()
    {
        EnterLocked(null);
        _tickTimer.Start();
        RestartOfflineWaitTimer();
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
        // Hearing from the server at all means we can reach it - clear any offline indication
        // before acting on the command itself.
        _offlineUnlocked = false;
        _offlineWaitTimer.Stop();
        _timerWindow?.SetOfflineBadgeVisible(false);

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
            case CommandType.ApplySettings:
                if (command.Settings is not null)
                {
                    _settings = command.Settings;
                    Strings.Apply(_settings.LockScreenLanguage);
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

        HideLockWindows();
        HideTimerWindow();
        UpdateHook();
    }

    private void EnterSession(SessionInfo? info, bool isNewSession)
    {
        if (info is null)
            return;

        _state = info.Type == SessionType.Prepay ? PcState.PrepaySession : PcState.PostpaySession;

        _sessionType = info.Type;
        _sessionBaselineRemaining = info.RemainingSeconds;
        _sessionBaselineElapsed = info.ElapsedSeconds;
        _sessionCurrentCostUzs = info.CurrentCostUzs;
        _sessionMaxPaymentUzs = info.MaxPaymentUzs;
        _sessionStopwatch.Restart();

        if (isNewSession)
        {
            _warnedMinutes1 = false;
            _warnedMinutes2 = false;
            _warnedCost = false;
        }

        HideLockWindows();
        UpdateHook();
        ShowTimerWindow();
        RenderTimer();
    }

    private void EndSession()
    {
        if (_settings.AfterSessionAction == AfterSessionAction.Restart)
            PowerControl.Restart();
        else
            ProcessCloser.CloseAll(_settings.ProcessListCsv);

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
            CheckMinuteWarnings(remaining);
        }
        else
        {
            var elapsed = _sessionBaselineElapsed + elapsedSinceSync;
            _timerWindow.ShowSession(_sessionType, 0, elapsed, _sessionCurrentCostUzs);
            CheckCostWarning();
        }
    }

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

    private void CheckCostWarning()
    {
        if (_warnedCost || _sessionMaxPaymentUzs <= 0)
            return;

        if (_sessionCurrentCostUzs >= _sessionMaxPaymentUzs * 0.9)
        {
            _warnedCost = true;
            ShowWarning(Strings.Get("WarningCostApproaching"));
        }
    }

    private void ShowWarning(string message)
    {
        System.Media.SystemSounds.Exclamation.Play();
        MessageBox.Show(message, _settings.ClubName, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---- offline rule (SPEC section 4) ----

    private void OnConnectionStateChanged(HubConnectionState state)
    {
        if (state == HubConnectionState.Connected)
        {
            _offlineWaitTimer.Stop();
            return;
        }

        RestartOfflineWaitTimer();
    }

    private void RestartOfflineWaitTimer()
    {
        if (_offlineUnlocked || _offlineWaitTimer.IsEnabled)
            return;

        _offlineWaitTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.OfflineUnlockWaitSeconds));
        _offlineWaitTimer.Start();
    }

    private void OnOfflineWaitElapsed()
    {
        _offlineWaitTimer.Stop();

        if (_connection?.State == HubConnectionState.Connected)
            return;

        _offlineUnlocked = true;

        if (_state is PcState.Locked or PcState.LimitReached)
        {
            _state = PcState.OfflineUnlocked;
            HideLockWindows();
            UpdateHook();
        }

        ShowTimerWindow();
        _timerWindow!.SetOfflineBadgeVisible(true);
        UpdateAutoShutdownTimer();
    }

    // ---- auto-shutdown for idle locked PCs (not LimitReached, not mid-session) ----

    private void UpdateAutoShutdownTimer()
    {
        if (_state == PcState.Locked && _settings.AutoShutdownAfterMinutes > 0)
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

    private void UpdateHook()
    {
        var shouldBlock = _state is PcState.Locked or PcState.LimitReached;

        if (shouldBlock && _blockingHook is null)
        {
            _blockingHook = new NativeKeyboardHook(BlockedKeys.IsBlockedCombo);
            _blockingHook.Install();
        }
        else if (!shouldBlock && _blockingHook is not null)
        {
            _blockingHook.Uninstall();
            _blockingHook = null;
        }
    }
}
