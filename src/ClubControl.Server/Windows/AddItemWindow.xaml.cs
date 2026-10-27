using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

/// <summary>
/// Adds something the client bought (name + price) to a running session. The items are listed in
/// the stop / payment dialog and added to the total. The dialog stays open after each item so
/// several can be entered in a row; Close finishes.
/// </summary>
public partial class AddItemWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly PcRow _pc;

    public AddItemWindow(IServiceProvider services, PcRow pc)
    {
        InitializeComponent();
        _services = services;
        _pc = pc;

        Title = Strings.Get("AddItemTitle");
        TargetText.Text = $"{Strings.Get("AddItemTitle")}: {pc.Label}";
        AddedLabel.Text = Strings.Get("ItemsAddedLabel");
        NameLabel.Text = Strings.Get("LabelItemName");
        AmountLabel.Text = Strings.Get("LabelItemPrice");
        CloseButton.Content = Strings.Get("Close");
        AddButton.Content = Strings.Get("ButtonAdd");

        Loaded += async (_, _) =>
        {
            await ReloadItemsAsync();
            NameBox.Focus();
        };
    }

    private async Task ReloadItemsAsync()
    {
        using var scope = _services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
        var items = await sessionService.GetExtrasAsync(_pc.MachineGuid);

        ItemsList.ItemsSource = items.Select(i => new { i.Id, i.Description, AmountDisplay = $"{i.AmountUzs:N0} UZS" }).ToList();
        AddedPanel.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddedTotalText.Text = $"{items.Sum(i => i.AmountUzs):N0} UZS";
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = Strings.Get("EnterItemName");
            return;
        }

        if (!int.TryParse(AmountBox.Text.Replace(" ", ""), out var amount) || amount <= 0)
        {
            ErrorText.Text = Strings.Get("EnterItemPrice");
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SessionService>().AddExtraAsync(_pc.MachineGuid, NameBox.Text, amount);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }

        NameBox.Clear();
        AmountBox.Clear();
        await ReloadItemsAsync();
        NameBox.Focus();
    }

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id })
            return;

        try
        {
            using var scope = _services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SessionService>().RemoveExtraAsync(id);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }

        await ReloadItemsAsync();
    }

    private void OnClose(object sender, RoutedEventArgs e) => DialogResult = true;
}
