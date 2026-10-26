using System;
using System.Windows;
using System.Windows.Threading;

namespace ClubControl.Client.Windows;

/// <summary>
/// Small borderless notification at the top of the screen for the minute/cost warnings (SPEC
/// section 4) - closes itself after a few seconds. Replaces the old MessageBox, which looked like
/// an error dialog and needed someone to click OK to go away.
/// </summary>
public partial class ToastWindow : Window
{
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(3);
    private readonly DispatcherTimer _closeTimer;

    public ToastWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + 16;

        _closeTimer = new DispatcherTimer { Interval = VisibleDuration };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Close();
        };
        _closeTimer.Start();
    }
}
