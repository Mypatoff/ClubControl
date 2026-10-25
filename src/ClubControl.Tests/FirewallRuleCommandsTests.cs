using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class FirewallRuleCommandsTests
{
    [Fact]
    public void DeleteRuleArguments_MatchesByNameOnly_SoItRemovesAnyNumberOfStaleRules()
    {
        var args = FirewallRuleCommands.DeleteRuleArguments("ClubControl Server (TCP 5050)");

        Assert.Equal("advfirewall firewall delete rule name=\"ClubControl Server (TCP 5050)\"", args);
    }

    [Theory]
    [InlineData("ClubControl Server (TCP 5050)", "TCP", 5050)]
    [InlineData("ClubControl Discovery (UDP 5051)", "UDP", 5051)]
    public void AddAllowRuleArguments_ContainsExactlyTheExpectedRule(string name, string protocol, int port)
    {
        var args = FirewallRuleCommands.AddAllowRuleArguments(name, protocol, port);

        Assert.Equal(
            $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={protocol} localport={port} profile=private",
            args);
    }

    [Theory]
    [InlineData("TCP", 5050)]
    [InlineData("UDP", 5051)]
    public void AddAllowRuleArguments_NeverWidensBeyondAllowAndPrivateProfile(string protocol, int port)
    {
        // Regression guard: this rule must always stay narrow (allow-only, private-profile-only,
        // this exact port) - see CLAUDE.md's "never disable the firewall" rule and the history of
        // the club's old software breaking because of firewall misconfiguration.
        var args = FirewallRuleCommands.AddAllowRuleArguments("Rule", protocol, port);

        Assert.Contains("dir=in", args);
        Assert.Contains("action=allow", args);
        Assert.Contains("profile=private", args);
        Assert.DoesNotContain("action=block", args);
        Assert.DoesNotContain("profile=any", args);
        Assert.DoesNotContain("profile=public", args);
        Assert.DoesNotContain("profile=domain", args);
    }
}
