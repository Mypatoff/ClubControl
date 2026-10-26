using System;
using System.IO;

namespace ClubControl.Client.Services;

/// <summary>
/// Keeps the hash of the staff unlock password on disk (the server sends it with the settings).
/// Without a local copy the unlock hotkey would be useless exactly when it is needed most: right
/// after a reboot with the server down. Only the PBKDF2 hash is stored, never the password itself.
/// </summary>
public static class UnlockPasswordStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClubControl", "client-unlock.dat");

    public static string? Load()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Saves the hash, or removes the file when the server has no unlock password set.</summary>
    public static void Save(string? hash)
    {
        try
        {
            if (string.IsNullOrEmpty(hash))
            {
                File.Delete(FilePath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, hash);
        }
        catch (IOException)
        {
            // Cannot persist right now - the in-memory copy still works until the next restart.
        }
    }
}
