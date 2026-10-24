using System;
using System.Windows;
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
        TargetText.Text = Strings.Format("TargetPcFormat", pc.Number!, pc.Class.HasValue ? Strings.ClassName(pc.Class.Value) : "");
        MoneyRadio.Content = Strings.Get("AddMoney");
        TimeRadio.Content = Strings.Get("AddTime");
        CancelButton.Content = Strings.Get("Cancel");
        ExtendButton.Content = Strings.Get("ButtonExtend");
    }

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
