using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class UdpPacketsTests
{
    [Fact]
    public void Probe_format_is_correct()
    {
        var probe = UdpPackets.Probe();
        Assert.Equal("NORD1|probe|v=1", probe);
    }

    [Fact]
    public void Announce_format_is_correct()
    {
        var announce = UdpPackets.Announce("Класс 10A", "192.168.1.100", 47821);
        Assert.Equal("NORD1|announce|v=1|name=Класс 10A|ip=192.168.1.100|tcp=47821", announce);
    }

    [Fact]
    public void Announce_does_not_contain_pin()
    {
        var announce = UdpPackets.Announce("Класс", "192.168.0.5", 47821);
        Assert.DoesNotContain("pin=", announce);
    }

    [Fact]
    public void TryParseAnnounce_valid_returns_true_and_extracts_fields()
    {
        var packet = "NORD1|announce|v=1|name=Информатика|ip=10.0.0.5|tcp=47821";
        var success = UdpPackets.TryParseAnnounce(packet, out var name, out var ip, out var tcpPort);
        Assert.True(success);
        Assert.Equal("Информатика", name);
        Assert.Equal("10.0.0.5", ip);
        Assert.Equal(47821, tcpPort);
    }

    [Theory]
    [InlineData("NORD1|probe|v=1")]
    [InlineData("OTHER|announce|v=1|name=Test|ip=127.0.0.1|tcp=47821")]
    [InlineData("NORD1|announce|v=2|name=Test|ip=127.0.0.1|tcp=47821")]
    [InlineData("NORD1|announce|v=1|name=Test|ip=127.0.0.1|tcp=badport")]
    [InlineData("garbage text")]
    [InlineData("")]
    public void TryParseAnnounce_invalid_returns_false(string packet)
    {
        var success = UdpPackets.TryParseAnnounce(packet, out var name, out var ip, out var tcpPort);
        Assert.False(success);
    }

    [Fact]
    public void WireMessage_serialization_roundtrip()
    {
        var msg = new WireMessage
        {
            V = 1,
            Type = "join_class",
            Pin = "1234",
            DisplayName = "ПК-01",
            Hostname = "STUDENT-PC",
            AgentVersion = "0.1.0"
        };

        var json = WireMessage.Serialize(msg);
        Assert.Contains("\"type\":\"join_class\"", json);
        Assert.Contains("\"display_name\":\"ПК-01\"", json);
        Assert.DoesNotContain("\"session_token\"", json); // null ignored

        var deserialized = WireMessage.Deserialize(json);
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized!.V);
        Assert.Equal("join_class", deserialized.Type);
        Assert.Equal("1234", deserialized.Pin);
        Assert.Equal("ПК-01", deserialized.DisplayName);
    }
}
