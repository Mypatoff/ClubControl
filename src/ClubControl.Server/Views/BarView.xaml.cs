using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Server.Windows;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace ClubControl.Server.Views;

/// <summary>
/// The second main page: sell from the bar (best sellers first), and manage products, sales and the stock log.
/// All rules live in <see cref="BarService"/>; this class only reads the boxes, calls it and shows the result.
/// </summary>
public partial class BarView : UserControl
{
    // ---- what the grids show ----

    private sealed record ProductRow(int Id, string Name, int Price, int Stock, bool Archived)
    {
        public string PriceDisplay => $"{Price:N0}";
        public string NameDisplay => Archived ? $"{Name} ({Strings.Get("BarArchivedMark")})" : Name;
        public bool IsAvailable => !Archived && Stock > 0;
    }

    private sealed record SaleRow(int Id, string TimeDisplay, string Product, int Quantity, int Cups, string AmountDisplay,
        string Where, string Status, string Comment, bool IsCancelled, bool IsOutstanding, string Summary);

    private sealed record UnpaidRow(int Id, string TimeDisplay, string Product, string AmountDisplay, string Where, string Comment, int Amount, string Summary);

    private sealed record LogRow(string TimeDisplay, string Product, string TypeDisplay, string ChangeDisplay, int StockAfter, string ReasonDisplay, string Note);

    private sealed record SellTarget(string Label, Guid? MachineGuid);

    /// <summary>The sales tab shows only the newest sales, so it stays short; everything is still in the stock log and the reports.</summary>
    private const int SalesShown = 30;

    private IServiceProvider Services => ((App)Application.Current).Services!;

    private List<ProductRow> _sellable = new();
    private ProductRow? _selected;
    private int _cupPrice;

