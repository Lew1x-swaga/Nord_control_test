using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

[Collection("NetworkTests")]
public class ProcessListTests
{
    private class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    }

    [Fact]
    public async Task Hub_receives_process_list_from_client()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        var clock = new TestClock();
        await using var hub = new ClassHub(udpPort: udpPort, tcpPort: tcpPort, clock: clock);
        await hub.StartClassAsync("TestClass", "1234");

        var client = new ClassClient("1234", udpPort: udpPort, tcpPort: tcpPort, manualTeacherIp: "127.0.0.1", displayName: "StudentProc", clock: clock);

        var expectedMsg = new WireMessage
        {
            V = ProtocolConstants.Version,
            Type = "process_list",
            ActiveExe = "notepad.exe",
            Items = new List<ProcessItemInfo>
            {
                new() { Exe = "notepad.exe", Pid = 1234, Title = "Безымянный — Блокнот" },
                new() { Exe = "calculator.exe", Pid = 5678, Title = "Калькулятор" }
            }
        };

        client.ProcessListCallback = () => expectedMsg;

        string? receivedStudentId = null;
        WireMessage? receivedMsg = null;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        hub.ProcessListReceived += (sId, msg) =>
        {
            receivedStudentId = sId;
            receivedMsg = msg;
            tcs.TrySetResult(true);
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = Task.Run(() => client.RunAsync(cts.Token));

        for (int i = 0; i < 50; i++)
        {
            if (client.Session.Status == SessionStatus.Online) break;
            await Task.Delay(50);
        }

        Assert.Equal(SessionStatus.Online, client.Session.Status);
        var studentId = client.Session.StudentId!;
        await hub.SelectStudentAsync(studentId);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.Same(tcs.Task, completed);

        Assert.Equal(studentId, receivedStudentId);
        Assert.NotNull(receivedMsg);
        Assert.Equal("process_list", receivedMsg!.Type);
        Assert.Equal("notepad.exe", receivedMsg.ActiveExe);
        Assert.NotNull(receivedMsg.Items);
        Assert.Equal(2, receivedMsg.Items!.Count);
        Assert.Equal("notepad.exe", receivedMsg.Items[0].Exe);
        Assert.Equal("calculator.exe", receivedMsg.Items[1].Exe);

        cts.Cancel();
        try { await run; } catch { }
        client.Dispose();
    }
}
