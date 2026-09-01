using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class JpegFrameCodecTests
{
    [Fact]
    public void Encode_and_decode_roundtrip_succeeds()
    {
        uint width = 1280;
        uint height = 720;
        ulong timestampMs = 1723814400000UL;
        byte[] fakeJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9 };

        var frame = new JpegFrame(width, height, timestampMs, fakeJpeg);
        var encoded = frame.Encode();

        Assert.NotNull(encoded);
        Assert.Equal(16 + fakeJpeg.Length, encoded.Length);

        var decoded = JpegFrame.Decode(encoded);
        Assert.NotNull(decoded);
        Assert.Equal(width, decoded!.Value.Width);
        Assert.Equal(height, decoded.Value.Height);
        Assert.Equal(timestampMs, decoded.Value.TimestampMs);
        Assert.Equal(fakeJpeg, decoded.Value.Data);
    }

    [Fact]
    public void Decode_header_is_big_endian()
    {
        uint width = 0x12345678;
        uint height = 0x0A0B0C0D;
        ulong timestamp = 0x0102030405060708UL;
        byte[] fakeJpeg = new byte[] { 0xAA, 0xBB };

        var frame = new JpegFrame(width, height, timestamp, fakeJpeg);
        var bytes = frame.Encode();

        // Big-endian checks
        Assert.Equal(0x12, bytes[0]);
        Assert.Equal(0x34, bytes[1]);
        Assert.Equal(0x56, bytes[2]);
        Assert.Equal(0x78, bytes[3]);

        Assert.Equal(0x0A, bytes[4]);
        Assert.Equal(0x0B, bytes[5]);
        Assert.Equal(0x0C, bytes[6]);
        Assert.Equal(0x0D, bytes[7]);

        Assert.Equal(0x01, bytes[8]);
        Assert.Equal(0x02, bytes[9]);
        Assert.Equal(0x03, bytes[10]);
        Assert.Equal(0x04, bytes[11]);
        Assert.Equal(0x05, bytes[12]);
        Assert.Equal(0x06, bytes[13]);
        Assert.Equal(0x07, bytes[14]);
        Assert.Equal(0x08, bytes[15]);

        Assert.Equal(0xAA, bytes[16]);
        Assert.Equal(0xBB, bytes[17]);
    }

    [Fact]
    public void Decode_too_short_payload_returns_null_or_throws()
    {
        byte[] shortBytes = new byte[15]; // less than 16-byte header
        var decoded = JpegFrame.Decode(shortBytes);
        Assert.Null(decoded);
    }

    [Fact]
    public void Decode_empty_jpeg_data_succeeds()
    {
        var frame = new JpegFrame(1920, 1080, 123456UL, Array.Empty<byte>());
        var bytes = frame.Encode();
        Assert.Equal(16, bytes.Length);

        var decoded = JpegFrame.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(1920u, decoded!.Value.Width);
        Assert.Equal(1080u, decoded.Value.Height);
        Assert.Equal(123456UL, decoded.Value.TimestampMs);
        Assert.Empty(decoded.Value.Data);
    }

    [Fact]
    public async Task WriteJpeg_matches_encode_then_write_without_concat_copy()
    {
        var frame = new JpegFrame(1280, 720, 1723814400000UL, [0xFF, 0xD8, 0xFF, 0xD9]);

        using var encoded = new MemoryStream();
        await FrameCodec.WriteAsync(encoded, ProtocolConstants.JpegMessageType, frame.Encode(), CancellationToken.None);

        using var direct = new MemoryStream();
        await FrameCodec.WriteJpegMessageAsync(direct, frame, CancellationToken.None);

        Assert.Equal(encoded.ToArray(), direct.ToArray());

        using var previewEncoded = new MemoryStream();
        await FrameCodec.WriteAsync(previewEncoded, ProtocolConstants.JpegPreviewMessageType, frame.Encode(), CancellationToken.None);

        using var previewDirect = new MemoryStream();
        await FrameCodec.WriteJpegPreviewMessageAsync(previewDirect, frame, CancellationToken.None);

        Assert.Equal(previewEncoded.ToArray(), previewDirect.ToArray());
    }
}
