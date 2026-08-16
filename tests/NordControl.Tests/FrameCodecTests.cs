using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class FrameCodecTests
{
    [Fact]
    public async Task Roundtrip_json_payload()
    {
        using var ms = new MemoryStream();
        var payload = "{\"v\":1,\"type\":\"heartbeat\"}"u8.ToArray();
        await FrameCodec.WriteAsync(ms, ProtocolConstants.JsonMessageType, payload, CancellationToken.None);
        ms.Position = 0;
        var frame = await FrameCodec.ReadAsync(ms, CancellationToken.None);
        Assert.NotNull(frame);
        Assert.Equal(ProtocolConstants.JsonMessageType, frame!.Value.Type);
        Assert.Equal(payload, frame.Value.Payload);
    }

    [Fact]
    public async Task Too_large_payload_throws()
    {
        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await FrameCodec.WriteAsync(ms, 1, new byte[ProtocolConstants.MaxFramePayload + 1], CancellationToken.None);
        });
    }

    [Fact]
    public async Task Too_large_frame_length_on_read_throws()
    {
        using var ms = new MemoryStream();
        var len = ProtocolConstants.MaxFramePayload + 2;
        var header = new byte[5];
        header[0] = (byte)(len >> 24);
        header[1] = (byte)(len >> 16);
        header[2] = (byte)(len >> 8);
        header[3] = (byte)len;
        header[4] = 1;
        await ms.WriteAsync(header);
        ms.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await FrameCodec.ReadAsync(ms, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Zero_frame_length_on_read_throws()
    {
        using var ms = new MemoryStream();
        var header = new byte[5] { 0, 0, 0, 0, 1 };
        await ms.WriteAsync(header);
        ms.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await FrameCodec.ReadAsync(ms, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Incomplete_stream_returns_null()
    {
        using var ms = new MemoryStream();
        var result = await FrameCodec.ReadAsync(ms, CancellationToken.None);
        Assert.Null(result);
    }
}
