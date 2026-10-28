using ClubControl.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Tests;

/// <summary>
/// The admin panel reads "the newest 200 events" and "the running sessions" every second. Without an index on
/// what they filter and sort by, each read walks the whole table and the panel slows down with every session
/// and event ever recorded (measured: 270 ms per refresh on a year of events; 2 ms with the index).
/// </summary>
public class QueryIndexTests : SessionTestBase
{
    private bool HasIndexOn<TEntity>(string property)
    {
        using var db = NewDb();
        return db.Model.FindEntityType(typeof(TEntity))!.GetIndexes()
            .Any(index => index.Properties.Count == 1 && index.Properties[0].Name == property);
    }

    [Fact]
    public void Events_are_indexed_by_time_for_the_newest_200_query() =>
        Assert.True(HasIndexOn<EventLogEntry>(nameof(EventLogEntry.TimestampUtc)));

    [Fact]
    public void Sessions_are_indexed_by_status_for_the_running_sessions_query() =>
        Assert.True(HasIndexOn<Session>(nameof(Session.Status)));
}
