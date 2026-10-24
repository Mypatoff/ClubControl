using System;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

/// <summary>"Admin ends it -> payment window shows amount due -> admin clicks Paid" (SPEC section 5/7).</summary>
public partial class PostpayPaymentWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly PcRow _pc;

    public PostpayPaymentWindow(IServiceProvider services, PcRow pc)
    {
        InitializeComponent();
        _services = services;
        _pc = pc;

        Title = Strings.Get("PaymentTitle");
        PcText.Text = Strings.Format("TargetPcFormat", pc.Number!, pc.TariffDisplay);
        AmountDueLabel.Text = Strings.Get("AmountDueLabel");
        CancelButton.Content = Strings.Get("Cancel");
        PaidButton.Content = Strings.Get("ButtonPaid");

        Loaded += async (_, _) => await LoadAmountDueAsync();
    }

    private async Task LoadAmountDueAsync()
    {
        using var scope = _services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
        var snapshot = await sessionService.GetActiveSessionSnapshotAsync(_pc.MachineGuid);

        if (snapshot is null)
        {
            ErrorText.Text = Strings.Get("NoActiveSessionFound");
            return;
        }

        AmountText.Text = $"{snapshot.AmountOrCostUzs:N0} UZS";
        PaidButton.IsEnabled = true;
    }

    private async void OnPaid(object sender, RoutedEventArgs e)
    {
        try
        {
            using var scope = _services.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
            await sessionService.EndSessionEarlyAsync(_pc.MachineGuid);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
