using ClubControl.Server.Data;
using ClubControl.Shared;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

public class ClientSettingsCommandTests : SessionTestBase
{
    [Fact]
    public async Task ApplySettingsCommand_CarriesTheUnlockPasswordHash()
    {
        using (var db = NewDb())
        {
            var setting = await SettingsHelper.GetOrCreateAsync(db);
            setting.ClientUnlockPasswordHash = "hash-from-settings";
            await db.SaveChangesAsync();
        }

        using var readDb = NewDb();
        var command = await ServiceFor(readDb).BuildApplySettingsCommandAsync();

        Assert.Equal(CommandType.ApplySettings, command.Type);
        Assert.Equal("hash-from-settings", command.Settings!.UnlockPasswordHash);
    }

    [Fact]
    public async Task PostpayResyncCommand_CarriesWhatTheClientNeedsToKeepTheCostGrowing()
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc1);
        await BackdateSessionAsync(TimeSpan.FromSeconds(29 * 60 + 30)); // 30 started minutes at 10 000/h = 5 000

        using var readDb = NewDb();
        var pc = await readDb.Pcs.SingleAsync(p => p.MachineGuid == Pc1);
        var session = (await ServiceFor(readDb).BuildCurrentCommandAsync(pc))!.Session!;

        Assert.Equal(StandardPrice, session.PricePerHourUzs);
        Assert.Equal(StandardMin, session.MinPaymentUzs);
        Assert.Equal(StandardMax, session.MaxPaymentUzs);
        Assert.Equal(0, session.ClosedSegmentsCostUzs);
        Assert.InRange(session.OpenSegmentElapsedSeconds, 29 * 60 + 30, 29 * 60 + 40);

        // The client's extrapolation agrees with the cost the server reports in the same message.
        Assert.Equal(session.CurrentCostUzs, BillingCalculator.PostpayCostWithOpenSegment(
            session.ClosedSegmentsCostUzs, session.OpenSegmentElapsedSeconds, session.PricePerHourUzs, session.MinPaymentUzs));
    }

    [Fact]
    public async Task PostpayResyncCommand_AfterAMove_KeepsTheOldSegmentsCostAndTheSamePrice()
    {
        using (var db = NewDb())
            await ServiceFor(db).StartPostpayAsync(Pc1);
        await BackdateSessionAsync(TimeSpan.FromSeconds(29 * 60 + 30)); // 5 000 so far
        using (var db = NewDb())
            await ServiceFor(db).MoveSessionAsync(Pc1, Pc2);

        using var readDb = NewDb();
        var pc = await readDb.Pcs.SingleAsync(p => p.MachineGuid == Pc2);
        var session = (await ServiceFor(readDb).BuildCurrentCommandAsync(pc))!.Session!;

        Assert.Equal(5_000, session.ClosedSegmentsCostUzs);
        Assert.Equal(StandardPrice, session.PricePerHourUzs);
    }

    [Fact]
    public async Task ApplySettingsCommand_WithNoUnlockPasswordSet_HasNoHash()
    {
        using var db = NewDb();
        var command = await ServiceFor(db).BuildApplySettingsCommandAsync();

        Assert.Null(command.Settings!.UnlockPasswordHash);
    }
}
