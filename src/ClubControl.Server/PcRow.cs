using System;
using System.Windows;
using System.Windows.Media;
using ClubControl.Shared;

namespace ClubControl.Server;

// Looks the brushes up from the active theme (Themes/Theme.xaml or DarkTheme.xaml) rather than
// hardcoding colours, so rows re-theme automatically on the next refresh tick after a theme
// switch - PcRow instances are rebuilt fresh every second anyway (see MainWindow.RefreshAsync).

/// <summary>
/// One row of the main table (SPEC section 7). Built fresh on every refresh tick from the
/// database plus <see cref="Services.SessionService.ActiveSessionSnapshot"/> - never bound live to
/// an EF entity, so the UI thread never touches a DbContext from another scope.
/// </summary>
public class PcRow
{
    public required int Id { get; init; }
    public required Guid MachineGuid { get; init; }
    public required bool IsOnline { get; init; }
    public required bool Enabled { get; init; }
    public required bool IsManual { get; init; }
    public required ResourceKind Kind { get; init; }
    public required int? Number { get; init; }
    public required int? TariffId { get; init; }
    public required PcState State { get; init; }

    public required string KindIcon { get; init; }
    public required string TariffDisplay { get; init; }
    public required string StateDisplay { get; init; }
    public required string SessionTypeDisplay { get; init; }
    public required string AmountDisplay { get; init; }
    public required string StartDisplay { get; init; }
    public required string EndDisplay { get; init; }
    public required string RemainingDisplay { get; init; }

    /// <summary>A manual resource (offline PC, billiard table) never connects, so there is nothing to shut down/restart/wake.</summary>
    public bool SupportsPowerControl => !IsManual;

    public bool HasActiveSession => State is PcState.PrepaySession or PcState.PostpaySession or PcState.LimitReached;

    private static Brush Resource(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;

    /// <summary>Row colours (SPEC 7): offline overrides everything else for networked PCs; manual resources are never "offline".</summary>
    public Brush RowBrush => !IsManual && !IsOnline
        ? Resource("OfflineRowBrush")
        : State switch
        {
            PcState.PrepaySession => Resource("PrepayRowBrush"),
            PcState.PostpaySession => Resource("PostpayRowBrush"),
            PcState.LimitReached => Resource("LimitRowBrush"),
            _ => Resource("SurfaceBrush")
        };

    public Brush DotBrush => IsManual ? Brushes.Transparent : (IsOnline ? Resource("OnlineDotBrush") : Resource("OfflineDotBrush"));
}
