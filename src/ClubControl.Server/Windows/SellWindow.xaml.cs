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
/// A quick counter sale from the main page, not connected to any PC (the PC version is Add item > From the
/// bar). Paid now, or unpaid with a comment (it then waits in the Unpaid list). The window stays open after each
/// sale so several can be entered in a row; Close finishes. All rules live in <see cref="BarService"/>.
/// </summary>
public partial class SellWindow : Window
{
    private sealed record ProductItem(int Id, string Name, int Price, int Stock)
    {
        public string Display => Strings.Format("BarProductLine", Name, $"{Price:N0}", Stock);
    }

    private readonly IServiceProvider _services;
    private int _cupPrice;

    public SellWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;

        Title = Strings.Get("BarSellTitle");
        TitleText.Text = $"{Strings.Get("BarSellTitle")}: {Strings.Get("BarSellToCounter")}";
        ProductLabel.Text = Strings.Get("BarColProduct");
        QuantityLabel.Text = Strings.Get("BarQuantity");
        CupsLabel.Text = Strings.Get("BarCups");
        UnpaidCheck.Content = Strings.Get("BarUnpaid");
        CommentLabel.Text = Strings.Get("BarUnpaidComment");
        CloseButton.Content = Strings.Get("Close");
        SellButton.Content = Strings.Get("BarSellButton");

        Loaded += async (_, _) =>
        {
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
        UpdateTotal();
    }

    private void OnInputChanged(object sender, SelectionChangedEventArgs e) => UpdateTotal();

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateTotal();

    private void OnUnpaidToggled(object sender, RoutedEventArgs e)
    {
        CommentPanel.Visibility = UnpaidCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (UnpaidCheck.IsChecked == true)
            CommentBox.Focus();
    }

    private void UpdateTotal()
    {
        // Also runs while the window is being built, before every box exists.
        if (TotalText is null || QuantityBox is null || CupsBox is null)
            return;

        var quantity = int.TryParse(QuantityBox.Text, out var q) ? Math.Max(q, 0) : 0;
        var cups = int.TryParse(CupsBox.Text, out var c) ? Math.Max(c, 0) : 0;
        var total = ProductBox.SelectedItem is ProductItem p ? (long)p.Price * quantity + (long)cups * _cupPrice : 0;
        TotalText.Text = Strings.Format("BarTotalFormat", $"{total:N0}");
    }

    private async void OnSell(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        ResultText.Text = "";

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
                product.Id, quantity, cups, null, UnpaidCheck.IsChecked == true, CommentBox.Text));
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
        await LoadProductsAsync();
        ProductBox.Focus();
    }

    private void OnClose(object sender, RoutedEventArgs e) => DialogResult = true;
}
