using System.Net;
using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class LocalNetworkInterfacesTests
{
    private static readonly IPAddress Mask24 = IPAddress.Parse("255.255.255.0");
    private static readonly IPAddress Mask8 = IPAddress.Parse("255.0.0.0");

    private static (IPAddress Address, IPAddress Mask) Nic(string address, IPAddress mask) => (IPAddress.Parse(address), mask);

    [Fact]
    public void AskerOnTheLan_GetsTheLanAddress_EvenWhenAVpnAdapterIsListedFirst()
    {
        // The bug: "first adapter" handed out the Radmin VPN address to a PC that cannot route to it.
        var candidates = new[] { Nic("26.10.20.30", Mask8), Nic("192.168.1.5", Mask24) };

        var chosen = LocalNetworkInterfaces.ChooseAddressFor(IPAddress.Parse("192.168.1.77"), candidates);

        Assert.Equal(IPAddress.Parse("192.168.1.5"), chosen);
    }

    [Fact]
    public void AskerOnTheVpn_GetsTheVpnAddress()
    {
        var candidates = new[] { Nic("192.168.1.5", Mask24), Nic("26.10.20.30", Mask8) };

        var chosen = LocalNetworkInterfaces.ChooseAddressFor(IPAddress.Parse("26.99.1.1"), candidates);

        Assert.Equal(IPAddress.Parse("26.10.20.30"), chosen);
    }

    [Fact]
    public void AskerOnASubnetNoAdapterIsOn_FallsBackToTheFirstRealAddress()
    {
        var candidates = new[] { Nic("169.254.7.7", Mask24), Nic("192.168.1.5", Mask24) };

        var chosen = LocalNetworkInterfaces.ChooseAddressFor(IPAddress.Parse("10.0.0.9"), candidates);

        Assert.Equal(IPAddress.Parse("192.168.1.5"), chosen);
    }

    [Fact]
    public void OnlyLinkLocalAddressesExist_StillAnswersWithOne()
    {
        var candidates = new[] { Nic("169.254.7.7", Mask24) };

        var chosen = LocalNetworkInterfaces.ChooseAddressFor(IPAddress.Parse("10.0.0.9"), candidates);

        Assert.Equal(IPAddress.Parse("169.254.7.7"), chosen);
    }

    [Fact]
    public void NoAddressesAtAll_ReturnsNull()
    {
        Assert.Null(LocalNetworkInterfaces.ChooseAddressFor(IPAddress.Parse("192.168.1.77"), Array.Empty<(IPAddress, IPAddress)>()));
    }

    [Fact]
    public void IPv4MappedIPv6Asker_IsMatchedAsIPv4()
    {
        var candidates = new[] { Nic("26.10.20.30", Mask8), Nic("192.168.1.5", Mask24) };
        var asker = IPAddress.Parse("192.168.1.77").MapToIPv6();

        Assert.Equal(IPAddress.Parse("192.168.1.5"), LocalNetworkInterfaces.ChooseAddressFor(asker, candidates));
    }
}
