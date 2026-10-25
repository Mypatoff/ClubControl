using System;
using System.Security.Cryptography;
using System.Text;

namespace ClubControl.Shared.Security;

/// <summary>
/// Hashing for a gaming PC's per-machine authentication token (ClubHub.Heartbeat - see the hub
/// for why this exists). Unlike the admin password, this secret is a random 256-bit value chosen
/// by the server itself, not something a human picks - so a fast, fixed-time-compared SHA-256
/// hash is appropriate; there is no low-entropy secret here for PBKDF2's deliberate slowness to
/// defend against.
/// </summary>
public static class PcTokenHasher
{
    private const int TokenSizeBytes = 32;

    /// <summary>A fresh, high-entropy token to hand to a PC the one time it is provisioned.</summary>
    public static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenSizeBytes));

    /// <summary>What gets stored in Pc.AuthTokenHash - the plaintext token itself is never persisted.</summary>
    public static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool Verify(string token, string storedHash)
    {
        try
        {
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var expected = Convert.FromBase64String(storedHash);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            // storedHash is corrupted/not in the expected format - treat exactly like a wrong
            // token rather than letting a malformed Pc row crash heartbeat handling.
            return false;
        }
    }
}
