using ClubControl.Shared.Security;
using Xunit;

namespace ClubControl.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_WithTheSamePassword_Succeeds()
    {
        var hash = PasswordHasher.Hash("secret-123");

        Assert.True(PasswordHasher.Verify("secret-123", hash));
    }

    [Fact]
    public void Verify_WithAWrongPassword_Fails()
    {
        var hash = PasswordHasher.Hash("secret-123");

        Assert.False(PasswordHasher.Verify("secret-124", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Hash_OfTheSamePasswordTwice_DiffersBecauseOfTheSalt()
    {
        Assert.NotEqual(PasswordHasher.Hash("secret-123"), PasswordHasher.Hash("secret-123"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("100000.!!!.!!!")]
    [InlineData("0.AAAA.AAAA")]
    public void Verify_WithACorruptedStoredHash_ReturnsFalseInsteadOfThrowing(string storedHash)
    {
        Assert.False(PasswordHasher.Verify("anything", storedHash));
    }
}
