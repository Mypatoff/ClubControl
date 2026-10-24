using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Server.Data;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

public partial class MoveSessionWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly PcRow _pc;

    public MoveSessionWindow(IServiceProvider services, PcRow pc)
    {
        InitializeComponent();
        _services = services;
        _pc = pc;

        Title = Strings.Get("MoveSessionTitle");
        TargetText.Text = Strings.Format("MoveSessionHeaderFormat", pc.Number!);
        MoveToLabel.Text = Strings.Get("MoveToLabel");
        CancelButton.Content = Strings.Get("Cancel");
        MoveButton.Content = Strings.Get("ButtonMove");

        Loaded += async (_, _) => await LoadCandidatesAsync();
    }

    private async Task LoadCandidatesAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();

        var candidates = await db.Pcs.AsNoTracking()
            .Where(p => p.Id != _pc.Id && p.Enabled && p.Number != null
                        && (p.State == PcState.Free || p.State == PcState.Locked))
            .OrderBy(p => p.Number)
            .ToListAsync();

        var tariffNames = await db.Tariffs.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);

        TargetCombo.ItemsSource = candidates
            .Select(p => new PcOption(
                Strings.Format("TargetPcFormat", p.Number!, p.TariffId.HasValue ? tariffNames.GetValueOrDefault(p.TariffId.Value, "") : ""),
                p.MachineGuid))
            .ToList();

        if (candidates.Count == 0)
            ErrorText.Text = Strings.Get("NoFreePc");
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        if (TargetCombo.SelectedItem is not PcOption target)
        {
            ErrorText.Text = Strings.Get("ChooseTargetPc");
            return;
        }

        try
        {
            using var scope = _services.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
            await sessionService.MoveSessionAsync(_pc.MachineGuid, target.MachineGuid);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private record PcOption(string Display, Guid MachineGuid);
}
