using ClubControl.Shared.Security;
using Xunit;

namespace ClubControl.Tests;

public class PcTokenHasherTests
{
    [Fact]
    public void GenerateToken_ProducesDifferentTokensEachTime()
    {
        var a = PcTokenHasher.GenerateToken();
        var b = PcTokenHasher.GenerateToken();

        Assert.NotEqual(a, b);
        Assert.NotEmpty(a);
    }

    [Fact]
    public void Hash_ThenVerify_WithTheSameToken_Succeeds()
    {
        var token = PcTokenHasher.GenerateToken();
        var hash = PcTokenHasher.Hash(token);

        Assert.True(PcTokenHasher.Verify(token, hash));
    }

    [Fact]
    public void Verify_WithADifferentToken_Fails()
    {
        var hash = PcTokenHasher.Hash(PcTokenHasher.GenerateToken());
        var wrongToken = PcTokenHasher.GenerateToken();

        Assert.False(PcTokenHasher.Verify(wrongToken, hash));
    }

    [Fact]
    public void Hash_NeverStoresThePlaintextToken()
    {
        var token = PcTokenHasher.GenerateToken();
        var hash = PcTokenHasher.Hash(token);

        Assert.NotEqual(token, hash);
    }

    [Fact]
    public void Verify_WithACorruptedStoredHash_FailsInsteadOfThrowing()
    {
        Assert.False(PcTokenHasher.Verify("whatever", "not-valid-base64!!"));
    }

    [Fact]
    public void Verify_WithAnEmptyStoredHash_Fails()
    {
        Assert.False(PcTokenHasher.Verify("whatever", ""));
    }
}
