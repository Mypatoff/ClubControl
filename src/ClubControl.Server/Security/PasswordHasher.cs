using System;
using System.Security.Cryptography;

namespace ClubControl.Server.Security;

/// <summary>PBKDF2 (HMAC-SHA256) hashing for the single admin password (SPEC section 7).</summary>
public static class PasswordHasher
{
    private const int IterationCount = 100_000;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    /// <summary>Returns "iterations.saltBase64.hashBase64", stored as-is in Setting.AdminPasswordHash.</summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, IterationCount, HashAlgorithmName.SHA256, HashSizeBytes);
        return $"{IterationCount}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations <= 0)
                return false;

            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            // storedHash is corrupted/not in the expected format - treat exactly like a wrong
            // password rather than letting a malformed Setting row crash the login dialog.
            return false;
        }
    }
}
