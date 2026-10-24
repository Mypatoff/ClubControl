using System;
using ClubControl.Shared;

namespace ClubControl.Server.Data.Models;

/// <summary>One row shown in the admin panel's event log, also kept permanently in the database.</summary>
public class EventLogEntry
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public EventType Type { get; set; }
    public string Message { get; set; } = "";

    public int? PcId { get; set; }
    public int? SessionId { get; set; }
}
