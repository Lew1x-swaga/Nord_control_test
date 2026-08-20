using System.Net;
using System.Net.NetworkInformation;
using NordControl.Core.Helpers;
using Xunit;

namespace NordControl.Tests;

public class LanEndpointsTests
{
    [Fact]
    public void Broadcast_FromAddressAndMask()
    {
        var broadcast = LanEndpoints.GetBroadcast(
            IPAddress.Parse("192.168.68.101"),
            IPAddress.Parse("255.255.255.0"));

        Assert.Equal(IPAddress.Parse("192.168.68.255"), broadcast);
    }

    [Fact]
    public void ProbeDestinations_AlwaysIncludeLimitedBroadcastAndLoopback()
    {
        var dests = LanEndpoints.GetUdpProbeDestinations(47820);
        Assert.Contains(dests, e => e.Address.Equals(IPAddress.Broadcast) && e.Port == 47820);
        Assert.Contains(dests, e => e.Address.Equals(IPAddress.Loopback) && e.Port == 47820);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.1")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.68.101")]
    public void IsClassroomIpv4_AcceptsLoopbackAndRfc1918(string ip)
    {
        Assert.True(LanEndpoints.IsClassroomIpv4(ip));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("not-an-ip")]
    [InlineData("")]
    [InlineData(null)]
    public void IsClassroomIpv4_RejectsPublicWanLinkLocalAndGarbage(string? ip)
    {
        Assert.False(LanEndpoints.IsClassroomIpv4(ip));
    }

    [Theory]
    [InlineData(NetworkInterfaceType.Ethernet, true)]
    [InlineData(NetworkInterfaceType.Wireless80211, true)]
    [InlineData(NetworkInterfaceType.GigabitEthernet, true)]
    [InlineData(NetworkInterfaceType.Tunnel, false)]
    [InlineData(NetworkInterfaceType.Ppp, false)]
    [InlineData(NetworkInterfaceType.Loopback, false)]
    public void IsPreferredLanNic_PrefersEthernetAndWifi_NotTunnel(NetworkInterfaceType type, bool expected)
    {
        Assert.Equal(expected, LanEndpoints.IsPreferredLanNic(type));
    }

    [Fact]
    public void GetLocalUnicastIpv4_ExcludesTunnelPppLoopbackAndLinkLocal()
    {
        var ips = LanEndpoints.GetLocalUnicastIpv4();

        Assert.DoesNotContain(ips, IPAddress.IsLoopback);
        Assert.DoesNotContain(ips, ip => ip.ToString().StartsWith("169.254.", StringComparison.Ordinal));

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp))
            {
                continue;
            }

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    Assert.DoesNotContain(addr.Address, ips);
                }
            }
        }
    }

    [Fact]
    public void GetAnnounceIpv4_LoopbackProbe_ReturnsLocalhost()
    {
        Assert.Equal("127.0.0.1", LanEndpoints.GetAnnounceIpv4(IPAddress.Loopback));
        Assert.Equal("127.0.0.1", LanEndpoints.GetAnnounceIpv4(IPAddress.Parse("127.0.0.1")));
    }

    [Fact]
    public void GetAnnounceIpv4_LanProbe_DoesNotReturnTunnelIfWifiOrEthernetExists()
    {
        var announced = IPAddress.Parse(LanEndpoints.GetAnnounceIpv4(IPAddress.Parse("192.168.68.50")));
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp))
            {
                continue;
            }

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    Assert.NotEqual(addr.Address, announced);
                }
            }
        }
    }

    [Fact]
    public void SameIpv4Subnet_UsesActualMask_NotWholeTenDot()
    {
        var mask24 = IPAddress.Parse("255.255.255.0");
        Assert.True(LanEndpoints.SameIpv4Subnet(
            IPAddress.Parse("10.0.0.10"), mask24, IPAddress.Parse("10.0.0.50")));
        Assert.False(LanEndpoints.SameIpv4Subnet(
            IPAddress.Parse("10.0.0.10"), mask24, IPAddress.Parse("10.8.0.2")));
    }

    [Theory]
    [InlineData("Ethernet", false)]
    [InlineData("Wi-Fi", false)]
    [InlineData("WireGuard Tunnel", true)]
    [InlineData("NordLynx", true)]
    [InlineData("TAP-Windows Adapter V9", true)]
    [InlineData("OpenVPN TAP", true)]
    public void IsVpnAdapterName_DetectsCommonVpnNics(string name, bool expected)
    {
        Assert.Equal(expected, LanEndpoints.IsVpnAdapterName(name));
    }

    [Fact]
    public void SelectAnnounceIpv4_PicksLanTenDot_NotVpnTenDot()
    {
        var locals = new[]
        {
            new LanUnicast(IPAddress.Parse("10.8.0.2"), IPAddress.Parse("255.255.255.0"), false),
            new LanUnicast(IPAddress.Parse("10.0.0.10"), IPAddress.Parse("255.255.255.0"), true)
        };

        Assert.Equal("10.0.0.10", LanEndpoints.SelectAnnounceIpv4(IPAddress.Parse("10.0.0.50"), locals));
        Assert.Equal("10.8.0.2", LanEndpoints.SelectAnnounceIpv4(IPAddress.Parse("10.8.0.9"), locals));
    }

    [Fact]
    public void PickReachableTeacherIpv4_PrefersLanRemoteOverVpnAnnounce()
    {
        var locals = new[]
        {
            new LanUnicast(IPAddress.Parse("192.168.1.40"), IPAddress.Parse("255.255.255.0"), true),
            new LanUnicast(IPAddress.Parse("10.8.0.5"), IPAddress.Parse("255.255.255.0"), false)
        };

        var picked = LanEndpoints.PickReachableTeacherIpv4(
            "10.8.0.1",
            IPAddress.Parse("192.168.1.5"),
            locals);

        Assert.Equal("192.168.1.5", picked);
    }
}
