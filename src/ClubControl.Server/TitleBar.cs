using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClubControl.Server;

/// <summary>
/// Makes the window title bar follow the app theme. Windows draws the title bar itself, so the dark theme
/// otherwise has a bright white strip on top; DWM can be told to draw it dark (Windows 10 2004 and
/// later), and on Windows 11 even in the exact colour of the page. On a Windows that does not know these
/// settings the calls simply fail and the normal title bar stays - nothing else changes.
/// </summary>
public static class TitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int UseImmersiveDarkModeOld = 19; // Windows 10 1903/1909
    private const int UseImmersiveDarkMode = 20;    // Windows 10 2004 and later, Windows 11
    private const int CaptionColor = 35;            // Windows 11
    private const int TextColor = 36;               // Windows 11

    /// <summary>Every window of the app gets the title bar of the current theme as soon as it is shown.</summary>
    public static void InstallForAllWindows() =>
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) =>
        {
            // Loaded also bubbles up from the controls inside the window; only the window itself is of interest.
            if (sender is Window window && ReferenceEquals(e.OriginalSource, window))
                Apply(window);
        }));

    public static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            var dark = ThemeManager.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeOld, ref dark, sizeof(int));

            if (window.TryFindResource("PageBackgroundBrush") is SolidColorBrush page)
            {
                var caption = ToColorRef(page.Color);
                DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(int));
            }

            if (window.TryFindResource("TextPrimaryBrush") is SolidColorBrush text)
            {
                var textColor = ToColorRef(text.Color);
                DwmSetWindowAttribute(handle, TextColor, ref textColor, sizeof(int));
            }
        }
        catch (Exception)
        {
            // Only cosmetic: never let the title bar stop a window from opening.
        }
    }

    /// <summary>Windows wants colours as 0x00BBGGRR.</summary>
    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);
}
