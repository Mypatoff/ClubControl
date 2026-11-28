namespace ClubControl.Shared;

/// <summary>
/// The short name an admin types when installing the client on a gaming PC ("no23"), so the admin panel can tell
/// the PCs apart before any of them has a number. The installer saves it in <see cref="FileName"/> under
/// %ProgramData%\ClubControl; the client sends it as the heartbeat's Hostname (instead of the Windows computer
/// name) and the server shows it on the card of a PC that has no number yet.
/// </summary>
public static class ClientName
{
    /// <summary>File in %ProgramData%\ClubControl that holds the name (UTF-8 text, one line).</summary>
    public const string FileName = "client-name.txt";

    /// <summary>Long enough for "Hall A - 23", short enough to stay readable in the admin panel.</summary>
    public const int MaxLength = 32;

    /// <summary>
    /// What to send for whatever the file contained: first line only, no control characters, trimmed, at most
    /// <see cref="MaxLength"/> characters. Empty when there is nothing usable (the caller then falls back to the
    /// Windows computer name).
    /// </summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var firstLine = raw.Split('\r', '\n')[0];
        var visible = new string(firstLine.Where(c => !char.IsControl(c)).ToArray()).Trim();

        return visible.Length > MaxLength ? visible[..MaxLength].TrimEnd() : visible;
    }
}
