using System;
using System.IO;

namespace ClubControl.Client.Services;

/// <summary>
/// Appends a timestamped line to a local log file so unattended failures leave a trace - nobody is
/// at the kiosk to see a MessageBox, and some failures (see ServerDiscoveryClient's retry loop,
/// started fire-and-forget) have no UI at all to show one in. Deliberately one flat file with no
/// rotation: these PCs rarely log more than a handful of lines a month, and a failure is worth
/// keeping, not something to manage.
/// </summary>
public static class ClientLog
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClubControl", "client-log.txt");

    private static readonly object WriteLock = new();

    public static void Error(string context, Exception ex) => Append(context, ex.ToString());

    private static readonly System.Collections.Generic.Dictionary<string, DateTime> LastLogged = new();

    /// <summary>
    /// Like <see cref="Error"/>, but the same error is written at most once a minute: one that repeats on
    /// every timer tick would otherwise fill the file (and the disk) on a PC nobody looks at.
    /// </summary>
    public static void ErrorThrottled(string context, Exception ex)
    {
        var key = context + ex.GetType().Name + ex.Message;
        lock (WriteLock)
        {
            if (LastLogged.TryGetValue(key, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(1))
                return;

            LastLogged[key] = DateTime.UtcNow;
        }

        Error(context, ex);
    }

    public static void Info(string context, string message) => Append(context, message);

    private static void Append(string context, string text)
    {
        try
        {
            lock (WriteLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{context}] {text}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never be the reason something crashes or gets stuck - this runs inside
            // exception handlers themselves, including the app-wide unhandled-exception one.
        }
    }
}
