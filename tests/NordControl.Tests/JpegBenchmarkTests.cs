using System;
using System.Diagnostics;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class JpegBenchmarkTests
{
    [Fact]
    public void JpegFrame_EncodeAndDecode_Benchmark_ExecutesUnder15MillisecondsPerFrame()
    {
        // 1280x720 typical JPEG frame payload is ~60-150 KB
        const int payloadSize = 100 * 1024; // 100 KB
        var sampleJpegData = new byte[payloadSize];
        new Random(42).NextBytes(sampleJpegData);

        // Warm up JIT
        var warmupFrame = new JpegFrame(1280, 720, 1000UL, sampleJpegData);
        var warmupBytes = warmupFrame.Encode();
        _ = JpegFrame.Decode(warmupBytes);

        // Benchmark loop: 200 iterations
        const int iterations = 200;
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            var frame = new JpegFrame(1280, 720, (ulong)i, sampleJpegData);
            var encoded = frame.Encode();
            var decoded = JpegFrame.Decode(encoded);

            Assert.NotNull(decoded);
            Assert.Equal(1280u, decoded!.Value.Width);
            Assert.Equal(720u, decoded.Value.Height);
            Assert.Equal((ulong)i, decoded.Value.TimestampMs);
        }

        stopwatch.Stop();

        var elapsedTotalMs = stopwatch.Elapsed.TotalMilliseconds;
        var avgPerFrameMs = elapsedTotalMs / iterations;

        // Target requirement: < 15 ms per frame for 1280x720
        Assert.True(avgPerFrameMs < 15.0, $"Average frame encode/decode time was {avgPerFrameMs:F4} ms, expected < 15.0 ms");
    }
}
