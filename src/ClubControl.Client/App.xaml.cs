using System;
using System.Windows;
using ClubControl.Client.Native;

namespace ClubControl.Client;

public partial class App : Application
{
    public App()
    {
        // Without this, an unhandled exception on any thread just silently kills the process -
        // no dialog, nothing to go on. Show what happened instead.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ShowFatalError(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            ShowFatalError(args.Exception);
            args.Handled = true;
            Shutdown();
        };
    }

    private static void ShowFatalError(Exception? ex) =>
        MessageBox.Show(ex?.ToString() ?? "Unknown error.", "ClubControl Client - unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);

#if DEBUG
    // Test-only escape hatch: the lock screen blocks Alt+F4 and friends, so without this a tester
    // can get stuck behind it. Ctrl+Shift+F12 just closes the client; it is never built into Release.
    private const int VkF12 = 0x7B;
    private NativeKeyboardHook? _debugExitHook;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _debugExitHook = new NativeKeyboardHook(vkCode =>
        {
            if (vkCode == VkF12 && NativeKeyboardHook.IsControlDown && NativeKeyboardHook.IsShiftDown)
                Dispatcher.Invoke(Shutdown);

            return false; // never swallow the key - this hook only watches
        });
        _debugExitHook.Install();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _debugExitHook?.Dispose();
        base.OnExit(e);
    }
#endif
}