    public BarView()
    {
        InitializeComponent();
        ApplyLocalization();
        UpdateTotal();

        // The page is only refreshed while it is shown: on opening it, and after each thing done on it.
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true)
                await RefreshAsync();
        };
    }

    private void ApplyLocalization()
    {
        SellTitleText.Text = Strings.Get("BarSellTitle");
        SearchLabel.Text = Strings.Get("BarSearchLabel");
        QuantityLabel.Text = Strings.Get("BarQuantity");
        CupsLabel.Text = Strings.Get("BarCups");
        SellToLabel.Text = Strings.Get("BarSellTo");
        UnpaidCheck.Content = Strings.Get("BarUnpaid");
        CommentLabel.Text = Strings.Get("BarUnpaidComment");
        SellButton.Content = Strings.Get("BarSellButton");
        SelectedText.Text = Strings.Get("BarChooseProduct");

        ListNameColumn.Header = Strings.Get("BarColProduct");
        ListPriceColumn.Header = Strings.Get("BarColPrice");
        ListStockColumn.Header = Strings.Get("BarColStock");

        SalesTab.Header = Strings.Get("BarTabSales");
        ShowUnpaidTabHeader(0);
        ProductsTab.Header = Strings.Get("BarTabProducts");
        StockLogTab.Header = Strings.Get("BarTabStockLog");

        SaleTimeColumn.Header = Strings.Get("BarColTime");
        SaleProductColumn.Header = Strings.Get("BarColProduct");
        SaleQtyColumn.Header = Strings.Get("BarColQty");
        SaleCupsColumn.Header = Strings.Get("BarColCups");
        SaleAmountColumn.Header = Strings.Get("BarColAmount");
        SaleWhereColumn.Header = Strings.Get("BarColWhere");
        SaleStatusColumn.Header = Strings.Get("BarColStatus");
        SaleCommentColumn.Header = Strings.Get("BarColComment");
        VoidButton.Content = Strings.Get("BarVoidButton");

        UnpaidTimeColumn.Header = Strings.Get("BarColTime");
        UnpaidProductColumn.Header = Strings.Get("BarColProduct");
        UnpaidAmountColumn.Header = Strings.Get("BarColAmount");
        UnpaidWhereColumn.Header = Strings.Get("BarColWhere");
        UnpaidCommentColumn.Header = Strings.Get("BarColComment");
        UnpaidPaidButton.Content = Strings.Get("UnpaidMarkPaid");
        UnpaidVoidButton.Content = Strings.Get("BarVoidButton");
        UnpaidEmptyText.Text = Strings.Get("UnpaidNone");

        AddProductButton.Content = "＋ " + Strings.Get("BarAddProduct");
        EditProductButton.Content = Strings.Get("BarEditProduct");
        AdjustStockButton.Content = Strings.Get("BarAdjustStock");
        ArchiveButton.Content = Strings.Get("BarArchive") + " / " + Strings.Get("BarUnarchive");
        ShowArchivedCheck.Content = Strings.Get("BarShowArchived");
        ProductNameColumn.Header = Strings.Get("BarProductName");
        ProductPriceColumn.Header = Strings.Get("BarColPrice");
        ProductStockColumn.Header = Strings.Get("BarColStock");
        CupPriceLabel.Text = Strings.Get("BarCupPriceLabel");
        CupPriceSaveButton.Content = Strings.Get("Save");
        ImportButton.Content = Strings.Get("BarImportButton");

        LogTimeColumn.Header = Strings.Get("BarColTime");
        LogProductColumn.Header = Strings.Get("BarColProduct");
        LogTypeColumn.Header = Strings.Get("BarColType");
        LogChangeColumn.Header = Strings.Get("BarColChange");
        LogAfterColumn.Header = Strings.Get("BarColAfter");
        LogReasonColumn.Header = Strings.Get("BarColReason");
        LogNoteColumn.Header = Strings.Get("BarColNote");
    }

    // ---- loading ----

    /// <summary>Reloads everything the page shows. Called when the page is opened, after each action, and when a sale was made elsewhere.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            var bar = scope.ServiceProvider.GetRequiredService<BarService>();
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();

            _cupPrice = await bar.GetCupPriceAsync();

            // Sell list: best sellers first. Management list: A-Z, optionally with the archived ones.
            _sellable = (await bar.ListProductsAsync(popularFirst: true)).Select(ToProductRow).ToList();
            ApplySearch();

            var all = await bar.ListProductsAsync(includeArchived: ShowArchivedCheck.IsChecked == true);
            ProductsGrid.ItemsSource = all.Select(ToProductRow).ToList();

            SalesGrid.ItemsSource = (await bar.GetRecentSalesAsync(SalesShown)).Select(ToSaleRow).ToList();

            var unpaid = await bar.GetUnpaidAsync();
            var selectedUnpaid = (UnpaidGrid.SelectedItem as UnpaidRow)?.Id;
            var unpaidRows = unpaid.Select(ToUnpaidRow).ToList();
            UnpaidGrid.ItemsSource = unpaidRows;
            UnpaidGrid.SelectedItem = unpaidRows.FirstOrDefault(r => r.Id == selectedUnpaid);
            UnpaidTotalText.Text = unpaid.Count == 0 ? "" : Strings.Format("UnpaidTotalFormat", $"{unpaid.Sum(s => (long)s.TotalUzs):N0}");
            UnpaidEmptyText.Visibility = unpaid.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UnpaidPaidButton.IsEnabled = UnpaidVoidButton.IsEnabled = unpaid.Count > 0;
            ShowUnpaidTabHeader(unpaid.Count);
            StockLogGrid.ItemsSource = (await bar.GetStockLogAsync()).Select(ToLogRow).ToList();

            if (!CupPriceBox.IsKeyboardFocusWithin)
                CupPriceBox.Text = _cupPrice.ToString();

            await LoadSellTargetsAsync(db);
            UpdateTotal();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, error: true);
        }
    }

    private async Task LoadSellTargetsAsync(ClubDbContext db)
    {
        var chosen = (SellToBox.SelectedItem as SellTarget)?.MachineGuid;

        var running = await db.Sessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Active)
            .Select(s => s.Pc!)
            .Distinct()
            .ToListAsync();

        var targets = new List<SellTarget> { new(Strings.Get("BarSellToCounter"), null) };
        targets.AddRange(running
            .OrderBy(p => p.Number)
            .Select(p => new SellTarget(Strings.ResourceLabel(p.Kind, p.Name, p.Number), p.MachineGuid)));

        SellToBox.ItemsSource = targets;
        SellToBox.SelectedItem = targets.FirstOrDefault(t => t.MachineGuid == chosen) ?? targets[0];
    }

    private static ProductRow ToProductRow(BarProduct p) => new(p.Id, p.Name, p.PriceUzs, p.Stock, p.Archived);

    private static string Money(int uzs) => $"{uzs:N0} UZS";

    private static string StatusOf(BarSale s) =>
        s.IsVoided ? Strings.Get("BarStatusCancelled")
        : s.IsOutstanding ? Strings.Get("BarStatusUnpaid")
        : s.IsUnpaid ? Strings.Get("BarStatusPaidLater")
        : Strings.Get("BarStatusPaid");

    private static SaleRow ToSaleRow(BarSale s) => new(
        s.Id, s.CreatedUtc.ToLocalTime().ToString("dd.MM HH:mm"), s.ProductName, s.Quantity, s.Cups, Money(s.TotalUzs),
        s.PcLabel == "" ? Strings.Get("BarWhereCounter") : s.PcLabel, StatusOf(s), s.UnpaidComment,
        s.IsVoided, s.IsOutstanding, $"{BarService.Describe(s)} — {Money(s.TotalUzs)}");

    private static UnpaidRow ToUnpaidRow(BarSale s) => new(
        s.Id, s.CreatedUtc.ToLocalTime().ToString("dd.MM HH:mm"), BarService.Describe(s), Money(s.TotalUzs),
        s.PcLabel == "" ? Strings.Get("BarWhereCounter") : s.PcLabel, s.UnpaidComment, s.TotalUzs, $"{BarService.Describe(s)} — {s.UnpaidComment}");

    /// <summary>The tab is named "UNPAID (n)" and turns red while something is owed, so it is hard to forget.</summary>
    private void ShowUnpaidTabHeader(int count)
    {
        UnpaidTab.Header = new TextBlock
        {
            Text = count > 0 ? Strings.Format("BarTabUnpaidFormat", count) : Strings.Get("BarTabUnpaid"),
            FontWeight = count > 0 ? FontWeights.Bold : FontWeights.Normal,
            Foreground = count > 0 ? (Brush)FindResource("ErrorBrush") : (Brush)FindResource("TextPrimaryBrush")
        };
    }

    private static LogRow ToLogRow(BarService.StockLogLine l) => new(
        l.CreatedUtc.ToLocalTime().ToString("dd.MM HH:mm"), l.ProductName, TypeName(l.Type), l.Delta > 0 ? $"+{l.Delta}" : l.Delta.ToString(),
        l.StockAfter, ReasonName(l.Reason), l.Note);

    private static string TypeName(BarStockMovementType type) => type switch
    {
        BarStockMovementType.Initial => Strings.Get("BarMoveInitial"),
        BarStockMovementType.Sale => Strings.Get("BarMoveSale"),
        BarStockMovementType.Void => Strings.Get("BarMoveVoid"),
        _ => Strings.Get("BarMoveAdjust")
    };

    /// <summary>The localized name of a stock-adjustment reason (the database keeps the English key).</summary>
    public static string ReasonName(string reason) => reason switch
    {
        "restock" => Strings.Get("BarReasonRestock"),
        "recount" => Strings.Get("BarReasonRecount"),
        "damaged" => Strings.Get("BarReasonDamaged"),
        "other" => Strings.Get("BarReasonOther"),
        _ => reason
    };

    // ---- selling ----

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private void ApplySearch()
    {
        var needle = SearchBox.Text.Trim();
        var shown = needle == ""
            ? _sellable
            : _sellable.Where(p => p.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var keep = _selected?.Id;
        ProductList.ItemsSource = shown;
        var again = shown.FirstOrDefault(p => p.Id == keep);
        if (again is not null)
            ProductList.SelectedItem = again; // the refreshed copy, with the current stock

        _selected = again;
        ShowSelected();
    }

    private void OnProductSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ProductList.SelectedItem is ProductRow row)
        {
            if (_selected?.Id != row.Id)
            {
                QuantityBox.Text = "1";
                CupsBox.Text = "0";
            }

            _selected = row;
            SellMessageText.Text = "";
        }

        ShowSelected();
    }

    private void ShowSelected()
    {
        SelectedText.Text = _selected is null
            ? Strings.Get("BarChooseProduct")
            : Strings.Format("BarProductLine", _selected.Name, _selected.PriceDisplay, _selected.Stock);
        SellButton.IsEnabled = _selected is { IsAvailable: true };
        UpdateTotal();
    }

    private void OnAmountsChanged(object sender, TextChangedEventArgs e) => UpdateTotal();

    private void OnQuantityMinus(object sender, RoutedEventArgs e) => Step(QuantityBox, -1, 1, _selected?.Stock ?? 1);
    private void OnQuantityPlus(object sender, RoutedEventArgs e) => Step(QuantityBox, +1, 1, _selected?.Stock ?? 1);
    private void OnCupsMinus(object sender, RoutedEventArgs e) => Step(CupsBox, -1, 0, BarService.MaxCups);
    private void OnCupsPlus(object sender, RoutedEventArgs e) => Step(CupsBox, +1, 0, BarService.MaxCups);

    private static void Step(TextBox box, int by, int min, int max)
    {
        var current = int.TryParse(box.Text, out var n) ? n : min;
        box.Text = Math.Clamp(current + by, min, Math.Max(min, max)).ToString();
    }

    private void OnUnpaidToggled(object sender, RoutedEventArgs e)
    {
        CommentPanel.Visibility = UnpaidCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (UnpaidCheck.IsChecked == true)
            CommentBox.Focus();
    }

    private void UpdateTotal()
    {
        // This also runs while the window is still being built, before every box exists.
        if (TotalText is null || QuantityBox is null || CupsBox is null)
            return;

        var quantity = int.TryParse(QuantityBox.Text, out var q) ? q : 0;
        var cups = int.TryParse(CupsBox.Text, out var c) ? c : 0;
        var total = _selected is null ? 0L : (long)_selected.Price * Math.Max(quantity, 0) + (long)Math.Max(cups, 0) * _cupPrice;
        TotalText.Text = Strings.Format("BarTotalFormat", $"{total:N0}");
    }

    private async void OnSell(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            ShowMessage(Strings.Get("BarChooseProduct"), error: true);
            return;
        }

        if (!int.TryParse(QuantityBox.Text, out var quantity) || !int.TryParse(CupsBox.Text, out var cups))
        {
            ShowMessage(Strings.Get("BarEnterNumbers"), error: true);
            return;
        }

        var unpaid = UnpaidCheck.IsChecked == true;
        var target = SellToBox.SelectedItem as SellTarget;
        var name = _selected.Name;

        try
        {
            using (var scope = Services.CreateScope())
            {
                var bar = scope.ServiceProvider.GetRequiredService<BarService>();
                await bar.SellAsync(new BarService.SaleRequest(_selected.Id, quantity, cups, target?.MachineGuid, unpaid, CommentBox.Text));
            }

            QuantityBox.Text = "1";
            CupsBox.Text = "0";
            UnpaidCheck.IsChecked = false;
            CommentBox.Clear();
            await RefreshAsync();

            var left = _sellable.FirstOrDefault(p => p.Name == name)?.Stock ?? 0;
            ShowMessage(Strings.Format("BarSoldFormat", quantity, name, left), error: false);
        }
        catch (InvalidOperationException ex)
        {
            ShowMessage(ex.Message, error: true);
            await RefreshAsync();
        }
    }

    private void ShowMessage(string text, bool error)
    {
        SellMessageText.Text = text;
        SellMessageText.Foreground = (Brush)FindResource(error ? "ErrorBrush" : "SuccessBrush");
    }

    // ---- sales ----

    private async void OnVoidSale(object sender, RoutedEventArgs e)
    {
        if (SalesGrid.SelectedItem is not SaleRow sale)
        {
            MessageBox.Show(Strings.Get("BarSelectSale"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(Strings.Format("BarVoidConfirmFormat", sale.Summary), Strings.Get("ConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BarService>().VoidSaleAsync(sale.Id);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await RefreshAsync();
    }

    // ---- unpaid ----

    private UnpaidRow? SelectedUnpaid()
    {
        if (UnpaidGrid.SelectedItem is UnpaidRow row)
            return row;

        MessageBox.Show(Strings.Get("BarSelectSale"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private async void OnUnpaidMarkPaid(object sender, RoutedEventArgs e)
    {
        if (SelectedUnpaid() is not { } row)
            return;

        if (MessageBox.Show(Strings.Format("UnpaidMarkPaidConfirm", $"{row.Amount:N0}", row.Summary), Strings.Get("ConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RunUnpaidAsync(bar => bar.MarkPaidAsync(row.Id));
    }

    private async void OnUnpaidVoid(object sender, RoutedEventArgs e)
    {
        if (SelectedUnpaid() is not { } row)
            return;

        if (MessageBox.Show(Strings.Format("BarVoidConfirmFormat", row.Summary), Strings.Get("ConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RunUnpaidAsync(bar => bar.VoidSaleAsync(row.Id));
    }

    private async Task RunUnpaidAsync(Func<BarService, Task> action)
    {
        try
        {
            using var scope = Services.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<BarService>());
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await RefreshAsync();
    }

    // ---- products ----

    private ProductRow? SelectedManagedProduct()
    {
        if (ProductsGrid.SelectedItem is ProductRow row)
            return row;

        MessageBox.Show(Strings.Get("BarSelectProduct"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private async void OnAddProduct(object sender, RoutedEventArgs e)
    {
        new ProductWindow(Services, null, null, 0, 0) { Owner = Window.GetWindow(this) }.ShowDialog();
        await RefreshAsync();
    }

    private async void OnEditProduct(object sender, RoutedEventArgs e)
    {
        if (SelectedManagedProduct() is not { } p)
            return;

        new ProductWindow(Services, p.Id, p.Name, p.Price, p.Stock) { Owner = Window.GetWindow(this) }.ShowDialog();
        await RefreshAsync();
    }

    private async void OnAdjustStock(object sender, RoutedEventArgs e)
    {
        if (SelectedManagedProduct() is not { } p)
            return;

        new AdjustStockWindow(Services, p.Id, p.Name) { Owner = Window.GetWindow(this) }.ShowDialog();
        await RefreshAsync();
    }

    private async void OnArchive(object sender, RoutedEventArgs e)
    {
        if (SelectedManagedProduct() is not { } p)
            return;

        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BarService>().SetArchivedAsync(p.Id, !p.Archived);

        await RefreshAsync();
    }

    private async void OnShowArchivedToggled(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnSaveCupPrice(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CupPriceBox.Text.Replace(" ", ""), out var price))
        {
            MessageBox.Show(Strings.Get("BarCupPriceInvalid"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using (var scope = Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<BarService>().SetCupPriceAsync(price);

            await RefreshAsync();
            MessageBox.Show(Strings.Get("BarCupPriceSaved"), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- import ----

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Strings.Get("BarImportFilter"), CheckFileExists = true };
        if (dialog.ShowDialog() != true)
            return;

        if (MessageBox.Show(Strings.Get("BarImportConfirm"), Strings.Get("ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            BarImportService.Result result;
            using (var scope = Services.CreateScope())
                result = await scope.ServiceProvider.GetRequiredService<BarImportService>().ImportAsync(dialog.FileName);

            await RefreshAsync();
            MessageBox.Show(Strings.Format("BarImportDoneFormat", result.Products, result.Sales, result.Movements),
                Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            MessageBox.Show(Strings.Format("BarImportFailed", ex.Message), Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
