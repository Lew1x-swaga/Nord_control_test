using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class PreviewProtocolTests
{
    [Fact]
    public void Preview_constants_match_plan()
    {
        Assert.Equal(3, ProtocolConstants.JpegPreviewMessageType);
        Assert.Equal(320, ProtocolConstants.PreviewLongSideMax);
        Assert.Equal(2500, ProtocolConstants.PreviewIntervalMs);
        Assert.Equal(40, ProtocolConstants.PreviewJpegQuality);
        Assert.Equal(2, ProtocolConstants.JpegMessageType);
        Assert.NotEqual(ProtocolConstants.JpegMessageType, ProtocolConstants.JpegPreviewMessageType);
    }

    [Fact]
    public async Task WriteJpegPreviewMessageAsync_roundtrips_type3_JpegFrame()
    {
        uint width = 320;
        uint height = 180;
        ulong timestampMs = 1723814400000UL;
        byte[] fakeJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];

        var frame = new JpegFrame(width, height, timestampMs, fakeJpeg);
        using var ms = new MemoryStream();
        await FrameCodec.WriteJpegPreviewMessageAsync(ms, frame, CancellationToken.None);
        ms.Position = 0;

        var tcp = await FrameCodec.ReadAsync(ms, CancellationToken.None);
        Assert.NotNull(tcp);
        Assert.Equal(ProtocolConstants.JpegPreviewMessageType, tcp!.Value.Type);
        Assert.NotEqual(ProtocolConstants.JpegMessageType, tcp.Value.Type);

        var decoded = JpegFrame.Decode(tcp.Value.Payload);
        Assert.NotNull(decoded);
        Assert.Equal(width, decoded!.Value.Width);
        Assert.Equal(height, decoded.Value.Height);
        Assert.Equal(timestampMs, decoded.Value.TimestampMs);
        Assert.Equal(fakeJpeg, decoded.Value.Data);
    }

    [Fact]
    public void Serialize_and_deserialize_preview_enable()
    {
        var msg = new WireMessage { Type = "preview_enable" };
        AssertPreviewControlJson(msg, "preview_enable");
    }

    [Fact]
    public void Serialize_and_deserialize_preview_disable()
    {
        var msg = new WireMessage { Type = "preview_disable" };
        AssertPreviewControlJson(msg, "preview_disable");
    }

    private static void AssertPreviewControlJson(WireMessage msg, string expectedType)
    {
        var json = WireMessage.Serialize(msg);
        Assert.Contains("\"v\":1", json);
        Assert.Contains($"\"type\":\"{expectedType}\"", json);
        Assert.DoesNotContain("\"data\":", json);
        Assert.DoesNotContain("base64", json, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jpeg", json, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"width\"", json);
        Assert.DoesNotContain("\"height\"", json);

        var deserialized = WireMessage.Deserialize(json);
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized!.V);
        Assert.Equal(expectedType, deserialized.Type);

        var utf8Bytes = WireMessage.SerializeUtf8(msg);
        var utf8Deser = WireMessage.Deserialize(utf8Bytes);
        Assert.NotNull(utf8Deser);
        Assert.Equal(1, utf8Deser!.V);
        Assert.Equal(expectedType, utf8Deser.Type);
        Assert.Null(utf8Deser.Message);
    }
}
