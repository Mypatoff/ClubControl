using System;
using System.Collections.Generic;
using ClubControl.Shared;

namespace ClubControl.Server.Data.Models;

/// <summary>
/// A billing session. Moving a session to another PC does not create a new Session row;
/// it closes the current SessionSegment and opens a new one (see SessionSegment).
/// </summary>
public class Session
{
    public int Id { get; set; }

    public int PcId { get; set; }
    public Pc? Pc { get; set; }

    public SessionType Type { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Active;

    public DateTime StartUtc { get; set; }
    public DateTime? PlannedEndUtc { get; set; }   // prepay only
    public DateTime? ActualEndUtc { get; set; }

    public int AmountUzs { get; set; }              // prepay/free: amount paid. Postpay: amount due, set when the session ends.
    public bool IsFree { get; set; }
    public string? Note { get; set; }

    /// <summary>The shift that was open when the session started. Null for sessions from before shifts existed.</summary>
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<SessionSegment> Segments { get; set; } = new();
    public List<SessionExtra> Extras { get; set; } = new();
}
