using System;
using System.Collections.Generic;
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
    private readonly IServiceScope _scope;
    private readonly ClubDbContext _db;

    private Setting _setting = null!;
    private Tariff _standardTariff = null!;
    private Tariff _vipTariff = null!;
    private List<Pc> _pcs = null!;
    private AdminPanelLanguage _openedWithLanguage;

    private record ComboOption<T>(string Display, T Value);

    public SettingsWindow(IServiceProvider services)
    {
        InitializeComponent();
        _scope = services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ClubDbContext>();

        ApplyLocalization();

        ClassColumn.ItemsSource = new[]
        {
            new ComboOption<PcClass>(Strings.ClassName(PcClass.Standard), PcClass.Standard),
            new ComboOption<PcClass>(Strings.ClassName(PcClass.Vip), PcClass.Vip),
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
        ComputersTab.Header = Strings.Get("TabComputers");

        ClubNameLabel.Text = Strings.Get("LabelClubName");
        LockScreenTextLabel.Text = Strings.Get("LabelLockScreenText");
        LockScreenLanguageLabel.Text = Strings.Get("LabelLockScreenLanguage");
        AdminPanelLanguageLabel.Text = Strings.Get("LabelAdminPanelLanguage");
        WarningMinutesLabel.Text = Strings.Get("LabelWarningMinutes");
        AfterSessionActionLabel.Text = Strings.Get("LabelAfterSessionAction");
        ProcessListLabel.Text = Strings.Get("LabelProcessList");
        OfflineWaitLabel.Text = Strings.Get("LabelOfflineWait");

        StandardGroup.Header = Strings.Get("GroupStandard");
        VipGroup.Header = Strings.Get("GroupVip");
        StandardPriceLabel.Text = Strings.Get("LabelPricePerHour");
        StandardMinLabel.Text = Strings.Get("LabelMinPayment");
        StandardMaxLabel.Text = Strings.Get("LabelMaxPayment");
        VipPriceLabel.Text = Strings.Get("LabelPricePerHour");
        VipMinLabel.Text = Strings.Get("LabelMinPayment");
        VipMaxLabel.Text = Strings.Get("LabelMaxPayment");

        ComputersHintText.Text = Strings.Get("ComputersHint");
        PcNumberColumn.Header = Strings.Get("ColumnPcNumber");
        ClassColumn.Header = Strings.Get("ColumnClass");
        EnabledColumn.Header = Strings.Get("ColumnEnabled");
        HostnameColumn.Header = Strings.Get("ColumnHostname");
        IpColumn.Header = Strings.Get("ColumnIp");
        MacColumn.Header = Strings.Get("ColumnMac");
        OnlineColumn.Header = Strings.Get("ColumnOnline");
    }

    private async Task LoadAsync()
    {
        _setting = await SettingsHelper.GetOrCreateAsync(_db);
        _standardTariff = await TariffsHelper.GetAsync(_db, PcClass.Standard) ?? throw new InvalidOperationException("Standard tariff missing.");
        _vipTariff = await TariffsHelper.GetAsync(_db, PcClass.Vip) ?? throw new InvalidOperationException("VIP tariff missing.");
        _pcs = await _db.Pcs.OrderBy(p => p.Number).ToListAsync();
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

        WarningMinutes1Box.Text = _setting.WarningMinutes1.ToString();
        WarningMinutes2Box.Text = _setting.WarningMinutes2.ToString();

        AfterSessionActionCombo.ItemsSource = Enum.GetValues<AfterSessionAction>()
            .Select(a => new ComboOption<AfterSessionAction>(Strings.AfterSessionActionName(a), a))
            .ToList();
        AfterSessionActionCombo.SelectedItem = ((List<ComboOption<AfterSessionAction>>)AfterSessionActionCombo.ItemsSource)
            .First(o => o.Value == _setting.AfterSessionAction);

        ProcessListBox.Text = _setting.ProcessListCsv;
        OfflineWaitBox.Text = _setting.OfflineUnlockWaitSeconds.ToString();

        StandardPriceBox.Text = _standardTariff.PricePerHourUzs.ToString();
        StandardMinBox.Text = _standardTariff.MinPaymentUzs.ToString();
        StandardMaxBox.Text = _standardTariff.MaxPaymentUzs.ToString();
        VipPriceBox.Text = _vipTariff.PricePerHourUzs.ToString();
        VipMinBox.Text = _vipTariff.MinPaymentUzs.ToString();
        VipMaxBox.Text = _vipTariff.MaxPaymentUzs.ToString();

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

        _setting.ClubName = ClubNameBox.Text;
        _setting.LockScreenText = LockScreenTextBox.Text;
        _setting.LockScreenLanguage = ((ComboOption<LockScreenLanguage>)LockScreenLanguageCombo.SelectedItem!).Value;
        _setting.AdminPanelLanguage = ((ComboOption<AdminPanelLanguage>)AdminPanelLanguageCombo.SelectedItem!).Value;
        _setting.WarningMinutes1 = ParseInt(WarningMinutes1Box.Text, _setting.WarningMinutes1);
        _setting.WarningMinutes2 = ParseInt(WarningMinutes2Box.Text, _setting.WarningMinutes2);
        _setting.AfterSessionAction = ((ComboOption<AfterSessionAction>)AfterSessionActionCombo.SelectedItem!).Value;
        _setting.ProcessListCsv = ProcessListBox.Text;
        _setting.OfflineUnlockWaitSeconds = ParseInt(OfflineWaitBox.Text, _setting.OfflineUnlockWaitSeconds);

        _standardTariff.PricePerHourUzs = ParseInt(StandardPriceBox.Text, _standardTariff.PricePerHourUzs);
        _standardTariff.MinPaymentUzs = ParseInt(StandardMinBox.Text, _standardTariff.MinPaymentUzs);
        _standardTariff.MaxPaymentUzs = ParseInt(StandardMaxBox.Text, _standardTariff.MaxPaymentUzs);
        _vipTariff.PricePerHourUzs = ParseInt(VipPriceBox.Text, _vipTariff.PricePerHourUzs);
        _vipTariff.MinPaymentUzs = ParseInt(VipMinBox.Text, _vipTariff.MinPaymentUzs);
        _vipTariff.MaxPaymentUzs = ParseInt(VipMaxBox.Text, _vipTariff.MaxPaymentUzs);

        await _db.SaveChangesAsync();

        var sessionService = _scope.ServiceProvider.GetRequiredService<SessionService>();
        await sessionService.BroadcastSettingsAsync();

        if (_setting.AdminPanelLanguage != _openedWithLanguage)
        {
            Strings.Apply(_setting.AdminPanelLanguage);
            MessageBox.Show(Strings.Get("LanguageChangedNotice"), Strings.Get("ConfirmTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        DialogResult = true;
    }

    private static int ParseInt(string text, int fallback) => int.TryParse(text, out var value) ? value : fallback;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
