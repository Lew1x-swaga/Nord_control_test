using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

[Collection("NetworkTests")]
public class ScreenStreamTests
{
    private class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    }

    [Fact]
    public async Task Hub_select_student_switches_stream_properly()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        var clock = new TestClock();
        await using var hub = new ClassHub(udpPort: udpPort, tcpPort: tcpPort, clock: clock);
        await hub.StartClassAsync("TestClass", "1234");

        var client1 = new ClassClient("1234", udpPort: udpPort, tcpPort: tcpPort, manualTeacherIp: "127.0.0.1", displayName: "Student1", clock: clock);
        var client2 = new ClassClient("1234", udpPort: udpPort, tcpPort: tcpPort, manualTeacherIp: "127.0.0.1", displayName: "Student2", clock: clock);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run1 = Task.Run(() => client1.RunAsync(cts.Token));
        var run2 = Task.Run(() => client2.RunAsync(cts.Token));

        // Wait until both clients are online
        for (int i = 0; i < 50; i++)
        {
            if (client1.Session.Status == SessionStatus.Online && client2.Session.Status == SessionStatus.Online && hub.Students.Count == 2)
                break;
            await Task.Delay(50);
        }

        Assert.Equal(SessionStatus.Online, client1.Session.Status);
        Assert.Equal(SessionStatus.Online, client2.Session.Status);

        var id1 = client1.Session.StudentId!;
        var id2 = client2.Session.StudentId!;

        // 1. Select student 1
        await hub.SelectStudentAsync(id1);
        Assert.Equal(id1, hub.SelectedStudentId);

        // Wait for client1 to receive stream_start
        for (int i = 0; i < 30; i++)
        {
            if (client1.Session.StreamEnabled) break;
            await Task.Delay(50);
        }

        Assert.True(client1.Session.StreamEnabled);
        Assert.False(client2.Session.StreamEnabled);

        // 2. Select student 2 (should stop 1 and start 2)
        await hub.SelectStudentAsync(id2);
        Assert.Equal(id2, hub.SelectedStudentId);

        for (int i = 0; i < 30; i++)
        {
            if (!client1.Session.StreamEnabled && client2.Session.StreamEnabled) break;
            await Task.Delay(50);
        }

        Assert.False(client1.Session.StreamEnabled);
        Assert.True(client2.Session.StreamEnabled);

        // 3. Deselect (select null)
        await hub.SelectStudentAsync(null);
        Assert.Null(hub.SelectedStudentId);

        for (int i = 0; i < 30; i++)
        {
            if (!client2.Session.StreamEnabled) break;
            await Task.Delay(50);
        }

        Assert.False(client2.Session.StreamEnabled);

        cts.Cancel();
        try { await Task.WhenAll(run1, run2); } catch { }
        client1.Dispose();
        client2.Dispose();
    }

    [Fact]
    public async Task Hub_overlapping_select_keeps_stream_on_latest_student()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        var clock = new TestClock();
        await using var hub = new ClassHub(udpPort: udpPort, tcpPort: tcpPort, clock: clock);
        await hub.StartClassAsync("TestClass", "1234");

        var client1 = new ClassClient("1234", udpPort: udpPort, tcpPort: tcpPort, manualTeacherIp: "127.0.0.1", displayName: "Student1", clock: clock);
        var client2 = new ClassClient("1234", udpPort: udpPort, tcpPort: tcpPort, manualTeacherIp: "127.0.0.1", displayName: "Student2", clock: clock);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var run1 = Task.Run(() => client1.RunAsync(cts.Token));
        var run2 = Task.Run(() => client2.RunAsync(cts.Token));

        for (int i = 0; i < 50; i++)
        {
            if (client1.Session.Status == SessionStatus.Online && client2.Session.Status == SessionStatus.Online && hub.Students.Count == 2)
                break;
            await Task.Delay(50);
        }

        Assert.Equal(SessionStatus.Online, client1.Session.Status);
        Assert.Equal(SessionStatus.Online, client2.Session.Status);

        var id1 = client1.Session.StudentId!;
        var id2 = client2.Session.StudentId!;

        var first = hub.SelectStudentAsync(id1);
        var second = hub.SelectStudentAsync(id2);
        await Task.WhenAll(first, second);

        Assert.Equal(id2, hub.SelectedStudentId);

        for (int i = 0; i < 40; i++)
        {
            if (!client1.Session.StreamEnabled && client2.Session.StreamEnabled)
                break;
            await Task.Delay(50);
        }

        Assert.False(client1.Session.StreamEnabled);
        Assert.True(client2.Session.StreamEnabled);

        cts.Cancel();
        try { await Task.WhenAll(run1, run2); } catch { }
        client1.Dispose();
        client2.Dispose();
    }

    [Fact]
    public async Task Hub_receives_type2_jpeg_frame_from_selected_student()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        var clock = new TestClock();
        await using var hub = new ClassHub(udpPort: udpPort, tcpPort: tcpPort, clock: clock);
        await hub.StartClassAsync("TestClass", "1234");

        var client = new ClassClient("1234", udpPort: udpPort, tcpPort: tcpPort, manualTeacherIp: "127.0.0.1", displayName: "Streamer", clock: clock);

        byte[] fakeJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0xFF, 0xD9 };
        var sentFrame = new JpegFrame(1280, 720, 1000UL, fakeJpeg);

        client.CaptureFrameCallback = (ct) => Task.FromResult<JpegFrame?>(sentFrame);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = Task.Run(() => client.RunAsync(cts.Token));

        for (int i = 0; i < 50; i++)
        {
            if (client.Session.Status == SessionStatus.Online) break;
            await Task.Delay(50);
        }

        Assert.Equal(SessionStatus.Online, client.Session.Status);
        var studentId = client.Session.StudentId!;

        JpegFrame? receivedFrame = null;
        string? receivedStudentId = null;
        var frameTcs = new TaskCompletionSource<bool>();

        hub.ScreenFrameReceived += (sId, frame) =>
        {
            receivedStudentId = sId;
            receivedFrame = frame;
            frameTcs.TrySetResult(true);
        };

        await hub.SelectStudentAsync(studentId);

        var completed = await Task.WhenAny(frameTcs.Task, Task.Delay(2000));
        Assert.Same(frameTcs.Task, completed);

        Assert.Equal(studentId, receivedStudentId);
        Assert.NotNull(receivedFrame);
        Assert.Equal(1280u, receivedFrame!.Value.Width);
        Assert.Equal(720u, receivedFrame.Value.Height);
        Assert.Equal(fakeJpeg, receivedFrame.Value.Data);

        cts.Cancel();
        try { await run; } catch { }
        client.Dispose();
    }

    [Fact]
    public async Task Hub_ignores_frames_from_unselected_students()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        var clock = new TestClock();
        await using var hub = new ClassHub(udpPort: udpPort, tcpPort: tcpPort, clock: clock);
        await hub.StartClassAsync("TestClass", "1234");

        var client1 = new ClassClient("1234", udpPort: udpPort, tcpPort: tcpPort, manualTeacherIp: "127.0.0.1", displayName: "UnselectedStreamer", clock: clock);
        var client2 = new ClassClient("1234", udpPort: udpPort, tcpPort: tcpPort, manualTeacherIp: "127.0.0.1", displayName: "SelectedStudent", clock: clock);

        byte[] fakeJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0xAA, 0xBB, 0xFF, 0xD9 };
        var sentFrame = new JpegFrame(1280, 720, 1000UL, fakeJpeg);

        // Force enable stream on client 1 even if not selected
        client1.CaptureFrameCallback = (ct) => Task.FromResult<JpegFrame?>(sentFrame);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run1 = Task.Run(() => client1.RunAsync(cts.Token));
        var run2 = Task.Run(() => client2.RunAsync(cts.Token));

        for (int i = 0; i < 50; i++)
        {
            if (client1.Session.Status == SessionStatus.Online && client2.Session.Status == SessionStatus.Online) break;
            await Task.Delay(50);
        }

        var client1Id = client1.Session.StudentId!;
        var client2Id = client2.Session.StudentId!;

        // Select student 2, NOT student 1
        await hub.SelectStudentAsync(client2Id);

        // Manually enable stream on client1 to simulate rogue frame
        client1.Session.StreamEnabled = true;

        bool unselectedFrameReceived = false;
        hub.ScreenFrameReceived += (sId, frame) =>
        {
            if (sId == client1Id)
            {
                unselectedFrameReceived = true;
            }
        };

        await Task.Delay(500);

        Assert.False(unselectedFrameReceived);

        cts.Cancel();
        try { await Task.WhenAll(run1, run2); } catch { }
        client1.Dispose();
        client2.Dispose();
    }
}
