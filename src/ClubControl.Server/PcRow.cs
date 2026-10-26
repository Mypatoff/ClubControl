using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ClubControl.Server.Resources;
using ClubControl.Shared;

namespace ClubControl.Server;

// Looks the brushes up from the active theme (Themes/Theme.xaml or DarkTheme.xaml) rather than
// hardcoding colours, so cards re-theme automatically on the next refresh tick after a theme
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

    public bool IsBilliard => Kind == ResourceKind.Billiard;

    /// <summary>A networked PC that is switched off / disconnected: its card is shown faded.</summary>
    public bool IsDimmed => !IsManual && !IsOnline;

    /// <summary>Section heading on the main panel ("PCs" / "Billiards"); the cards are grouped by <see cref="IsBilliard"/>.</summary>
    public string GroupTitle => Strings.Get(IsBilliard ? "KindBilliardPlural" : "KindComputerPlural");

    public string NumberDisplay => Number?.ToString() ?? "–";

    /// <summary>The big line on the card: time left for prepay, running cost for postpay / limit reached.</summary>
    public string PrimaryDisplay => RemainingDisplay != "" ? RemainingDisplay : AmountDisplay;

    /// <summary>Everything the compact card leaves out (tariff, amount, start/end), shown on hover.</summary>
    public string TooltipText => string.Join("\n", new[]
    {
        TariffDisplay,
        AmountDisplay,
        StartDisplay != "" ? StartDisplay + (EndDisplay != "" ? " – " + EndDisplay : "") : ""
    }.Where(line => line != ""));

    /// <summary>Card accent colour (SPEC 7): offline overrides everything else for networked PCs; manual resources are never "offline".</summary>
    public Brush AccentBrush => IsDimmed
        ? Resource("StateOfflineBrush")
        : State switch
        {
            PcState.PrepaySession => Resource("StatePrepayBrush"),
            PcState.PostpaySession => Resource("StatePostpayBrush"),
            PcState.LimitReached => Resource("StateLimitBrush"),
            _ => Resource("StateIdleBrush")
        };

    public Brush DotBrush => IsManual ? Brushes.Transparent : (IsOnline ? Resource("OnlineDotBrush") : Resource("OfflineDotBrush"));
}
