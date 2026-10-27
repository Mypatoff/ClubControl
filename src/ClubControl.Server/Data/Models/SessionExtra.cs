using System;

namespace ClubControl.Server.Data.Models;

/// <summary>
/// Something the client bought during a session (a drink, a snack...) that is paid on top of the
/// time charge. It belongs to the Session, so it follows the session when it is moved to another
/// PC, and it never counts toward the tariff's minimum/maximum.
/// </summary>
public class SessionExtra
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public Session? Session { get; set; }

    public string Description { get; set; } = "";
    public int AmountUzs { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
