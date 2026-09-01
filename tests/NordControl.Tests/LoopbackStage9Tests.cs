using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

[Collection("NetworkTests")]
public class LoopbackStage9Tests
{
    [Fact]
    public async Task Preview_enable_hd_skip_and_disable()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        await using var hub = new ClassHub(udpPort, tcpPort);
        await hub.StartClassAsync("Информатика-Stage9", "1234");

        var client1 = new ClassClient(
            pin: "1234",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-Stage9-01");
        var client2 = new ClassClient(
            pin: "1234",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-Stage9-02");

        byte[] previewJpeg1 = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0xFF, 0xD9];
        byte[] previewJpeg2 = [0xFF, 0xD8, 0xFF, 0xE0, 0x02, 0xFF, 0xD9];
        byte[] hdJpeg1 = [0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22, 0xFF, 0xD9];
        var previewFrame1 = new JpegFrame(64, 36, 1UL, previewJpeg1);
        var previewFrame2 = new JpegFrame(64, 36, 2UL, previewJpeg2);
        var hdFrame1 = new JpegFrame(1280, 720, 100UL, hdJpeg1);

        client1.CapturePreviewCallback = _ => Task.FromResult<JpegFrame?>(previewFrame1);
        client2.CapturePreviewCallback = _ => Task.FromResult<JpegFrame?>(previewFrame2);
        client1.CaptureFrameCallback = _ => Task.FromResult<JpegFrame?>(hdFrame1);

        var previewLock = new object();
        var previewCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var lastPreview = new Dictionary<string, JpegFrame>(StringComparer.Ordinal);
        var screenFrames = new List<(string id, JpegFrame frame)>();
        var streamTrueCount1 = 0;

        client1.StreamStateChanged += isStreaming =>
        {
            if (isStreaming)
            {
                Interlocked.Increment(ref streamTrueCount1);
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var runTask1 = Task.Run(() => client1.RunAsync(cts.Token));
        var runTask2 = Task.Run(() => client2.RunAsync(cts.Token));

        try
        {
            await WaitUntil.True(
                () => client1.Session.Status == SessionStatus.Online &&
                      client2.Session.Status == SessionStatus.Online &&
                      hub.Students.Count == 2);

            Assert.Equal(SessionStatus.Online, client1.Session.Status);
            Assert.Equal(SessionStatus.Online, client2.Session.Status);

            var student1Id = client1.Session.StudentId!;
            var student2Id = client2.Session.StudentId!;

            hub.PreviewFrameReceived += (sId, frame) =>
            {
                lock (previewLock)
                {
                    previewCounts[sId] = previewCounts.GetValueOrDefault(sId) + 1;
                    lastPreview[sId] = frame;
                }
            };

            hub.ScreenFrameReceived += (sId, frame) =>
            {
                lock (previewLock)
                {
                    screenFrames.Add((sId, frame));
                }
            };

            var enabled = await hub.BroadcastPreviewEnableAsync();
            Assert.Equal(2, enabled);

            await WaitUntil.True(() =>
            {
                lock (previewLock)
                {
                    return previewCounts.GetValueOrDefault(student1Id) >= 1 &&
                           previewCounts.GetValueOrDefault(student2Id) >= 1;
                }
            }, attempts: 120, delayMs: 50);

            lock (previewLock)
            {
                Assert.True(previewCounts[student1Id] >= 1);
                Assert.True(previewCounts[student2Id] >= 1);
                Assert.Equal(previewJpeg1, lastPreview[student1Id].Data);
                Assert.Equal(previewJpeg2, lastPreview[student2Id].Data);
                Assert.Empty(screenFrames);
            }

            Assert.True(client1.Session.PreviewEnabled);
            Assert.True(client2.Session.PreviewEnabled);
            Assert.True(client1.Session.ShouldPreview);
            Assert.True(client2.Session.ShouldPreview);
            Assert.False(client1.Session.ShouldCapture);
            Assert.False(client2.Session.ShouldCapture);
            Assert.Equal(0, Volatile.Read(ref streamTrueCount1));

            await hub.SelectStudentAsync(student1Id);

            await WaitUntil.True(() =>
            {
                lock (previewLock)
                {
                    return screenFrames.Exists(f => string.Equals(f.id, student1Id, StringComparison.Ordinal));
                }
            });

            JpegFrame hdReceived;
            lock (previewLock)
            {
                hdReceived = screenFrames.Find(f => string.Equals(f.id, student1Id, StringComparison.Ordinal)).frame;
            }

            Assert.Equal(hdJpeg1, hdReceived.Data);
            Assert.Equal(1280u, hdReceived.Width);
            Assert.True(client1.Session.ShouldCapture);
            Assert.True(client1.Session.ShouldPreview);
            Assert.True(client1.Session.PreviewEnabled);
            Assert.False(client2.Session.ShouldCapture);
            Assert.True(client2.Session.ShouldPreview);
            Assert.True(Volatile.Read(ref streamTrueCount1) >= 1);

            int s1AfterHd;
            int s2AfterHd;
            lock (previewLock)
            {
                s1AfterHd = previewCounts.GetValueOrDefault(student1Id);
                s2AfterHd = previewCounts.GetValueOrDefault(student2Id);
            }

            await Task.Delay(ProtocolConstants.PreviewIntervalMs + 400);
            lock (previewLock)
            {
                Assert.Equal(s1AfterHd, previewCounts.GetValueOrDefault(student1Id));
                Assert.DoesNotContain(screenFrames, f => string.Equals(f.id, student2Id, StringComparison.Ordinal));
            }

            await WaitUntil.True(() =>
            {
                lock (previewLock)
                {
                    return previewCounts.GetValueOrDefault(student2Id) > s2AfterHd;
                }
            }, attempts: 120, delayMs: 50);

            var disabled = await hub.BroadcastPreviewDisableAsync();
            Assert.Equal(2, disabled);

            await WaitUntil.True(() => !client2.Session.PreviewEnabled);

            int s2AfterDisable;
            lock (previewLock)
            {
                s2AfterDisable = previewCounts.GetValueOrDefault(student2Id);
            }

            await Task.Delay(ProtocolConstants.PreviewIntervalMs + 400);
            lock (previewLock)
            {
                Assert.Equal(s2AfterDisable, previewCounts.GetValueOrDefault(student2Id));
                Assert.Equal(s1AfterHd, previewCounts.GetValueOrDefault(student1Id));
            }

            Assert.False(client1.Session.PreviewEnabled);
            Assert.False(client2.Session.PreviewEnabled);
            Assert.False(client2.Session.ShouldPreview);
        }
        finally
        {
            cts.Cancel();
            try { await Task.WhenAll(runTask1, runTask2); } catch { }
            client1.Dispose();
            client2.Dispose();
        }
    }
}
