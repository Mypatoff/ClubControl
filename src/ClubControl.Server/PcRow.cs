using System;
using System.Windows.Media;
using ClubControl.Shared;

namespace ClubControl.Server;

// Mirrors the brush colours in Themes/Theme.xaml - kept as plain values here (rather than a
// resource lookup) since PcRow is a plain data object, not something tied to the visual tree.

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

    private static readonly Brush PrepayRowBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xF3, 0xE4));
    private static readonly Brush PostpayRowBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xEA, 0xFB));
    private static readonly Brush LimitRowBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xE6, 0xC8));
    private static readonly Brush OfflineRowBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xEC, 0xEF));
    private static readonly Brush SurfaceBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush OnlineDotBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x9E, 0x52));
    private static readonly Brush OfflineDotBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA1, 0xAC));

    /// <summary>Row colours (SPEC 7): offline overrides everything else for networked PCs; manual resources are never "offline".</summary>
    public Brush RowBrush => !IsManual && !IsOnline
        ? OfflineRowBrush
        : State switch
        {
            PcState.PrepaySession => PrepayRowBrush,
            PcState.PostpaySession => PostpayRowBrush,
            PcState.LimitReached => LimitRowBrush,
            _ => SurfaceBrush
        };

    public Brush DotBrush => IsManual ? Brushes.Transparent : (IsOnline ? OnlineDotBrush : OfflineDotBrush);
}
