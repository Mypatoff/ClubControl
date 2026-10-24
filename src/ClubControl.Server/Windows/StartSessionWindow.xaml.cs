using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

/// <summary>
/// Starts a prepay/postpay session - on one PC, or on several at once as long as they all share
/// the same tariff (SPEC extension: bulk-start, e.g. one group booking several PCs together).
/// </summary>
public partial class StartSessionWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly List<PcRow> _targets;

    public StartSessionWindow(IServiceProvider services, List<PcRow> targets)
    {
        InitializeComponent();
        _services = services;
        _targets = targets;

        Title = Strings.Get("StartSessionTitle");
        TargetText.Text = TargetHeader.Build(targets);
        PrepayMoneyRadio.Content = Strings.Get("PrepayByMoney");
        PrepayTimeRadio.Content = Strings.Get("PrepayByTime");
        PostpayRadio.Content = Strings.Get("Postpay");
        CancelButton.Content = Strings.Get("Cancel");
        StartButton.Content = Strings.Get("ButtonStart");
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        using var scope = _services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
        var failures = new List<string>();

        foreach (var target in _targets)
        {
            try
            {
                if (PrepayMoneyRadio.IsChecked == true)
                    await sessionService.StartPrepayByMoneyAsync(target.MachineGuid, ParseInt(MoneyBox.Text));
                else if (PrepayTimeRadio.IsChecked == true)
                    await sessionService.StartPrepayByTimeAsync(target.MachineGuid, ParseInt(MinutesBox.Text));
                else
                    await sessionService.StartPostpayAsync(target.MachineGuid);
            }
            catch (Exception ex)
            {
                failures.Add($"#{target.Number}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
            MessageBox.Show(string.Join("\n", failures), Strings.Get("SomeFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);

        if (failures.Count < _targets.Count)
            DialogResult = true;
    }

    private static int ParseInt(string text) => int.TryParse(text, out var value) ? value : 0;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
