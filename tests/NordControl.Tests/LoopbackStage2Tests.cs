using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

[Collection("NetworkTests")]
public class LoopbackStage2Tests
{
    [Fact]
    public async Task Full_stage2_loopback_stream_and_process_lifecycle()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        await using var hub = new ClassHub(udpPort, tcpPort);
        await hub.StartClassAsync("Информатика", "5678");

        var client1 = new ClassClient(
            pin: "5678",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-01"
        );

        var client2 = new ClassClient(
            pin: "5678",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-02"
        );

        byte[] fakeJpeg1 = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22, 0xFF, 0xD9 };
        var frame1 = new JpegFrame(1280, 720, 100UL, fakeJpeg1);

        client1.CaptureFrameCallback = (ct) => Task.FromResult<JpegFrame?>(frame1);
        client1.ProcessListCallback = () => new WireMessage
        {
            V = ProtocolConstants.Version,
            Type = "process_list",
            ActiveExe = "browser.exe",
            Items = new List<ProcessItemInfo>
            {
                new() { Exe = "browser.exe", Pid = 100, Title = "Учебник" }
            }
        };

        byte[] fakeJpeg2 = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x33, 0x44, 0xFF, 0xD9 };
        var frame2 = new JpegFrame(1024, 768, 200UL, fakeJpeg2);
        client2.CaptureFrameCallback = (ct) => Task.FromResult<JpegFrame?>(frame2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var runTask1 = Task.Run(() => client1.RunAsync(cts.Token));
        var runTask2 = Task.Run(() => client2.RunAsync(cts.Token));

        // 1. Wait for both clients to join
        for (int i = 0; i < 60; i++)
        {
            if (client1.Session.Status == SessionStatus.Online &&
                client2.Session.Status == SessionStatus.Online &&
                hub.Students.Count == 2)
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.Equal(SessionStatus.Online, client1.Session.Status);
        Assert.Equal(SessionStatus.Online, client2.Session.Status);

        var student1Id = client1.Session.StudentId!;
        var student2Id = client2.Session.StudentId!;

        // 2. Setup Hub events
        var frameReceivedTcs = new TaskCompletionSource<JpegFrame>();
        var processReceivedTcs = new TaskCompletionSource<WireMessage>();

        hub.ScreenFrameReceived += (sId, frame) =>
        {
            if (sId == student1Id)
            {
                frameReceivedTcs.TrySetResult(frame);
            }
        };

        hub.ProcessListReceived += (sId, msg) =>
        {
            if (sId == student1Id)
            {
                processReceivedTcs.TrySetResult(msg);
            }
        };

        // 3. Select Student 1
        await hub.SelectStudentAsync(student1Id);

        var completedFrame = await Task.WhenAny(frameReceivedTcs.Task, Task.Delay(3000));
        Assert.Same(frameReceivedTcs.Task, completedFrame);
        var receivedFrame = await frameReceivedTcs.Task;
        Assert.Equal(1280u, receivedFrame.Width);
        Assert.Equal(720u, receivedFrame.Height);
        Assert.Equal(fakeJpeg1, receivedFrame.Data);

        var completedProc = await Task.WhenAny(processReceivedTcs.Task, Task.Delay(4000));
        Assert.Same(processReceivedTcs.Task, completedProc);
        var receivedProc = await processReceivedTcs.Task;
        Assert.Equal("browser.exe", receivedProc.ActiveExe);
        Assert.Single(receivedProc.Items!);
        Assert.Equal("browser.exe", receivedProc.Items![0].Exe);

        // Verify Student 2 is NOT streaming
        Assert.False(client2.Session.StreamEnabled);
        Assert.True(client1.Session.StreamEnabled);

        // 4. Switch to Student 2
        await hub.SelectStudentAsync(student2Id);

        // Wait for switch propagation
        for (int i = 0; i < 40; i++)
        {
            if (!client1.Session.StreamEnabled && client2.Session.StreamEnabled)
                break;
            await Task.Delay(50);
        }

        Assert.False(client1.Session.StreamEnabled);
        Assert.True(client2.Session.StreamEnabled);

        // 5. Deselect all students
        await hub.SelectStudentAsync(null);
        for (int i = 0; i < 40; i++)
        {
            if (!client2.Session.StreamEnabled)
                break;
            await Task.Delay(50);
        }
        Assert.False(client2.Session.StreamEnabled);

        // 6. Stop class and verify fail-open
        await hub.StopClassAsync();
        for (int i = 0; i < 40; i++)
        {
            if (client1.Session.Status == SessionStatus.Idle && client2.Session.Status == SessionStatus.Idle)
                break;
            await Task.Delay(50);
        }

        Assert.Equal(SessionStatus.Idle, client1.Session.Status);
        Assert.Equal(SessionStatus.Idle, client2.Session.Status);

        cts.Cancel();
        try { await Task.WhenAll(runTask1, runTask2); } catch { }
        client1.Dispose();
        client2.Dispose();
    }
}
