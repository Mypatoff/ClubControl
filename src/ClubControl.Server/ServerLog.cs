using System;
using System.IO;
using ClubControl.Server.Data;

namespace ClubControl.Server;

/// <summary>
/// Appends a timestamped line to <c>%ProgramData%\ClubControl\server-log.txt</c> so an unexpected error
/// leaves a trace even though the server now keeps running after it (the admin panel itself only shows
/// a short notice). One flat file; when it passes 2 MB it is renamed to <c>server-log.old.txt</c> (replacing
/// the previous one) so an error that repeats every second cannot fill the disk.
/// </summary>
public static class ServerLog
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly string FilePath = Path.Combine(DbPaths.DataDirectory, "server-log.txt");

    private static readonly object WriteLock = new();

    public static void Error(string context, Exception ex) => Append(context, ex.ToString());

    public static void Info(string context, string message) => Append(context, message);

    private static void Append(string context, string text)
    {
        try
        {
            lock (WriteLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.txt"), overwrite: true);

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{context}] {text}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never be the reason something crashes - this runs inside exception handlers.
        }
    }
}
