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
    [InlineData("1232")]
    [InlineData("a1")]
    [InlineData("password1")]
    [InlineData("Club2026Control")]
    public void AcceptsPasswordsMeetingTheBar(string password)
    {
        Assert.True(PasswordPolicy.IsValid(password, out var error));
        Assert.Null(error);
    }
}
