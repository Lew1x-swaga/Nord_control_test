using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NordControl.Protocol;

public readonly record struct TcpFrame(byte Type, byte[] Payload);

public static class FrameCodec
{
    public static async Task WriteAsync(Stream stream, byte type, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length > ProtocolConstants.MaxFramePayload)
            throw new InvalidDataException("payload too large");

        var len = 1 + payload.Length;
        var header = new byte[5];
        header[0] = (byte)(len >> 24);
        header[1] = (byte)(len >> 16);
        header[2] = (byte)(len >> 8);
        header[3] = (byte)len;
        header[4] = type;

        await stream.WriteAsync(header, ct);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, ct);
        }
        await stream.FlushAsync(ct);
    }

    public static async Task<TcpFrame?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[5];
        if (!await ReadExactAsync(stream, header, ct))
            return null;

        var len = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        if (len < 1 || len > ProtocolConstants.MaxFramePayload + 1)
            throw new InvalidDataException("bad length");

        var type = header[4];
        var payloadLen = len - 1;
        var payload = new byte[payloadLen];
        if (payloadLen > 0 && !await ReadExactAsync(stream, payload, ct))
            return null;

        return new TcpFrame(type, payload);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var off = 0;
        while (off < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(off), ct);
            if (n == 0) return false;
            off += n;
        }
        return true;
    }
}
