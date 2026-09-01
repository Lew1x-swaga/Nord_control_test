using System;
using System.Buffers.Binary;

namespace NordControl.Protocol;

public readonly record struct JpegFrame(uint Width, uint Height, ulong TimestampMs, byte[] Data)
{
    public const int HeaderSize = 16;

    public void WriteHeader(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], Width);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), Height);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(8, 8), TimestampMs);
    }

    public byte[] Encode()
    {
        var dataLen = Data?.Length ?? 0;
        var buffer = new byte[HeaderSize + dataLen];
        WriteHeader(buffer.AsSpan(0, HeaderSize));
        if (Data != null && dataLen > 0)
        {
            Data.CopyTo(buffer.AsSpan(HeaderSize));
        }
        return buffer;
    }

    public static JpegFrame? Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderSize)
            return null;

        var width = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        var timestampMs = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(8, 8));
        var dataLen = payload.Length - HeaderSize;
        var data = dataLen > 0 ? payload.Slice(HeaderSize).ToArray() : [];

        return new JpegFrame(width, height, timestampMs, data);
    }
}
