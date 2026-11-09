using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
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

        var lines = new List<ItemLineDisplay>();
        var total = 0;

        foreach (var target in _targets)
        {
            var snapshot = await sessionService.GetActiveSessionSnapshotAsync(target.MachineGuid);
            if (snapshot is null)
                continue;

            var items = await sessionService.GetExtrasAsync(target.MachineGuid);
            total += snapshot.AmountOrCostUzs + items.Sum(i => i.AmountUzs);

            // "PC #3: 10 000 UZS", when it started and how long it ran, then one indented line per item.
            lines.Add(new ItemLineDisplay($"{target.Label}: {snapshot.AmountOrCostUzs:N0} UZS", false));
            lines.Add(new ItemLineDisplay("    " + Strings.Format("StopStartedFormat", TimeText.StartedAt(snapshot.StartUtc), TimeText.Duration(DateTime.UtcNow - snapshot.StartUtc)), false));
            lines.AddRange(items.Select(i => new ItemLineDisplay($"    {i.Description}: {i.AmountUzs:N0} UZS", i.IsUnpaid)));
        }

        // Unpaid bar products are in red, so the admin sees what is being collected for them.
        BreakdownText.Inlines.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                BreakdownText.Inlines.Add(new LineBreak());

            var run = new Run(lines[i].Text);
            if (lines[i].IsUnpaid)
            {
                run.SetResourceReference(TextElement.ForegroundProperty, "ErrorBrush");
                run.FontWeight = FontWeights.SemiBold;
            }

            BreakdownText.Inlines.Add(run);
        }

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
