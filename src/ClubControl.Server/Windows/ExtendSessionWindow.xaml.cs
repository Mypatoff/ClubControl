using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

public partial class ExtendSessionWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly PcRow _pc;

    public ExtendSessionWindow(IServiceProvider services, PcRow pc)
    {
        InitializeComponent();
        _services = services;
        _pc = pc;

        Title = Strings.Get("ExtendSessionTitle");
        TargetText.Text = $"{pc.Label} ({pc.TariffDisplay})";
        MoneyRadio.Content = Strings.Get("AddMoney");
        TimeRadio.Content = Strings.Get("AddTime");
        CancelButton.Content = Strings.Get("Cancel");
        ExtendButton.Content = Strings.Get("ButtonExtend");

        // Clicking anywhere on an option (or typing in its box) picks that option, which is outlined in the accent colour.
        MoneyCard.MouseLeftButtonUp += (_, _) => { MoneyRadio.IsChecked = true; MoneyBox.Focus(); };
        TimeCard.MouseLeftButtonUp += (_, _) => { TimeRadio.IsChecked = true; MinutesBox.Focus(); };
        MoneyBox.GotFocus += (_, _) => MoneyRadio.IsChecked = true;
        MinutesBox.GotFocus += (_, _) => TimeRadio.IsChecked = true;
        Loaded += (_, _) => MoneyBox.Focus();
        foreach (var radio in new[] { MoneyRadio, TimeRadio })
        {
            radio.Checked += (_, _) => UpdateOutlines();
            radio.Unchecked += (_, _) => UpdateOutlines();
        }

        UpdateOutlines();
    }

    private void UpdateOutlines()
    {
        MoneyCard.BorderBrush = OutlineFor(MoneyRadio);
        TimeCard.BorderBrush = OutlineFor(TimeRadio);
    }

    private Brush OutlineFor(RadioButton radio) => (Brush)FindResource(radio.IsChecked == true ? "AccentBrush" : "DividerBrush");

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        try
        {
            using var scope = _services.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

            if (MoneyRadio.IsChecked == true)
                await sessionService.ExtendPrepayByMoneyAsync(_pc.MachineGuid, ParseInt(MoneyBox.Text));
            else
                await sessionService.ExtendPrepayByTimeAsync(_pc.MachineGuid, ParseInt(MinutesBox.Text));

            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private static int ParseInt(string text) => int.TryParse(text, out var value) ? value : 0;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
