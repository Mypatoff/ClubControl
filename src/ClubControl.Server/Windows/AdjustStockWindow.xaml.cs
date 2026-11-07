using System;
using System.Linq;
using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Server.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

/// <summary>Adds or removes stock by hand (a delivery, a recount, something broken), with a reason that goes into the stock log.</summary>
public partial class AdjustStockWindow : Window
{
    private sealed record ReasonItem(string Key, string Label);

    private readonly IServiceProvider _services;
    private readonly int _productId;

    public AdjustStockWindow(IServiceProvider services, int productId, string productName)
    {
        InitializeComponent();
        _services = services;
        _productId = productId;

        Title = Strings.Get("BarAdjustStock");
        TitleText.Text = Strings.Format("BarAdjustFor", productName);
        DeltaLabel.Text = Strings.Get("BarAdjustDelta");
        ReasonLabel.Text = Strings.Get("BarAdjustReason");
        NoteLabel.Text = Strings.Get("BarAdjustNote");
        CancelButton.Content = Strings.Get("Cancel");
        SaveButton.Content = Strings.Get("Save");

        var reasons = BarService.AdjustReasons.Select(r => new ReasonItem(r, BarView.ReasonName(r))).ToList();
        ReasonBox.ItemsSource = reasons;
        ReasonBox.SelectedItem = reasons[0];

        Loaded += (_, _) => DeltaBox.Focus();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        if (!int.TryParse(DeltaBox.Text.Replace(" ", ""), out var delta))
        {
            ErrorText.Text = Strings.Get("BarAdjustDeltaInvalid");
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var reason = (ReasonBox.SelectedItem as ReasonItem)?.Key ?? "other";
            await scope.ServiceProvider.GetRequiredService<BarService>().AdjustStockAsync(_productId, delta, reason, NoteBox.Text);
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
