using System;
using System.Windows.Media;
using ClubControl.Shared;

namespace ClubControl.Server;

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

    /// <summary>Row colours (SPEC 7): offline overrides everything else for networked PCs; manual resources are never "offline".</summary>
    public Brush RowBrush => !IsManual && !IsOnline
        ? Brushes.LightGray
        : State switch
        {
            PcState.PrepaySession => Brushes.LightGreen,
            PcState.PostpaySession => Brushes.LightBlue,
            PcState.LimitReached => Brushes.Orange,
            _ => Brushes.White
        };

    public Brush DotBrush => IsManual ? Brushes.Transparent : (IsOnline ? Brushes.Green : Brushes.Gray);
}
