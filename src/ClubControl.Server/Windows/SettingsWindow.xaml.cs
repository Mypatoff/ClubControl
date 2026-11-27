using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Resources;
using ClubControl.Server.Security;
using ClubControl.Server.Services;
using ClubControl.Shared;
using ClubControl.Shared.Security;
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
            new ComboOption<ResourceKind>(Strings.Get("KindOther"), ResourceKind.Other),
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
        UnlockPasswordLabel.Text = Strings.Get("LabelUnlockPassword");
        AutoShutdownLabel.Text = Strings.Get("LabelAutoShutdown");
        AdminPasswordSectionLabel.Text = Strings.Get("LabelChangeAdminPassword");
        AdminPasswordHintLabel.Text = Strings.Get("HintChangeAdminPassword");
        CurrentAdminPasswordLabel.Text = Strings.Get("LabelCurrentAdminPassword");
        NewAdminPasswordLabel.Text = Strings.Get("LabelNewAdminPassword");
        ConfirmAdminPasswordLabel.Text = Strings.Get("ConfirmPasswordLabel");

        AddTariffButton.Content = Strings.Get("ButtonAdd");
        DeleteTariffButton.Content = "🗑 " + Strings.Get("ButtonDelete");
        TariffNameColumn.Header = Strings.Get("LabelName");
        TariffKindColumn.Header = Strings.Get("ColumnKind");
        TariffPriceColumn.Header = Strings.Get("LabelPricePerHour");
        TariffMinColumn.Header = Strings.Get("LabelMinPayment");
        TariffMaxColumn.Header = Strings.Get("LabelMaxPayment");

        ComputersHintText.Text = Strings.Get("ComputersHint");
        AddComputerButton.Content = "🖥 " + Strings.Get("AddComputerTitle");
        AddOtherButton.Content = "📦 " + Strings.Get("AddOtherTitle");
        DeleteResourceButton.Content = "🗑 " + Strings.Get("ButtonDelete");
        KindColumn.Header = Strings.Get("ColumnKind");
        PcNumberColumn.Header = Strings.Get("ColumnPcNumber");
        PcNameColumn.Header = Strings.Get("LabelName");
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
        _pcs = await LoadPcsAsync();
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

    private void OnDeleteTariff(object sender, RoutedEventArgs e)
    {
        if (TariffGrid.SelectedItem is not Tariff tariff)
        {
            MessageBox.Show(Strings.Get("SelectATariffToDelete"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // A PC/resource currently assigned to this tariff (including an edit made earlier in this
        // same dialog, before Save) would be left pointing at a tariff that no longer exists -
        // block that and point the admin at reassigning it first, same as OnDeleteResource does
        // for session history.
        if (_pcs.Any(p => p.TariffId == tariff.Id))
        {
            MessageBox.Show(Strings.Get("CannotDeleteTariffInUse"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(Strings.Get("DeleteTariffConfirm"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _db.Tariffs.Remove(tariff);
        _tariffs.Remove(tariff);
    }

    /// <summary>The PCs and resources in the grid. Deleted ones that are kept only for the reports ("*Old PC 6") are not listed.</summary>
    private Task<List<Pc>> LoadPcsAsync() =>
        _db.Pcs.Where(p => !p.Name.StartsWith(Pc.RetiredPrefix)).OrderBy(p => p.Number).ToListAsync();

    private async void OnAddComputer(object sender, RoutedEventArgs e) => await AddResourceAsync(ResourceKind.Computer);

    private async void OnAddOther(object sender, RoutedEventArgs e) => await AddResourceAsync(ResourceKind.Other);

    private async Task AddResourceAsync(ResourceKind kind)
    {
        // AddResourceWindow persists immediately via its own scope (same as when it was opened
        // straight from MainWindow) - just re-query so the new row shows up in this grid too.
        var window = new AddResourceWindow(_services, kind) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _pcs = await LoadPcsAsync();
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

        if (MessageBox.Show(Strings.Get("DeleteResourceConfirm"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        // One with session history is not erased (that would wipe it from past reports): it is renamed "*Old PC 6",
        // loses its number and is hidden. One without any history is removed completely.
        try
        {
            await PcRetirement.RemoveAsync(_db, pc);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, Strings.Get("ConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _pcs = await LoadPcsAsync();
        PcGrid.ItemsSource = _pcs;
    }

    /// <summary>
    /// A "New PC" given a number that an offline PC already has is probably that PC after a Windows reinstall (its machine
    /// id changed). Asks once per such pair; returns the pairs the admin confirmed, or null when the admin said no.
    /// </summary>
    private List<(Pc Old, Pc New)>? AskAboutReinstalledPcs()
    {
        var pairs = new List<(Pc Old, Pc New)>();
        foreach (var added in _pcs.Where(p => p.Number.HasValue && _openedWithNumbers.TryGetValue(p.Id, out var was) && was is null))
        {
            var old = _pcs.FirstOrDefault(o => o.Id != added.Id && o.Number == added.Number
                && _openedWithNumbers.TryGetValue(o.Id, out var oldWas) && oldWas == added.Number && PcRetirement.CanTakeOver(o, added));
            if (old is null)
                continue;

            var answer = MessageBox.Show(Strings.Format("TakeOverConfirmFormat", added.Number!.Value, added.Hostname, added.IpAddress),
                Strings.Get("ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return null;

            pairs.Add((old, added));
        }

        return pairs;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var reinstalled = AskAboutReinstalledPcs();
        if (reinstalled is null)
        {
            ErrorText.Text = Strings.Get("PcNumbersMustBeUnique");
            return;
        }

        var numbers = _pcs.Where(p => p.Number.HasValue && !reinstalled.Any(r => r.New == p)).Select(p => p.Number!.Value).ToList();
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

        // A computer picking an Other tariff (or vice versa) wouldn't crash - it would just bill
        // under the wrong class and quietly corrupt the per-class report breakdown (SPEC section 9).
        if (_pcs.Any(p => p.TariffId.HasValue && _tariffs.Any(t => t.Id == p.TariffId.Value && t.Kind != p.Kind)))
        {
            ErrorText.Text = Strings.Get("TariffKindMismatch");
            return;
        }

        // Changing the admin password: all three boxes empty = leave it alone. Anything typed is
        // checked in full here, before any setting is touched, so a mistake never half-saves.
        string? newAdminPasswordHash = null;
        if (WantsAdminPasswordChange())
        {
            var passwordError = CheckAdminPasswordChange(out newAdminPasswordHash);
            if (passwordError is not null)
            {
                ErrorText.Text = passwordError;
                return;
            }
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

        // Empty box = keep the current unlock password; typing one replaces it.
        if (!string.IsNullOrWhiteSpace(UnlockPasswordBox.Password))
            _setting.ClientUnlockPasswordHash = PasswordHasher.Hash(UnlockPasswordBox.Password);

        if (newAdminPasswordHash is not null)
            _setting.AdminPasswordHash = newAdminPasswordHash;

        _setting.AutoShutdownAfterMinutes = ParseInt(AutoShutdownBox.Text, _setting.AutoShutdownAfterMinutes);

        foreach (var tariff in _tariffs)
            tariff.Name = tariff.Name.Trim();

        // Reinstalled computers: the old PC row takes over the new machine id (this also saves what is pending above).
        foreach (var (old, added) in reinstalled)
        {
            await PcRetirement.TakeOverAsync(_db, old, added);
            _pcs.Remove(added);
        }

        await _db.SaveChangesAsync();

        var sessionService = _scope.ServiceProvider.GetRequiredService<SessionService>();
        await sessionService.BroadcastSettingsAsync();

        // Provision any PC that is now configured but has no auth token yet - this is the only
        // place a token is ever minted (see SessionService.ProvisionPcIfNeededAsync for why it is
        // tied to this exact Save action rather than to the PC's next heartbeat). No-ops for a PC
        // that is not currently connected; opening Settings and saving again once it is online
        // provisions it then.
        foreach (var pc in _pcs)
            await sessionService.ProvisionPcIfNeededAsync(pc);

        if (_setting.AdminPanelLanguage != _openedWithLanguage)
        {
            Strings.Apply(_setting.AdminPanelLanguage);
            MessageBox.Show(Strings.Get("LanguageChangedNotice"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        if (_setting.UiTheme != _openedWithTheme)
            NewThemeIfChanged = _setting.UiTheme;

        if (newAdminPasswordHash is not null)
            MessageBox.Show(Strings.Get("AdminPasswordChangedNotice"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);

        DialogResult = true;
    }

    private bool WantsAdminPasswordChange() =>
        CurrentAdminPasswordBox.Password.Length > 0
        || NewAdminPasswordBox.Password.Length > 0
        || ConfirmAdminPasswordBox.Password.Length > 0;

    /// <summary>
    /// Returns an error message, or null when the change is fine (then <paramref name="newHash"/> is
    /// the hash to store). The new password is checked first so a typo there never counts as a
    /// wrong-password attempt; the current password goes through the same lockout as the login window.
    /// </summary>
    private string? CheckAdminPasswordChange(out string? newHash)
    {
        newHash = null;
        var newPassword = NewAdminPasswordBox.Password;

        if (!PasswordPolicy.IsValid(newPassword, out var policyError))
            return Strings.Get(policyError!);

        if (newPassword != ConfirmAdminPasswordBox.Password)
            return Strings.Get("PasswordsDontMatch");

        if (LoginAttemptGuard.IsLockedOut(out var remaining))
            return Strings.Format("LoginLockedOut", Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds)));

        if (_setting.AdminPasswordHash is null || !PasswordHasher.Verify(CurrentAdminPasswordBox.Password, _setting.AdminPasswordHash))
        {
            LoginAttemptGuard.RecordFailure();
            return LoginAttemptGuard.IsLockedOut(out remaining)
                ? Strings.Format("LoginLockedOut", Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds)))
                : Strings.Get("WrongPassword");
        }

        LoginAttemptGuard.RecordSuccess();
        newHash = PasswordHasher.Hash(newPassword);
        return null;
    }

    private static int ParseInt(string text, int fallback) => int.TryParse(text, out var value) ? value : fallback;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
