using System;

namespace ClubControl.Server.Data.Models;

/// <summary>
/// One working shift: the stretch of time between staff pressing "Open shift" and "Close shift". A shift can
/// run across midnight, which is the whole point - a calendar day cannot describe it. Every session
/// belongs to the shift that was open when it started (<see cref="Session.ShiftId"/>).
/// </summary>
public class Shift
{
    public int Id { get; set; }
    public DateTime OpenedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Null while the shift is still open. At most one shift is open at a time.</summary>
    public DateTime? ClosedUtc { get; set; }
}
