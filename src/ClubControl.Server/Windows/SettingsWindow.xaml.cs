using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

/// <summary>Settings (SPEC section 8): General, Tariffs, Computers. One DbContext for the whole window, saved on "Save".</summary>
public partial class SettingsWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly ClubDbContext _db;

    private Setting _setting = null!;
    private ObservableCollection<Tariff> _tariffs = null!;
    private List<Pc> _pcs = null!;
    private Dictionary<int, int?> _openedWithNumbers = null!;
    private AdminPanelLanguage _openedWithLanguage;
    private UiTheme _openedWithTheme;

    /// <summary>
    /// Set after a successful Save if the theme actually changed, so the caller (MainWindow, once
    /// this dialog has fully closed) can safely swap the theme and recreate itself - doing that
    /// from inside this dialog would risk WPF closing this owned window as a side effect of
    /// closing its MainWindow owner mid-save.
    /// </summary>
    public UiTheme? NewThemeIfChanged { get; private set; }

    private record ComboOption<T>(string Display, T Value);

    public SettingsWindow(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _scope = services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ClubDbContext>();

        ApplyLocalization();

        TariffKindColumn.ItemsSource = new[]
        {
            new ComboOption<ResourceKind>(Strings.Get("KindComputer"), ResourceKind.Computer),
            new ComboOption<ResourceKind>(Strings.Get("KindBilliard"), ResourceKind.Billiard),
        };

        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => _scope.Dispose();
    }

    private void ApplyLocalization()
    {
        Title = Strings.Get("SettingsTitle");
        CancelButton.Content = Strings.Get("Cancel");
        SaveButton.Content = Strings.Get("Save");

        GeneralTab.Header = Strings.Get("TabGeneral");
        TariffsTab.Header = Strings.Get("TabTariffs");
        ComputersTab.Header = Strings.Get("TabResources");

        ClubNameLabel.Text = Strings.Get("LabelClubName");
        LockScreenTextLabel.Text = Strings.Get("LabelLockScreenText");
        LockScreenLanguageLabel.Text = Strings.Get("LabelLockScreenLanguage");
        AdminPanelLanguageLabel.Text = Strings.Get("LabelAdminPanelLanguage");
        UiThemeLabel.Text = Strings.Get("LabelUiTheme");
        WarningMinutesLabel.Text = Strings.Get("LabelWarningMinutes");
        AfterSessionActionLabel.Text = Strings.Get("LabelAfterSessionAction");
        ProcessListLabel.Text = Strings.Get("LabelProcessList");
        OfflineWaitLabel.Text = Strings.Get("LabelOfflineWait");
        AutoShutdownLabel.Text = Strings.Get("LabelAutoShutdown");

        AddTariffButton.Content = Strings.Get("ButtonAdd");
        TariffNameColumn.Header = Strings.Get("LabelName");
        TariffKindColumn.Header = Strings.Get("ColumnKind");
        TariffPriceColumn.Header = Strings.Get("LabelPricePerHour");
        TariffMinColumn.Header = Strings.Get("LabelMinPayment");
        TariffMaxColumn.Header = Strings.Get("LabelMaxPayment");

        ComputersHintText.Text = Strings.Get("ComputersHint");
        AddComputerButton.Content = "🖥 " + Strings.Get("AddComputerTitle");
        AddBilliardButton.Content = "🎱 " + Strings.Get("AddBilliardTitle");
        DeleteResourceButton.Content = "🗑 " + Strings.Get("ButtonDelete");
        KindColumn.Header = Strings.Get("ColumnKind");
        PcNumberColumn.Header = Strings.Get("ColumnPcNumber");
        TariffColumn.Header = Strings.Get("ColumnTariff");
        EnabledColumn.Header = Strings.Get("ColumnEnabled");
        HostnameColumn.Header = Strings.Get("ColumnHostname");
        IpColumn.Header = Strings.Get("ColumnIp");
        MacColumn.Header = Strings.Get("ColumnMac");
        OnlineColumn.Header = Strings.Get("ColumnOnline");
    }

    private async Task LoadAsync()
    {
        _setting = await SettingsHelper.GetOrCreateAsync(_db);
        _tariffs = new ObservableCollection<Tariff>(await TariffsHelper.GetAllAsync(_db));
        _pcs = await _db.Pcs.OrderBy(p => p.Number).ToListAsync();
        _openedWithNumbers = _pcs.ToDictionary(p => p.Id, p => p.Number);
        _openedWithLanguage = _setting.AdminPanelLanguage;

        ClubNameBox.Text = _setting.ClubName;
        LockScreenTextBox.Text = _setting.LockScreenText;

        LockScreenLanguageCombo.ItemsSource = Enum.GetValues<LockScreenLanguage>()
            .Select(l => new ComboOption<LockScreenLanguage>(Strings.LockScreenLanguageName(l), l))
            .ToList();
        LockScreenLanguageCombo.SelectedItem = ((List<ComboOption<LockScreenLanguage>>)LockScreenLanguageCombo.ItemsSource)
            .First(o => o.Value == _setting.LockScreenLanguage);

        AdminPanelLanguageCombo.ItemsSource = Enum.GetValues<AdminPanelLanguage>()
            .Select(l => new ComboOption<AdminPanelLanguage>(Strings.AdminPanelLanguageName(l), l))
            .ToList();
        AdminPanelLanguageCombo.SelectedItem = ((List<ComboOption<AdminPanelLanguage>>)AdminPanelLanguageCombo.ItemsSource)
            .First(o => o.Value == _setting.AdminPanelLanguage);

        _openedWithTheme = _setting.UiTheme;
        UiThemeCombo.ItemsSource = new[]
        {
            new ComboOption<UiTheme>(Strings.Get("ThemeLight"), UiTheme.Light),
            new ComboOption<UiTheme>(Strings.Get("ThemeDark"), UiTheme.Dark),
        };
        UiThemeCombo.SelectedItem = ((ComboOption<UiTheme>[])UiThemeCombo.ItemsSource)
            .First(o => o.Value == _setting.UiTheme);

        WarningMinutes1Box.Text = _setting.WarningMinutes1.ToString();
        WarningMinutes2Box.Text = _setting.WarningMinutes2.ToString();

        AfterSessionActionCombo.ItemsSource = Enum.GetValues<AfterSessionAction>()
            .Select(a => new ComboOption<AfterSessionAction>(Strings.AfterSessionActionName(a), a))
            .ToList();
        AfterSessionActionCombo.SelectedItem = ((List<ComboOption<AfterSessionAction>>)AfterSessionActionCombo.ItemsSource)
            .First(o => o.Value == _setting.AfterSessionAction);

        ProcessListBox.Text = _setting.ProcessListCsv;
        OfflineWaitBox.Text = _setting.OfflineUnlockWaitSeconds.ToString();
        AutoShutdownBox.Text = _setting.AutoShutdownAfterMinutes.ToString();

        TariffGrid.ItemsSource = _tariffs;
        TariffColumn.ItemsSource = _tariffs;
        PcGrid.ItemsSource = _pcs;
    }

    private void OnAddTariff(object sender, RoutedEventArgs e)
    {
        var tariff = new Tariff { Name = Strings.Get("NewTariffDefaultName"), Kind = ResourceKind.Computer };
        _db.Tariffs.Add(tariff);
        _tariffs.Add(tariff);
    }

    private async void OnAddComputer(object sender, RoutedEventArgs e) => await AddResourceAsync(ResourceKind.Computer);

    private async void OnAddBilliard(object sender, RoutedEventArgs e) => await AddResourceAsync(ResourceKind.Billiard);

    private async Task AddResourceAsync(ResourceKind kind)
    {
        // AddResourceWindow persists immediately via its own scope (same as when it was opened
        // straight from MainWindow) - just re-query so the new row shows up in this grid too.
        var window = new AddResourceWindow(_services, kind) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _pcs = await _db.Pcs.OrderBy(p => p.Number).ToListAsync();
            PcGrid.ItemsSource = _pcs;
        }
    }

    private async void OnDeleteResource(object sender, RoutedEventArgs e)
    {
        if (PcGrid.SelectedItem is not Pc pc)
        {
            MessageBox.Show(Strings.Get("SelectAResourceToDelete"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Deleting a PC/billiard with any billing history would cascade-delete its Sessions and
        // SessionSegments too (required FK), silently erasing past reports - block that and point
        // the admin at "Enabled" instead, which hides it without touching history.
        if (await _db.Sessions.AnyAsync(s => s.PcId == pc.Id))
        {
            MessageBox.Show(Strings.Get("CannotDeleteResourceWithHistory"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(Strings.Get("DeleteResourceConfirm"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _db.Pcs.Remove(pc);
        await _db.SaveChangesAsync();

        _pcs = await _db.Pcs.OrderBy(p => p.Number).ToListAsync();
        PcGrid.ItemsSource = _pcs;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var numbers = _pcs.Where(p => p.Number.HasValue).Select(p => p.Number!.Value).ToList();
        if (numbers.Count != numbers.Distinct().Count())
        {
            ErrorText.Text = Strings.Get("PcNumbersMustBeUnique");
            return;
        }

        var names = _tariffs.Select(t => t.Name.Trim()).ToList();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Count != names.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            ErrorText.Text = Strings.Get("TariffNamesMustBeUniqueAndNotEmpty");
            return;
        }

        // Catch a misconfigured tariff here, at save time, rather than letting it reach
        // SessionService later - a price of 0 (the default for a freshly added tariff nobody has
        // edited yet) would otherwise let a prepay session be created that charges full money for
        // zero minutes (see BillingCalculator.IsValidTariff).
        if (_tariffs.Any(t => !BillingCalculator.IsValidTariff(t.PricePerHourUzs, t.MinPaymentUzs, t.MaxPaymentUzs)))
        {
            ErrorText.Text = Strings.Get("TariffPriceAndLimitsMustBeValid");
            return;
        }

        // A PC whose number was just cleared is being unassigned/repurposed - its auth token (if
        // any) no longer vouches for whatever ends up at that MachineGuid next, so require
        // re-provisioning (see ClubHub.Heartbeat) rather than letting a stale token keep trusting
        // it under a new identity.
        foreach (var pc in _pcs)
        {
            if (_openedWithNumbers.TryGetValue(pc.Id, out var originalNumber) && originalNumber.HasValue && !pc.Number.HasValue)
                pc.AuthTokenHash = null;
        }

        _setting.ClubName = ClubNameBox.Text;
        _setting.LockScreenText = LockScreenTextBox.Text;
        _setting.LockScreenLanguage = ((ComboOption<LockScreenLanguage>)LockScreenLanguageCombo.SelectedItem!).Value;
        _setting.AdminPanelLanguage = ((ComboOption<AdminPanelLanguage>)AdminPanelLanguageCombo.SelectedItem!).Value;
        _setting.UiTheme = ((ComboOption<UiTheme>)UiThemeCombo.SelectedItem!).Value;
        _setting.WarningMinutes1 = ParseInt(WarningMinutes1Box.Text, _setting.WarningMinutes1);
        _setting.WarningMinutes2 = ParseInt(WarningMinutes2Box.Text, _setting.WarningMinutes2);
        _setting.AfterSessionAction = ((ComboOption<AfterSessionAction>)AfterSessionActionCombo.SelectedItem!).Value;
        _setting.ProcessListCsv = ProcessListBox.Text;
        _setting.OfflineUnlockWaitSeconds = ParseInt(OfflineWaitBox.Text, _setting.OfflineUnlockWaitSeconds);
        _setting.AutoShutdownAfterMinutes = ParseInt(AutoShutdownBox.Text, _setting.AutoShutdownAfterMinutes);

        foreach (var tariff in _tariffs)
            tariff.Name = tariff.Name.Trim();

        await _db.SaveChangesAsync();

        var sessionService = _scope.ServiceProvider.GetRequiredService<SessionService>();
        await sessionService.BroadcastSettingsAsync();

        if (_setting.AdminPanelLanguage != _openedWithLanguage)
        {
            Strings.Apply(_setting.AdminPanelLanguage);
            MessageBox.Show(Strings.Get("LanguageChangedNotice"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        if (_setting.UiTheme != _openedWithTheme)
            NewThemeIfChanged = _setting.UiTheme;

        DialogResult = true;
    }

    private static int ParseInt(string text, int fallback) => int.TryParse(text, out var value) ? value : fallback;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
