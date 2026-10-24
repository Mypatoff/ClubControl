using System;
using System.Windows;
using System.Windows.Threading;
using ClubControl.Client.Resources;
using ClubControl.Shared.Dto;

namespace ClubControl.Client.Windows;

/// <summary>
/// One fullscreen, borderless, topmost instance per monitor (SPEC section 4). Re-asserts topmost
/// every 2 s because some windows (e.g. Task Manager) can otherwise steal the foreground.
/// </summary>
public partial class LockWindow : Window
{
    private readonly DispatcherTimer _topmostTimer;

    public LockWindow(System.Drawing.Rectangle bounds)
    {
        InitializeComponent();

        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;

        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();
        Loaded += (_, _) => _topmostTimer.Start();
        Closed += (_, _) => _topmostTimer.Stop();
    }

    private void ReassertTopmost()
    {
        Topmost = false;
        Topmost = true;
        Activate();
    }

    public void Render(ClientSettingsDto settings, LockInfo? info)
    {
        ClubNameText.Text = settings.ClubName;

        PcInfoText.Text = info is { PcNumber: int number }
            ? Strings.Format("PcNumberFormat", number) +
              (info.Class is { } pcClass ? $" ({Strings.ClassName(pcClass)})" : "") +
              (info.PricePerHourUzs > 0 ? $" — {Strings.Format("PerHourFormat", info.PricePerHourUzs.ToString("N0"))}" : "")
            : "";

        MessageText.Text = info?.LimitReached == true ? Strings.Get("PleaseGoToAdmin") : "";
        CustomText.Text = settings.LockScreenText;
    }
}
