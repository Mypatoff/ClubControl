using System;
using System.IO;

namespace ClubControl.Client.Services;

/// <summary>
/// Persists this PC's auth token (see ClubHub.Heartbeat on the server) so it survives client
/// restarts - without this, every restart would look like a brand new, unprovisioned PC. Stored
/// next to the rest of ClubControl's local state; the client runs elevated (SPEC section 1: a
/// Task Scheduler task with highest privileges), so writing here needs no extra setup.
/// </summary>
public static class TokenStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClubControl", "client-token.dat");

    public static string? Load() =>
        File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : null;

    public static void Save(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, token);
    }
}
