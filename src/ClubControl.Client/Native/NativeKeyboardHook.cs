using System;
using System.Runtime.InteropServices;

namespace ClubControl.Client.Native;

/// <summary>
/// Thin wrapper around a WH_KEYBOARD_LL global hook. The caller's <see cref="_shouldBlock"/>
/// delegate decides, per key-down, whether the key is swallowed (return true) or passed through.
/// </summary>
public sealed class NativeKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12; // Alt

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsControlDown => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
    public static bool IsShiftDown => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
    public static bool IsAltDown => (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

    // Kept alive for the hook's lifetime - the hook would stop firing (or crash) if this delegate got GC'd.
    private readonly LowLevelKeyboardProc _proc;
    private readonly Func<int, bool> _shouldBlock;
    private IntPtr _hookId = IntPtr.Zero;

    /// <param name="shouldBlock">Given a virtual-key code, return true to swallow the key press.</param>
    public NativeKeyboardHook(Func<int, bool> shouldBlock)
    {
        _shouldBlock = shouldBlock;
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero)
            return;

        using var currentModule = System.Diagnostics.Process.GetCurrentProcess().MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(currentModule.ModuleName), 0);
    }

    public void Uninstall()
    {
        if (_hookId == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var vkCode = Marshal.ReadInt32(lParam);
            if (_shouldBlock(vkCode))
                return (IntPtr)1;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
