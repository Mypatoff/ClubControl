using System;

namespace ClubControl.Server.Data.Models;

/// <summary>One PC-and-price slice of a session. A "Move" closes one segment and opens the next.</summary>
public class SessionSegment
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public Session? Session { get; set; }

    public int PcId { get; set; }
    public Pc? Pc { get; set; }

    public int PricePerHourUzs { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
}
