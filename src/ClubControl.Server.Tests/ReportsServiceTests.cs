using ClubControl.Server.Data.Models;
using ClubControl.Server.Services;
using ClubControl.Shared;

namespace ClubControl.Server.Tests;

/// <summary>Reports for named "Other" resources (SPEC extension: PS5 room, billiard table, ...).</summary>
public class ReportsServiceTests : SessionTestBase
{
    [Fact]
    public async Task A_named_other_resource_is_reported_under_its_own_kind_and_name()
    {
        using (var setup = NewDb())
        {
            var tariff = new Tariff { Name = "Table", Kind = ResourceKind.Other, PricePerHourUzs = StandardPrice, MinPaymentUzs = StandardMin, MaxPaymentUzs = StandardMax };
            setup.Tariffs.Add(tariff);
            await setup.SaveChangesAsync();
            setup.Pcs.Add(new Pc { MachineGuid = OtherGuid, Number = 10, Name = "PS5 room", Kind = ResourceKind.Other, IsManual = true, TariffId = tariff.Id });
            await setup.SaveChangesAsync();
        }

        using (var db = NewDb())
        {
            await ServiceFor(db).StartPrepayByMoneyAsync(OtherGuid, 5_000);
            await ServiceFor(db).StartPrepayByMoneyAsync(Pc1, 3_000);
        }

        using var reportDb = NewDb();
        var report = await new ReportsService(reportDb).BuildReportAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), ResourceKind.Other);

        var row = Assert.Single(report.ByPc);
        Assert.Equal("PS5 room", row.PcName);
        Assert.Equal(10, row.PcNumber);
        Assert.Equal(5_000, row.IncomeUzs);
    }

    [Fact]
    public async Task Sessions_list_shows_which_pc_started_when_and_whether_it_has_ended()
    {
        using (var db = NewDb())
        {
            await ServiceFor(db).StartPrepayByMoneyAsync(Pc1, 3_000, note: "birthday");
            await ServiceFor(db).StartPostpayAsync(Pc2);
        }

        using (var db = NewDb())
            await ServiceFor(db).EndSessionEarlyAsync(Pc2);

        using var reportDb = NewDb();
        var report = await new ReportsService(reportDb).BuildReportAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), ResourceKind.Computer);

        Assert.Equal(2, report.Sessions.Count);

        var running = Assert.Single(report.Sessions, s => s.Note == "birthday");
        Assert.Null(running.EndUtc);
        Assert.Equal(3_000, running.AmountUzs);
        Assert.InRange(running.StartUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        var ended = Assert.Single(report.Sessions, s => s.Type == SessionType.Postpay);
        Assert.NotNull(ended.EndUtc);
        Assert.True(report.Sessions[0].StartUtc >= report.Sessions[1].StartUtc, "newest session first");
    }
}
