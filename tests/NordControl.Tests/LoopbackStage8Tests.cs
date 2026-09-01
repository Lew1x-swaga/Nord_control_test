using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

[Collection("NetworkTests")]
public class LoopbackStage8Tests
{
    [Fact]
    public async Task Broadcast_group_clip_empty_and_stop_fail_open()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        await using var hub = new ClassHub(udpPort, tcpPort);
        await hub.StartClassAsync("Информатика-Stage8", "1234");

        var client1 = new ClassClient(
            pin: "1234",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-Stage8-01");
        var client2 = new ClassClient(
            pin: "1234",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-Stage8-02");

        var received1 = new List<(string id, string text)>();
        var received2 = new List<(string id, string text)>();
        var dismissed1 = 0;
        var dismissed2 = 0;
        var recvLock = new object();

        client1.TeacherMessageReceived += (id, text) =>
        {
            lock (recvLock) received1.Add((id, text));
        };
        client2.TeacherMessageReceived += (id, text) =>
        {
            lock (recvLock) received2.Add((id, text));
        };
        client1.TeacherMessageDismissed += () => Interlocked.Increment(ref dismissed1);
        client2.TeacherMessageDismissed += () => Interlocked.Increment(ref dismissed2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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

            Assert.False(await hub.SendTeacherMessageAsync(student1Id, ""));
            Assert.False(await hub.SendTeacherMessageAsync(student1Id, "   "));
            Assert.Equal(0, await hub.BroadcastTeacherMessageAsync(""));
            Assert.Equal(0, await hub.BroadcastTeacherMessageAsync("  "));
            Assert.Equal(0, await hub.SendTeacherMessageToGroupAsync("no-group", "hi"));
            Assert.False(await hub.SendTeacherMessageAsync("missing-student", "hello"));
            await Task.Delay(150);
            lock (recvLock)
            {
                Assert.Empty(received1);
                Assert.Empty(received2);
            }

            var broadcastCount = await hub.BroadcastTeacherMessageAsync("Откройте §3");
            Assert.Equal(2, broadcastCount);
            await WaitUntil.True(() =>
            {
                lock (recvLock) return received1.Count >= 1 && received2.Count >= 1;
            });
            lock (recvLock)
            {
                Assert.Single(received1);
                Assert.Single(received2);
                Assert.Equal("Откройте §3", received1[0].text);
                Assert.Equal("Откройте §3", received2[0].text);
                Assert.False(string.IsNullOrEmpty(received1[0].id));
                Assert.False(string.IsNullOrEmpty(received2[0].id));
            }

            var groupA = hub.CreateGroup("A");
            var groupB = hub.CreateGroup("B");
            Assert.False(string.IsNullOrEmpty(groupA));
            Assert.False(string.IsNullOrEmpty(groupB));
            Assert.True(hub.SetStudentGroup(student1Id, groupA));
            Assert.True(hub.SetStudentGroup(student2Id, groupB));

            var groupCount = await hub.SendTeacherMessageToGroupAsync(groupA!, "Только группа A");
            Assert.Equal(1, groupCount);
            await WaitUntil.True(() =>
            {
                lock (recvLock) return received1.Count >= 2;
            });
            await Task.Delay(150);
            lock (recvLock)
            {
                Assert.Equal(2, received1.Count);
                Assert.Single(received2);
                Assert.Equal("Только группа A", received1[1].text);
            }

            var longText = new string('x', ProtocolConstants.MaxTeacherMessageChars + 50);
            var clipCount = await hub.BroadcastTeacherMessageAsync(longText);
            Assert.Equal(2, clipCount);
            await WaitUntil.True(() =>
            {
                lock (recvLock) return received1.Count >= 3 && received2.Count >= 2;
            });
            lock (recvLock)
            {
                Assert.Equal(ProtocolConstants.MaxTeacherMessageChars, received1[2].text.Length);
                Assert.Equal(ProtocolConstants.MaxTeacherMessageChars, received2[1].text.Length);
            }

            Assert.Equal(SessionStatus.Online, client1.Session.Status);
            Assert.Equal(SessionStatus.Online, client2.Session.Status);

            await hub.StopClassAsync();
            await WaitUntil.True(
                () => client1.Session.Status != SessionStatus.Online &&
                      client2.Session.Status != SessionStatus.Online);

            Assert.NotEqual(SessionStatus.Online, client1.Session.Status);
            Assert.NotEqual(SessionStatus.Online, client2.Session.Status);
            Assert.False(hub.IsRunning);
            Assert.True(Volatile.Read(ref dismissed1) >= 1);
            Assert.True(Volatile.Read(ref dismissed2) >= 1);

            Assert.Equal(0, await hub.BroadcastTeacherMessageAsync("после конца"));
            Assert.False(await hub.SendTeacherMessageAsync(student1Id, "после конца"));
            Assert.Equal(0, await hub.SendTeacherMessageToGroupAsync(groupA!, "после конца"));
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
