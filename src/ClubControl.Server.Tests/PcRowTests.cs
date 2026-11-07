using System.ComponentModel;
using ClubControl.Shared;

namespace ClubControl.Server.Tests;

public class PcRowTests
{
    private static PcRow Row(string remaining = "10:00", int id = 1) => new()
    {
        Id = id,
        MachineGuid = Guid.Empty,
        IsOnline = true,
        Enabled = true,
        IsManual = false,
        Kind = ResourceKind.Computer,
        Number = 1,
        TariffId = 1,
        State = PcState.PrepaySession,
        KindIcon = "🖥",
        TariffDisplay = "Standard",
        StateDisplay = "Prepay",
        SessionTypeDisplay = "Prepay",
        AmountDisplay = "10 000 UZS",
        StartDisplay = "12:00:00",
        EndDisplay = "13:00:00",
        RemainingDisplay = remaining,
    };

    [Fact]
    public void A_card_takes_over_every_value_of_the_fresh_row()
    {
        var shown = Row("10:00");
        var fresh = Row("09:59");
        fresh.IsOnline = false;
        fresh.Note = "birthday";
        fresh.ExtrasUzs = 5_000;
        fresh.ClientMissing = true;
        fresh.AuthRejected = true;

        shown.CopyFrom(fresh);

        // Compared property by property, so a property added to PcRow later cannot be forgotten by CopyFrom unnoticed.
        foreach (var property in typeof(PcRow).GetProperties().Where(p => p.CanRead && p.CanWrite))
            Assert.Equal(property.GetValue(fresh), property.GetValue(shown));
    }

    [Fact]
    public void The_card_is_told_to_redraw_only_when_something_changed()
    {
        var shown = Row("10:00");
        var redraws = 0;
        shown.PropertyChanged += (_, _) => redraws++;

        shown.CopyFrom(Row("10:00"));
        Assert.Equal(0, redraws);

        shown.CopyFrom(Row("09:59"));
        Assert.True(redraws > 0);
    }

    [Fact]
    public void A_changing_time_or_amount_redraws_only_what_shows_them()
    {
        var shown = Row("10:00");
        var changed = new List<string?>();
        shown.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shown.CopyFrom(Row("09:59"));

        Assert.Equal(new List<string?> { nameof(PcRow.PrimaryDisplay), nameof(PcRow.TooltipText) }, changed);
    }

    [Fact]
    public void Any_other_change_redraws_the_whole_card_including_the_computed_properties()
    {
        var shown = Row();
        var changed = new List<string?>();
        shown.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var fresh = Row();
        fresh.IsOnline = false; // colour and state text are computed from this
        shown.CopyFrom(fresh);

        Assert.Equal(new List<string?> { string.Empty }, changed);
    }

    [Fact]
    public void A_time_change_together_with_another_change_redraws_the_whole_card()
    {
        var shown = Row("10:00");
        var changed = new List<string?>();
        shown.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var fresh = Row("09:59");
        fresh.Note = "birthday";
        shown.CopyFrom(fresh);

        Assert.Equal(new List<string?> { string.Empty }, changed);
    }
}
