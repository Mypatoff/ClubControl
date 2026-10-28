using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Shared;

namespace ClubControl.Server;

// Looks the brushes up from the active theme (Themes/Theme.xaml or DarkTheme.xaml) rather than
// hardcoding colours, so cards re-theme automatically on the next refresh tick after a theme
// switch - the window is recreated for a theme switch (see App), so its rows start over.

/// <summary>
/// One row of the main table (SPEC section 7). Built on every refresh tick from the
/// database plus <see cref="Services.SessionService.ActiveSessionSnapshot"/> - never bound live to
/// an EF entity, so the UI thread never touches a DbContext from another scope. The card already on
/// screen takes over the new values (<see cref="CopyFrom"/>) instead of being replaced by a new one,
/// so a refresh does not rebuild all the cards every second.
/// </summary>
public class PcRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // Every plain property above and below (the ones with a setter); the computed ones follow from them.
    private static readonly PropertyInfo[] StoredProperties = typeof(PcRow).GetProperties()
        .Where(p => p.CanRead && p.CanWrite).ToArray();

    /// <summary>
    /// Takes over the values of a freshly built row for the same PC and, only if something really changed,
    /// tells the card to redraw (all its bindings, including the computed ones such as the colour).
    /// </summary>
    public void CopyFrom(PcRow fresh)
    {
        var changed = false;
        foreach (var property in StoredProperties)
        {
            var value = property.GetValue(fresh);
            if (Equals(property.GetValue(this), value))
                continue;

            property.SetValue(this, value);
            changed = true;
        }

        if (changed)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public required int Id { get; set; }
    public required Guid MachineGuid { get; set; }
    public required bool IsOnline { get; set; }
    public required bool Enabled { get; set; }
    public required bool IsManual { get; set; }
    public required ResourceKind Kind { get; set; }
    public required int? Number { get; set; }
    public string Name { get; set; } = "";
    public required int? TariffId { get; set; }
    public required PcState State { get; set; }

    public required string KindIcon { get; set; }
    public required string TariffDisplay { get; set; }
    public required string StateDisplay { get; set; }
    public required string SessionTypeDisplay { get; set; }
    public required string AmountDisplay { get; set; }
    public required string StartDisplay { get; set; }
    public required string EndDisplay { get; set; }
    public required string RemainingDisplay { get; set; }

    /// <summary>Client build this PC last reported ("1.0.0+f4545fa"); empty if unknown. Lets the admin see which PCs an "Update client" has reached.</summary>
    public string ClientVersion { get; set; } = "";

    /// <summary>Progress / result of the last "Update client" on this PC; null when there is nothing to show.</summary>
    public ClientUpdateInfo? Update { get; set; }

    /// <summary>The client on this PC keeps connecting but the server rejects its token (see <see cref="Services.AuthRejectionTracker"/>).</summary>
    public bool AuthRejected { get; set; }

    public bool IsAuthRejected => IsOffline && AuthRejected;

    /// <summary>The server found this PC switched on although its client is not connected (see <see cref="Services.ClientMissingTracker"/>).</summary>
    public bool ClientMissing { get; set; }

    /// <summary>A networked PC that is on but whose client was deleted, uninstalled or closed: not the same as switched off.</summary>
    public bool IsClientMissing => IsOffline && ClientMissing;

    /// <summary>The state text on the card: the update progress or result while there is one, then "No client", otherwise the normal state.</summary>
    public string CardStateDisplay => Update is not null ? UpdateDisplay
        : IsAuthRejected ? Strings.Get("AuthRejectedCard")
        : IsClientMissing ? Strings.Get("ClientMissingCard")
        : StateDisplay;

    private string UpdateDisplay => Update?.Status switch
    {
        ClientUpdateStatus.Waiting => Strings.Get("UpdateCardWaiting"),
        ClientUpdateStatus.Updating => Strings.Get("UpdateCardUpdating"),
        ClientUpdateStatus.Updated => Strings.Get("UpdateCardUpdated"),
        ClientUpdateStatus.Unchanged => Strings.Get("UpdateCardSame"),
        ClientUpdateStatus.NoResponse => Strings.Get("UpdateCardFailed"),
        _ => ""
    };

    /// <summary>The optional note typed when the current session was started; empty if none.</summary>
    public string Note { get; set; } = "";

    /// <summary>Total of the items (drinks, snacks) added to the current session; 0 if none.</summary>
    public int ExtrasUzs { get; set; }

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
        Update is null ? "" : UpdateDisplay + (Update.Status == ClientUpdateStatus.Updated ? $" (v{Update.VersionBefore} → v{Update.VersionNow})" : ""),
        IsAuthRejected ? Strings.Get("AuthRejectedTooltip") : "",
        IsClientMissing ? Strings.Get("ClientMissingTooltip") : "",
        StateDisplay, // the card is narrow, so a long state name ("Лимит достигнут") can be cut off there
        TariffDisplay,
        AmountDisplay,
        StartDisplay != "" ? StartDisplay + (EndDisplay != "" ? " – " + EndDisplay : "") : "",
        Note,
        ExtrasUzs > 0 ? Strings.Format("ItemsTooltipFormat", ExtrasUzs.ToString("N0")) : "",
        ClientVersion != "" ? "v" + ClientVersion : ""
    }.Where(line => line != ""));

    /// <summary>Opened by staff with the unlock password and not billing: shown as "Repair".</summary>
    public bool IsRepair => State == PcState.ManualUnlocked;

    /// <summary>Card accent colour (SPEC 7): pink = PC on but client missing, red = offline (overrides everything else for networked PCs; manual resources are never "offline"), green = session running, blue = free, purple = repair.</summary>
    public Brush AccentBrush => IsClientMissing || IsAuthRejected
        ? Resource("StateNoClientBrush")
        : IsOffline
        ? Resource("StateOfflineBrush")
        : State switch
        {
            PcState.PrepaySession or PcState.PostpaySession => Resource("StateRunningBrush"),
            PcState.LimitReached => Resource("StateLimitBrush"),
            PcState.ManualUnlocked => Resource("StateRepairBrush"),
            _ => Resource("StateIdleBrush")
        };

    public Brush DotBrush => IsManual ? Brushes.Transparent : (IsOnline ? Resource("OnlineDotBrush") : Resource("OfflineDotBrush"));
}
