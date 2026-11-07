using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

/// <summary>
/// The bar sales that were given on credit and not paid yet (the list behind the "Unpaid" chip in the
/// main window). Mark one paid when the client pays, or cancel it if it was a mistake.
/// </summary>
public partial class UnpaidWindow : Window
{
    private sealed record UnpaidRow(int Id, string TimeDisplay, string Product, string AmountDisplay, string Where, string Comment, string Summary, int Amount);

    private readonly IServiceProvider _services;

    public UnpaidWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;

        Title = Strings.Get("UnpaidTitle");
        TimeColumn.Header = Strings.Get("BarColTime");
        ProductColumn.Header = Strings.Get("BarColProduct");
        AmountColumn.Header = Strings.Get("BarColAmount");
        WhereColumn.Header = Strings.Get("BarColWhere");
        CommentColumn.Header = Strings.Get("BarColComment");
        PaidButton.Content = Strings.Get("UnpaidMarkPaid");
        VoidButton.Content = Strings.Get("BarVoidButton");
        CloseButton.Content = Strings.Get("Close");
        EmptyText.Text = Strings.Get("UnpaidNone");

        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        using var scope = _services.CreateScope();
        var sales = await scope.ServiceProvider.GetRequiredService<BarService>().GetUnpaidAsync();

        var rows = sales.Select(ToRow).ToList();
        UnpaidGrid.ItemsSource = rows;
        TotalText.Text = Strings.Format("UnpaidTotalFormat", $"{sales.Sum(s => (long)s.TotalUzs):N0}");
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PaidButton.IsEnabled = VoidButton.IsEnabled = rows.Count > 0;
    }

    private static UnpaidRow ToRow(BarSale s) => new(
        s.Id, s.CreatedUtc.ToLocalTime().ToString("dd.MM HH:mm"), BarService.Describe(s), $"{s.TotalUzs:N0} UZS",
        s.PcLabel == "" ? Strings.Get("BarWhereCounter") : s.PcLabel, s.UnpaidComment,
        $"{BarService.Describe(s)} — {s.UnpaidComment}", s.TotalUzs);

    private UnpaidRow? Selected()
    {
        if (UnpaidGrid.SelectedItem is UnpaidRow row)
            return row;

        MessageBox.Show(Strings.Get("BarSelectSale"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private async void OnMarkPaid(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row)
            return;

        if (MessageBox.Show(Strings.Format("UnpaidMarkPaidConfirm", $"{row.Amount:N0}", row.Summary), Strings.Get("ConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RunAsync(bar => bar.MarkPaidAsync(row.Id));
    }

    private async void OnVoid(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row)
            return;

        if (MessageBox.Show(Strings.Format("BarVoidConfirmFormat", row.Summary), Strings.Get("ConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RunAsync(bar => bar.VoidSaleAsync(row.Id));
    }

    private async Task RunAsync(Func<BarService, Task> action)
    {
        try
        {
            using var scope = _services.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<BarService>());
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await ReloadAsync();
    }

    private void OnClose(object sender, RoutedEventArgs e) => DialogResult = true;
}
