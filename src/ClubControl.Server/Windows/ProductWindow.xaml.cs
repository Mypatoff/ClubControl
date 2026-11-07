using System;
using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

/// <summary>Adds a bar product (name, price, starting stock) or edits an existing one (name and price).</summary>
public partial class ProductWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly int? _productId;

    /// <param name="productId">null to add a new product.</param>
    public ProductWindow(IServiceProvider services, int? productId, string? name, int price, int stock)
    {
        InitializeComponent();
        _services = services;
        _productId = productId;

        Title = Strings.Get(productId is null ? "BarAddProduct" : "BarEditProduct");
        NameLabel.Text = Strings.Get("BarProductName");
        PriceLabel.Text = Strings.Get("BarProductPrice");
        StockLabel.Text = Strings.Get("BarProductStart");
        CancelButton.Content = Strings.Get("Cancel");
        SaveButton.Content = Strings.Get("Save");

        NameBox.Text = name ?? "";
        PriceBox.Text = productId is null ? "" : price.ToString();
        StockBox.Text = stock.ToString();
        StockPanel.Visibility = productId is null ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) => NameBox.Focus();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        if (!int.TryParse(PriceBox.Text.Replace(" ", ""), out var price) || !int.TryParse(StockBox.Text.Replace(" ", ""), out var stock))
        {
            ErrorText.Text = Strings.Get("BarEnterNumbers");
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var bar = scope.ServiceProvider.GetRequiredService<BarService>();
            if (_productId is { } id)
                await bar.UpdateProductAsync(id, NameBox.Text, price);
            else
                await bar.CreateProductAsync(NameBox.Text, price, stock);
        }
        catch (InvalidOperationException ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
