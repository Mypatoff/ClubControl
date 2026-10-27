using System;
using System.Threading;
using System.Windows;
using ClubControl.Client.Native;
using ClubControl.Client.Services;

namespace ClubControl.Client;

public partial class App : Application
{
    // "Global\" (not per-session) so this still catches a second copy started under the same kiosk
    // account from a different login session - e.g. RDP or fast user switching, not just a stray
    // double-launch in the same session.
    private const string SingleInstanceMutexName = "Global\\ClubControl.Client.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            if (ex is not null)
                ClientLog.Error("AppDomain.UnhandledException", ex);
            ShowFatalError(ex);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            // Keep running after an unexpected UI-thread error instead of exiting - this runs
            // unattended on a kiosk PC, so staying alive (even degraded) beats dropping the lock
            // screen and keyboard block with nobody there to notice or restart it.
            ClientLog.Error("DispatcherUnhandledException", args.Exception);
            ShowFatalError(args.Exception);
            args.Handled = true;
        };
    }

    private static void ShowFatalError(Exception? ex) =>
        MessageBox.Show(ex?.ToString() ?? "Unknown error.", "ClubControl Client - unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out _ownsSingleInstanceMutex);
        if (!_ownsSingleInstanceMutex)
        {
            // A second copy would fight the running one for the lock windows, the keyboard hook and
            // the unlock prompt, and only one of them ever gets commands from the server (see
            // ConnectionRegistry on the server side) - there is nothing useful for it to do. Exit
            // quietly; the first instance already owns the lock screen.
            Shutdown();
            return;
        }

        // There is no visible UI by design (SPEC: the lock screen is the only UI) - closing or
        // losing the bootstrap window must never end the process. Without this, WPF's default
        // ShutdownMode (OnLastWindowClose) would exit the whole app the moment that window closed
        // for any reason, taking the lock screen and keyboard block down with it.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        MainWindow = new MainWindow();

#if DEBUG
        // Test-only escape hatch: the lock screen blocks Alt+F4 and friends, so without this a
        // tester can get stuck behind it. Ctrl+Shift+F11 just exits the client; never built into Release.
        // (Ctrl+Shift+F12 is the real unlock-password hotkey, see UnlockHotkey.)
        _debugExitHook = new NativeKeyboardHook(vkCode =>
        {
            if (vkCode == VkF11 && NativeKeyboardHook.IsControlDown && NativeKeyboardHook.IsShiftDown)
                Dispatcher.Invoke(Shutdown);

            return false; // never swallow the key - this hook only watches
        });
        _debugExitHook.Install();
#endif
    }

#if DEBUG
    private const int VkF11 = 0x7A;
    private NativeKeyboardHook? _debugExitHook;
#endif

    protected override void OnExit(ExitEventArgs e)
    {
#if DEBUG
        _debugExitHook?.Dispose();
#endif
        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex!.ReleaseMutex();

        base.OnExit(e);
    }
}
