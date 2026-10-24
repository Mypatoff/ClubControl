using System;
using System.Collections.Generic;
using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClubControl.Server.Windows;

/// <summary>Starts a free (no charge) session - on one PC, or several at once sharing a tariff, same as Start.</summary>
public partial class FreeSessionWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly List<PcRow> _targets;

    public FreeSessionWindow(IServiceProvider services, List<PcRow> targets)
    {
        InitializeComponent();
        _services = services;
        _targets = targets;

        Title = Strings.Get("FreeSessionTitle");
        TargetText.Text = TargetHeader.Build(targets);
        NoteLabel.Text = Strings.Get("LabelNote");
        CancelButton.Content = Strings.Get("Cancel");
        StartButton.Content = Strings.Get("ButtonStart");
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        var note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text;

        using var scope = _services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
        var failures = new List<string>();

        foreach (var target in _targets)
        {
            try
            {
                await sessionService.StartFreeAsync(target.MachineGuid, note);
            }
            catch (Exception ex)
            {
                failures.Add($"#{target.Number}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
            MessageBox.Show(string.Join("\n", failures), Strings.Get("SomeFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);

        if (failures.Count < _targets.Count)
            DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
