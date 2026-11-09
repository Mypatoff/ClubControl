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
/// Adds something the client bought to a running session. Either a bar product (it is also a bar
/// sale: stock goes down; paid at once it stays off the bill, marked unpaid it is added to the bill
/// in red and paid when the session is stopped) or anything else as a name + price. Items on the bill
/// are listed in the stop / payment dialog and added to the total. The dialog
/// stays open after each item so several can be entered in a row; Close finishes.
/// </summary>
public partial class AddItemWindow : Window
{
    private sealed record ProductItem(int Id, string Name, int Price, int Stock)
    {
        public string Display => Strings.Format("BarProductLine", Name, $"{Price:N0}", Stock);
    }

    private readonly IServiceProvider _services;
    private readonly PcRow _pc;
    private int _cupPrice;

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

        BarTab.Header = Strings.Get("AddItemFromBar");
        OtherTab.Header = Strings.Get("AddItemOther");
        ProductLabel.Text = Strings.Get("BarColProduct");
        QuantityLabel.Text = Strings.Get("BarQuantity");
        CupsLabel.Text = Strings.Get("BarCups");
        UnpaidCheck.Content = Strings.Get("BarUnpaid");
        CommentLabel.Text = Strings.Get("BarUnpaidComment");

        Loaded += async (_, _) =>
        {
            await ReloadItemsAsync();
            await LoadProductsAsync();
            ProductBox.Focus();
        };
    }

    /// <summary>The products that can be sold now, best sellers first.</summary>
    private async Task LoadProductsAsync()
    {
        using var scope = _services.CreateScope();
        var bar = scope.ServiceProvider.GetRequiredService<BarService>();
        var products = await bar.ListProductsAsync(popularFirst: true);
        _cupPrice = await bar.GetCupPriceAsync();

        var chosen = (ProductBox.SelectedItem as ProductItem)?.Id;
        var items = products.Where(p => p.Stock > 0).Select(p => new ProductItem(p.Id, p.Name, p.PriceUzs, p.Stock)).ToList();
        ProductBox.ItemsSource = items;
        ProductBox.SelectedItem = items.FirstOrDefault(i => i.Id == chosen);
        UpdateBarTotal();
    }

    private void OnBarInputChanged(object sender, SelectionChangedEventArgs e) => UpdateBarTotal();

    private void OnBarTextChanged(object sender, TextChangedEventArgs e) => UpdateBarTotal();

    private void OnUnpaidToggled(object sender, RoutedEventArgs e)
    {
        CommentPanel.Visibility = UnpaidCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (UnpaidCheck.IsChecked == true)
            CommentBox.Focus();
    }

    private void UpdateBarTotal()
    {
        // Also runs while the window is being built, before every box exists.
        if (BarTotalText is null || QuantityBox is null || CupsBox is null)
            return;

        var quantity = int.TryParse(QuantityBox.Text, out var q) ? Math.Max(q, 0) : 0;
        var cups = int.TryParse(CupsBox.Text, out var c) ? Math.Max(c, 0) : 0;
        var total = ProductBox.SelectedItem is ProductItem p ? (long)p.Price * quantity + (long)cups * _cupPrice : 0;
        BarTotalText.Text = Strings.Format("BarTotalFormat", $"{total:N0}");
    }

    private async Task ReloadItemsAsync()
    {
        using var scope = _services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
        var items = await sessionService.GetExtrasAsync(_pc.MachineGuid);

        ItemsList.ItemsSource = items.Select(i => new { i.Id, i.Description, i.IsUnpaid, AmountDisplay = $"{i.AmountUzs:N0} UZS" }).ToList();
        AddedPanel.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddedTotalText.Text = $"{items.Sum(i => i.AmountUzs):N0} UZS";
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        ResultText.Text = "";

        if (SourceTabs.SelectedItem == BarTab)
        {
            await AddBarProductAsync();
            return;
        }

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

    private async Task AddBarProductAsync()
    {
        if (ProductBox.SelectedItem is not ProductItem product)
        {
            ErrorText.Text = Strings.Get("BarChooseProduct");
            return;
        }

        if (!int.TryParse(QuantityBox.Text, out var quantity) || !int.TryParse(CupsBox.Text, out var cups))
        {
            ErrorText.Text = Strings.Get("BarEnterNumbers");
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BarService>().SellAsync(new BarService.SaleRequest(
                product.Id, quantity, cups, _pc.MachineGuid, UnpaidCheck.IsChecked == true, CommentBox.Text));
        }
        catch (InvalidOperationException ex)
        {
            ErrorText.Text = ex.Message;
            await LoadProductsAsync(); // the stock may have changed
            return;
        }

        ResultText.Text = Strings.Format("BarSoldFormat", quantity, product.Name, product.Stock - quantity);
        QuantityBox.Text = "1";
        CupsBox.Text = "0";
        UnpaidCheck.IsChecked = false;
        CommentBox.Clear();
        await ReloadItemsAsync();
        await LoadProductsAsync();
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
        await LoadProductsAsync();
    }

    private void OnClose(object sender, RoutedEventArgs e) => DialogResult = true;
}
