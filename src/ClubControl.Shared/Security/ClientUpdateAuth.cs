using System;
using System.Security.Cryptography;
using System.Text;

namespace ClubControl.Shared.Security;

/// <summary>
/// Proves that an "Update client" command really came from the server this PC was provisioned by.
/// Without it, anyone on the club LAN who answered the UDP discovery broadcast could hand a client
/// a zip and have it run with the highest privileges.
///
/// The server signs the update zip's SHA-256 with an HMAC whose key is this PC's stored token hash
/// (Pc.AuthTokenHash). The token hash never travels over the network after provisioning, so only
/// the real server and the real PC can produce / check the signature; the PC derives the same key
/// from its own plaintext token with <see cref="PcTokenHasher.Hash"/>.
/// </summary>
public static class ClientUpdateAuth
{
    public static string ComputeSha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>Server side: signs the zip's hash for one PC, using that PC's stored token hash as the key.</summary>
    public static string Sign(string tokenHash, string sha256Hex) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(tokenHash), Encoding.UTF8.GetBytes(sha256Hex)));

    /// <summary>Client side: checks the signature using the PC's own plaintext token.</summary>
    public static bool Verify(string token, string sha256Hex, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(sha256Hex))
            return false;

        try
        {
            var expected = Convert.FromBase64String(Sign(PcTokenHasher.Hash(token), sha256Hex));
            var actual = Convert.FromBase64String(signature);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            // Not valid base64 - same as a wrong signature, never a crash.
            return false;
        }
    }
}
