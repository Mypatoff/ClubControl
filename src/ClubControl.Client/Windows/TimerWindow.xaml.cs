using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ClubControl.Client.Resources;
using ClubControl.Shared;

namespace ClubControl.Client.Windows;

/// <summary>
/// Small faint always-on-top corner label (SPEC section 4). Shows the running session and/or the manual-unlock badge.
/// It is click-through and never takes focus, so mouse clicks and keys go to the game underneath, and it
/// hides itself while a full-screen program (a game) is in front.
/// </summary>
public partial class TimerWindow : Window
{
    public TimerWindow()
    {
        InitializeComponent();

        // The size follows the text, so keep the right edge fixed whenever it changes.
        SizeChanged += (_, _) => PlaceInCorner();

        var watcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        watcher.Tick += (_, _) => Opacity = IsFullScreenProgramInFront() ? 0 : VisibleOpacity;
        Closed += (_, _) => watcher.Stop();
        watcher.Start();
    }

    private const double VisibleOpacity = 0.55;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public int Flags; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);

    /// <summary>True when the window in front covers a whole monitor (a game, a video) and is not the desktop itself.</summary>
    private static bool IsFullScreenProgramInFront()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var window))
            return false;

        var className = new StringBuilder(64);
        GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")
            return false; // the desktop / taskbar can be "full screen" too

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* nearest */), ref info))
            return false;

        return window.Left <= info.Monitor.Left && window.Top <= info.Monitor.Top
            && window.Right >= info.Monitor.Right && window.Bottom >= info.Monitor.Bottom;
    }

    private void PlaceInCorner()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 12;
        Top = workArea.Top + 8;
    }

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;  // mouse clicks pass through to the window below
    private const int WsExToolWindow = 0x80;   // not in Alt+Tab
    private const int WsExNoActivate = 0x08000000; // never takes focus from the game

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    public void ShowSession(SessionType type, int remainingSeconds, int elapsedSeconds, int currentCostUzs)
    {
        if (type == SessionType.Prepay)
        {
            MainText.Text = FormatTime(remainingSeconds);
            SubText.Text = Strings.Get("Remaining");
        }
        else
        {
            MainText.Text = FormatTime(elapsedSeconds);
            SubText.Text = ""; // the client never sees the price of a session
        }
    }

    public void ClearSession()
    {
        MainText.Text = "";
        SubText.Text = "";
    }

    /// <summary>Shows or hides the "Unlocked manually" label (the PC was opened with the unlock hotkey, not by a session).</summary>
    public void SetManualUnlockBadgeVisible(bool visible)
    {
        BadgeText.Text = Strings.Get("ManualUnlockBadge");
        BadgeText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatTime(int totalSeconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes:D2}:{span.Seconds:D2}";
    }
}
