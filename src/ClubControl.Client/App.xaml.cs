using System;
using System.Windows;
using ClubControl.Client.Native;

namespace ClubControl.Client;

public partial class App : Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ShowFatalError(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            // Keep running after an unexpected UI-thread error instead of exiting - this runs
            // unattended on a kiosk PC, so staying alive (even degraded) beats dropping the lock
            // screen and keyboard block with nobody there to notice or restart it.
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

    protected override void OnExit(ExitEventArgs e)
    {
        _debugExitHook?.Dispose();
        base.OnExit(e);
    }
#endif
}
