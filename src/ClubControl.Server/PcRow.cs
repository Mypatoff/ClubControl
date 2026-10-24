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
    public required int? Number { get; init; }
    public required PcClass? Class { get; init; }
    public required PcState State { get; init; }

    public required string ClassDisplay { get; init; }
    public required string StateDisplay { get; init; }
    public required string SessionTypeDisplay { get; init; }
    public required string AmountDisplay { get; init; }
    public required string StartDisplay { get; init; }
    public required string EndDisplay { get; init; }
    public required string RemainingDisplay { get; init; }

    /// <summary>Row colours (SPEC 7): offline overrides everything else; otherwise colour by session state.</summary>
    public Brush RowBrush => !IsOnline
        ? Brushes.LightGray
        : State switch
        {
            PcState.PrepaySession => Brushes.LightGreen,
            PcState.PostpaySession => Brushes.LightBlue,
            PcState.LimitReached => Brushes.Orange,
            _ => Brushes.White
        };

    public Brush DotBrush => IsOnline ? Brushes.Green : Brushes.Gray;
}
