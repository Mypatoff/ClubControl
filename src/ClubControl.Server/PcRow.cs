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
    public string Name { get; init; } = "";
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

    /// <summary>Client build this PC last reported ("1.0.0+f4545fa"); empty if unknown. Lets the admin see which PCs an "Update client" has reached.</summary>
    public string ClientVersion { get; init; } = "";

    /// <summary>The optional note typed when the current session was started; empty if none.</summary>
    public string Note { get; init; } = "";

    /// <summary>Total of the items (drinks, snacks) added to the current session; 0 if none.</summary>
    public int ExtrasUzs { get; init; }

    /// <summary>A manual resource (offline PC, other resource) never connects, so there is nothing to shut down/restart/wake.</summary>
    public bool SupportsPowerControl => !IsManual;

    public bool HasActiveSession => State is PcState.PrepaySession or PcState.PostpaySession or PcState.LimitReached;

    private static Brush Resource(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;

    public bool IsOther => Kind == ResourceKind.Other;

    /// <summary>A networked PC that is switched off / disconnected: its card is red.</summary>
    public bool IsOffline => !IsManual && !IsOnline;

    /// <summary>Section heading on the main panel ("PCs" / "Other"); the cards are grouped by <see cref="IsOther"/>.</summary>
    public string GroupTitle => Strings.Get(IsOther ? "KindOtherPlural" : "KindComputerPlural");

    public string NumberDisplay => Number?.ToString() ?? "–";

    /// <summary>What the admin calls this resource: the chosen name for a named "Other" resource, otherwise its number.</summary>
    public string Label => Strings.ResourceLabel(Kind, Name, Number);

    public string CardTitle => IsOther && Name != "" ? Name : NumberDisplay;

    /// <summary>The big line on the card: time left for prepay, running cost for postpay / limit reached.</summary>
    public string PrimaryDisplay => RemainingDisplay != "" ? RemainingDisplay : AmountDisplay;

    /// <summary>Everything the compact card leaves out (tariff, amount, start/end), shown on hover.</summary>
    public string TooltipText => string.Join("\n", new[]
    {
        IsOther && Name != "" ? Name : "",
        StateDisplay, // the card is narrow, so a long state name ("Лимит достигнут") can be cut off there
        TariffDisplay,
        AmountDisplay,
        StartDisplay != "" ? StartDisplay + (EndDisplay != "" ? " – " + EndDisplay : "") : "",
        Note,
        ExtrasUzs > 0 ? Strings.Format("ItemsTooltipFormat", ExtrasUzs.ToString("N0")) : "",
        ClientVersion != "" ? "v" + ClientVersion : ""
    }.Where(line => line != ""));

    /// <summary>Card accent colour (SPEC 7): red = offline (overrides everything else for networked PCs; manual resources are never "offline"), green = session running, blue = free.</summary>
    public Brush AccentBrush => IsOffline
        ? Resource("StateOfflineBrush")
        : State switch
        {
            PcState.PrepaySession or PcState.PostpaySession => Resource("StateRunningBrush"),
            PcState.LimitReached => Resource("StateLimitBrush"),
            _ => Resource("StateIdleBrush")
        };

    public Brush DotBrush => IsManual ? Brushes.Transparent : (IsOnline ? Resource("OnlineDotBrush") : Resource("OfflineDotBrush"));
}
