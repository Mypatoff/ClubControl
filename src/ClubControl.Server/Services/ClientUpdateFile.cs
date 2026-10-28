using System;
using System.IO;

namespace ClubControl.Server.Services;

/// <summary>
/// Where the latest client build for "Update client" to hand out is kept: the whole client publish
/// output zipped, replaced by "Update server" (or by hand) each time. One fixed place, no versioning:
/// whatever is here right now is what gets sent to a PC when asked.
/// <para>
/// It lives next to the server program (<c>ClientUpdate\ClubControl.Client.zip</c> in the server's own
/// folder), because that is a folder the server's Windows user can write to - which is also what
/// "Update server" needs. A server in a protected folder (Program Files) cannot write there and keeps
/// using the older place, <c>%ProgramData%\ClubControl\ClientUpdate</c>, so that setup still works.
/// </para>
/// </summary>
public static class ClientUpdateFile
{
    private const string FileName = "ClubControl.Client.zip";

    /// <summary>Next to the server program.</summary>
    public static readonly string ServerFolderPath = Path.Combine(AppContext.BaseDirectory, "ClientUpdate", FileName);

    /// <summary>Where earlier versions kept it (and where a server in a protected folder still does).</summary>
    public static readonly string LegacyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClubControl", "ClientUpdate", FileName);

    /// <summary>The zip to hand out: the newer of the two places if both have one (a server that was moved or upgraded may still have the old copy).</summary>
    public static string FilePath => PickToServe(ServerFolderPath, LegacyPath);

    /// <summary>Where a new zip is written: next to the server if that folder is writable, otherwise the old place.</summary>
    public static string StagePath => PickToWrite(ServerFolderPath, LegacyPath);

    public static string PickToServe(string preferred, string fallback)
    {
        var preferredExists = File.Exists(preferred);
        var fallbackExists = File.Exists(fallback);

        if (preferredExists && fallbackExists)
            return File.GetLastWriteTimeUtc(fallback) > File.GetLastWriteTimeUtc(preferred) ? fallback : preferred;

        return fallbackExists ? fallback : preferred;
    }

    public static string PickToWrite(string preferred, string fallback)
    {
        try
        {
            var folder = Path.GetDirectoryName(preferred)!;
            Directory.CreateDirectory(folder);

            var probe = Path.Combine(folder, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return preferred;
        }
        catch (Exception)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
            return fallback;
        }
    }
}
