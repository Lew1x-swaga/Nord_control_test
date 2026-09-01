using System;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using Xunit;

namespace NordControl.Tests;

[Collection("NetworkTests")]
public class LoopbackStage7Tests
{
    [Fact]
    public async Task Group_launch_and_block_target_only_members_broadcast_and_stop_clear_groups()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        await using var hub = new ClassHub(udpPort, tcpPort);
        await hub.StartClassAsync("Информатика-Stage7", "1234");

        var fakeBlocker1 = new FakeAppBlocker();
        var fakeLauncher1 = new FakeAppLauncher();
        var client1 = new ClassClient(
            pin: "1234",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-Stage7-01",
            appBlocker: fakeBlocker1,
            appLauncher: fakeLauncher1
        );

        var fakeBlocker2 = new FakeAppBlocker();
        var fakeLauncher2 = new FakeAppLauncher();
        var client2 = new ClassClient(
            pin: "1234",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-Stage7-02",
            appBlocker: fakeBlocker2,
            appLauncher: fakeLauncher2
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var runTask1 = Task.Run(() => client1.RunAsync(cts.Token));
        var runTask2 = Task.Run(() => client2.RunAsync(cts.Token));

        try
        {
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

            var groupA = hub.CreateGroup("A");
            var groupB = hub.CreateGroup("B");
            Assert.False(string.IsNullOrEmpty(groupA));
            Assert.False(string.IsNullOrEmpty(groupB));
            Assert.True(hub.SetStudentGroup(student1Id, groupA));
            Assert.True(hub.SetStudentGroup(student2Id, groupB));

            var launchCount = await hub.SendLaunchAppToGroupAsync(groupA!, "calc.exe");
            Assert.Equal(1, launchCount);

            for (int i = 0; i < 40; i++)
            {
                if (fakeLauncher1.Launched.Count > 0) break;
                await Task.Delay(50);
            }

            Assert.Single(fakeLauncher1.Launched);
            Assert.Equal("calc.exe", fakeLauncher1.Launched[0].exe);
            Assert.Empty(fakeLauncher2.Launched);

            var blockCount = await hub.SendBlockListToGroupAsync(groupB!, ["notepad.exe"]);
            Assert.Equal(1, blockCount);

            for (int i = 0; i < 40; i++)
            {
                if (fakeBlocker2.CurrentBlockList.Contains("notepad.exe")) break;
                await Task.Delay(50);
            }

            Assert.Contains("notepad.exe", fakeBlocker2.CurrentBlockList);
            Assert.Empty(fakeBlocker1.CurrentBlockList);

            var broadcastCount = await hub.BroadcastBlockListAsync(["steam.exe"]);
            Assert.Equal(2, broadcastCount);

            for (int i = 0; i < 40; i++)
            {
                if (fakeBlocker1.CurrentBlockList.Contains("steam.exe") &&
                    fakeBlocker2.CurrentBlockList.Contains("steam.exe"))
                {
                    break;
                }
                await Task.Delay(50);
            }

            Assert.Contains("steam.exe", fakeBlocker1.CurrentBlockList);
            Assert.Contains("steam.exe", fakeBlocker2.CurrentBlockList);

            var initialClearCount1 = fakeBlocker1.ClearCount;
            var initialClearCount2 = fakeBlocker2.ClearCount;

            await hub.StopClassAsync();

            for (int i = 0; i < 60; i++)
            {
                if (fakeBlocker1.ClearCount > initialClearCount1 &&
                    fakeBlocker2.ClearCount > initialClearCount2)
                {
                    break;
                }
                await Task.Delay(50);
            }

            Assert.True(fakeBlocker1.ClearCount > initialClearCount1);
            Assert.True(fakeBlocker2.ClearCount > initialClearCount2);
            Assert.Empty(fakeBlocker1.CurrentBlockList);
            Assert.Empty(fakeBlocker2.CurrentBlockList);
            Assert.Empty(hub.Groups);
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
