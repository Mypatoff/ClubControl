using ClubControl.Shared.Security;
using Xunit;

namespace ClubControl.Tests;

public class ClientUpdateAuthTests
{
    private static readonly byte[] Zip = { 1, 2, 3, 4, 5 };

    [Fact]
    public void ServerSignature_IsAcceptedByThePcThatOwnsTheToken()
    {
        var token = PcTokenHasher.GenerateToken();
        var sha = ClientUpdateAuth.ComputeSha256Hex(Zip);

        // What the server does (it only has the stored hash) vs what the client does (it has the token).
        var signature = ClientUpdateAuth.Sign(PcTokenHasher.Hash(token), sha);

        Assert.True(ClientUpdateAuth.Verify(token, sha, signature));
    }

    [Fact]
    public void Signature_ForADifferentPcsToken_IsRejected()
    {
        var sha = ClientUpdateAuth.ComputeSha256Hex(Zip);
        var signature = ClientUpdateAuth.Sign(PcTokenHasher.Hash(PcTokenHasher.GenerateToken()), sha);

        Assert.False(ClientUpdateAuth.Verify(PcTokenHasher.GenerateToken(), sha, signature));
    }

    [Fact]
    public void Signature_ForADifferentZip_IsRejected()
    {
        var token = PcTokenHasher.GenerateToken();
        var signature = ClientUpdateAuth.Sign(PcTokenHasher.Hash(token), ClientUpdateAuth.ComputeSha256Hex(Zip));

        var otherZipSha = ClientUpdateAuth.ComputeSha256Hex(new byte[] { 9, 9, 9 });

        Assert.False(ClientUpdateAuth.Verify(token, otherZipSha, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-valid-base64!!")]
    public void MissingOrMalformedSignature_IsRejectedWithoutThrowing(string? signature)
    {
        var token = PcTokenHasher.GenerateToken();

        Assert.False(ClientUpdateAuth.Verify(token, ClientUpdateAuth.ComputeSha256Hex(Zip), signature));
    }

    [Fact]
    public void ComputeSha256Hex_ChangesWhenTheZipChanges()
    {
        Assert.NotEqual(
            ClientUpdateAuth.ComputeSha256Hex(new byte[] { 1 }),
            ClientUpdateAuth.ComputeSha256Hex(new byte[] { 2 }));
    }
}
