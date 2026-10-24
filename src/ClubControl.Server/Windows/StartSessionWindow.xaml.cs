using System;
using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

public partial class StartSessionWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly PcRow _pc;

    public StartSessionWindow(IServiceProvider services, PcRow pc)
    {
        InitializeComponent();
        _services = services;
        _pc = pc;

        Title = Strings.Get("StartSessionTitle");
        TargetText.Text = Strings.Format("TargetPcFormat", pc.Number!, pc.Class.HasValue ? Strings.ClassName(pc.Class.Value) : "");
        PrepayMoneyRadio.Content = Strings.Get("PrepayByMoney");
        PrepayTimeRadio.Content = Strings.Get("PrepayByTime");
        PostpayRadio.Content = Strings.Get("Postpay");
        FreeRadio.Content = Strings.Get("FreeSession");
        CancelButton.Content = Strings.Get("Cancel");
        StartButton.Content = Strings.Get("ButtonStart");
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        try
        {
            using var scope = _services.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();

            if (PrepayMoneyRadio.IsChecked == true)
                await sessionService.StartPrepayByMoneyAsync(_pc.MachineGuid, ParseInt(MoneyBox.Text));
            else if (PrepayTimeRadio.IsChecked == true)
                await sessionService.StartPrepayByTimeAsync(_pc.MachineGuid, ParseInt(MinutesBox.Text));
            else if (PostpayRadio.IsChecked == true)
                await sessionService.StartPostpayAsync(_pc.MachineGuid);
            else
                await sessionService.StartFreeAsync(_pc.MachineGuid, string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text);

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
