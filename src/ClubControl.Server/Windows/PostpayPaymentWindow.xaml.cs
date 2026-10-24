using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

/// <summary>
/// "Admin ends it -> payment window shows amount due -> admin clicks Paid" (SPEC section 5/7) -
/// on one PC, or several at once (e.g. a group finishing together): shows each target's cost plus
/// the combined total before collecting payment for all of them in one click.
/// </summary>
public partial class PostpayPaymentWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly List<PcRow> _targets;

    public PostpayPaymentWindow(IServiceProvider services, List<PcRow> targets)
    {
        InitializeComponent();
        _services = services;
        _targets = targets;

        Title = Strings.Get("PaymentTitle");
        PcText.Text = TargetHeader.Build(targets);
        AmountDueLabel.Text = Strings.Get("AmountDueLabel");
        CancelButton.Content = Strings.Get("Cancel");
        PaidButton.Content = Strings.Get("ButtonPaid");

        Loaded += async (_, _) => await LoadAmountsDueAsync();
    }

    private async Task LoadAmountsDueAsync()
    {
        using var scope = _services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

        var lines = new List<string>();
        var total = 0;

        foreach (var target in _targets)
        {
            var snapshot = await sessionService.GetActiveSessionSnapshotAsync(target.MachineGuid);
            if (snapshot is null)
                continue;

            total += snapshot.AmountOrCostUzs;
            if (_targets.Count > 1)
                lines.Add($"{Strings.Format("PcNumberFormat", target.Number!)}: {snapshot.AmountOrCostUzs:N0} UZS");
        }

        BreakdownText.Text = string.Join("\n", lines);
        AmountText.Text = $"{total:N0} UZS";
        PaidButton.IsEnabled = true;
    }

    private async void OnPaid(object sender, RoutedEventArgs e)
    {
        using var scope = _services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
        var failures = new List<string>();

        foreach (var target in _targets)
        {
            try
            {
                await sessionService.EndSessionEarlyAsync(target.MachineGuid);
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

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
