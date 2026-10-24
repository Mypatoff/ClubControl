using System;
using System.Windows;
using System.Windows.Input;
using ClubControl.Client.Resources;
using ClubControl.Shared;

namespace ClubControl.Client.Windows;

/// <summary>Small movable always-on-top corner window (SPEC section 4). Shows the running session and/or the offline badge.</summary>
public partial class TimerWindow : Window
{
    public TimerWindow()
    {
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Top + 16;
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

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
            SubText.Text = currentCostUzs > 0 ? $"{currentCostUzs:N0} {Strings.Get("CurrencyUzs")}" : "";
        }
    }

    public void ClearSession()
    {
        MainText.Text = "";
        SubText.Text = "";
    }

    public void SetOfflineBadgeVisible(bool visible)
    {
        OfflineText.Text = Strings.Get("OfflineMode");
        OfflineText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatTime(int totalSeconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes:D2}:{span.Seconds:D2}";
    }
}
