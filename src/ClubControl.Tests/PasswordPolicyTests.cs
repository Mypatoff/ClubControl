using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsEmptyOrWhitespace(string? password)
    {
        Assert.False(PasswordPolicy.IsValid(password, out var error));
        Assert.Equal("PasswordEmpty", error);
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("a1")]
    public void RejectsTooShort(string password)
    {
        Assert.False(PasswordPolicy.IsValid(password, out var error));
        Assert.Equal("PasswordTooShort", error);
    }

    [Theory]
    [InlineData("alllettersnodigit")]
    [InlineData("12345678")]
    public void RejectsMissingLetterOrDigit(string password)
    {
        Assert.False(PasswordPolicy.IsValid(password, out var error));
        Assert.Equal("PasswordNeedsLetterAndDigit", error);
    }

    [Theory]
    [InlineData("password1")]
    [InlineData("Club2026Control")]
    [InlineData("8characte1")]
    public void AcceptsPasswordsMeetingTheBar(string password)
    {
        Assert.True(PasswordPolicy.IsValid(password, out var error));
        Assert.Null(error);
    }
}
