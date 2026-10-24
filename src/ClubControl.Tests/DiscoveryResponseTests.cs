using ClubControl.Shared.Dto;
using Xunit;

namespace ClubControl.Tests;

public class DiscoveryResponseTests
{
    [Fact]
    public void RoundTrips_ThroughToStringAndTryParse()
    {
        var original = new DiscoveryResponse { ServerId = Guid.NewGuid(), ServerIp = "192.168.1.10", Port = 5050 };

        var parsed = DiscoveryResponse.TryParse(original.ToString());

        Assert.NotNull(parsed);
        Assert.Equal(original.ServerId, parsed!.ServerId);
        Assert.Equal(original.ServerIp, parsed.ServerIp);
        Assert.Equal(original.Port, parsed.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("CLUBCONTROL_SERVER|not-a-guid|192.168.1.10|5050")]
    [InlineData("CLUBCONTROL_SERVER|00000000-0000-0000-0000-000000000000|192.168.1.10|not-a-port")]
    [InlineData("WRONG_PREFIX|00000000-0000-0000-0000-000000000000|192.168.1.10|5050")]
    public void TryParse_RejectsInvalidInput(string text)
    {
        Assert.Null(DiscoveryResponse.TryParse(text));
    }
}
