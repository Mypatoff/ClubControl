using System;
using System.IO;
using ClubControl.Shared;

namespace ClubControl.Client.Services;

/// <summary>
/// Reads the name the admin typed in the installer ("no23", see <see cref="ClientName"/>). The installer writes it to
/// %ProgramData%\ClubControl\client-name.txt; the client only ever reads it, so an admin can also change the name by
/// editing that file (it is read again on every heartbeat, no restart needed).
/// </summary>
public static class ClientNameStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClubControl", ClientName.FileName);

    /// <summary>The cleaned name, or "" when there is no file, it is empty or it cannot be read right now.</summary>
    public static string Load()
    {
        try
        {
            return File.Exists(FilePath) ? ClientName.Clean(File.ReadAllText(FilePath)) : "";
        }
        catch (Exception)
        {
            // Being edited at this moment, or locked: not worth an error, the next heartbeat tries again.
            return "";
        }
    }
}
