using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using NordControl.Core.Policies;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

[Collection("NetworkTests")]
public class LoopbackStage3Tests
{
    [Fact]
    public async Task Full_stage3_loopback_hints_launch_and_ram_blocking_lifecycle()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        await using var hub = new ClassHub(udpPort, tcpPort);
        await hub.StartClassAsync("Информатика-Stage3", "4321");

        var testApps1 = new List<InstalledAppInfo>
        {
            new() { Name = "Visual Studio Code", Exe = "code.exe", LaunchTarget = @"C:\VSCode\code.exe" },
            new() { Name = "Google Chrome", Exe = "chrome.exe", LaunchTarget = @"C:\Chrome\chrome.exe" }
        };

        var testApps2 = new List<InstalledAppInfo>
        {
            new() { Name = "Calculator", Exe = "calc.exe", LaunchTarget = null }
        };

        var fakeBlocker1 = new FakeAppBlocker();
        var fakeLauncher1 = new FakeAppLauncher();
        var client1 = new ClassClient(
            pin: "4321",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-Stage3-01",
            appBlocker: fakeBlocker1,
            appLauncher: fakeLauncher1,
            installedAppsProvider: () => testApps1
        );

        var fakeBlocker2 = new FakeAppBlocker();
        var fakeLauncher2 = new FakeAppLauncher();
        var client2 = new ClassClient(
            pin: "4321",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-Stage3-02",
            appBlocker: fakeBlocker2,
            appLauncher: fakeLauncher2,
            installedAppsProvider: () => testApps2
        );

        var hints1Tcs = new TaskCompletionSource<IReadOnlyList<InstalledAppInfo>>();
        var hints2Tcs = new TaskCompletionSource<IReadOnlyList<InstalledAppInfo>>();

        hub.InstalledHintsReceived += (sId, apps) =>
        {
            if (client1.Session.StudentId == sId)
            {
                hints1Tcs.TrySetResult(apps);
            }
            else if (client2.Session.StudentId == sId)
            {
                hints2Tcs.TrySetResult(apps);
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var runTask1 = Task.Run(() => client1.RunAsync(cts.Token));
        var runTask2 = Task.Run(() => client2.RunAsync(cts.Token));

        try
        {
            // 1. Wait for both clients to join and become Online
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

            // 2. Verify installed_hints received by Hub
            var completedHints1 = await Task.WhenAny(hints1Tcs.Task, Task.Delay(3000));
            Assert.Same(hints1Tcs.Task, completedHints1);
            var apps1 = await hints1Tcs.Task;
            Assert.Equal(2, apps1.Count);
            Assert.Equal("Visual Studio Code", apps1[0].Name);

            var completedHints2 = await Task.WhenAny(hints2Tcs.Task, Task.Delay(3000));
            Assert.Same(hints2Tcs.Task, completedHints2);
            var apps2 = await hints2Tcs.Task;
            Assert.Single(apps2);
            Assert.Equal("Calculator", apps2[0].Name);

            // 3. Test SendLaunchAppAsync (targeted to student 1)
            var sendLaunchResult = await hub.SendLaunchAppAsync(student1Id, "code.exe", @"C:\VSCode\code.exe");
            Assert.True(sendLaunchResult);

            for (int i = 0; i < 40; i++)
            {
                if (fakeLauncher1.Launched.Count > 0) break;
                await Task.Delay(50);
            }

            Assert.Single(fakeLauncher1.Launched);
            Assert.Equal("code.exe", fakeLauncher1.Launched[0].exe);
            Assert.Equal(@"C:\VSCode\code.exe", fakeLauncher1.Launched[0].target);
            Assert.Empty(fakeLauncher2.Launched);

            // 4. Test BroadcastLaunchAppAsync (to all students)
            var broadcastLaunchCount = await hub.BroadcastLaunchAppAsync("browser.exe", null);
            Assert.Equal(2, broadcastLaunchCount);

            for (int i = 0; i < 40; i++)
            {
                if (fakeLauncher1.Launched.Count >= 2 && fakeLauncher2.Launched.Count >= 1) break;
                await Task.Delay(50);
            }

            Assert.Equal(2, fakeLauncher1.Launched.Count);
            Assert.Equal("browser.exe", fakeLauncher1.Launched[1].exe);
            Assert.Single(fakeLauncher2.Launched);
            Assert.Equal("browser.exe", fakeLauncher2.Launched[0].exe);

            // 5. Test SendBlockListAsync (targeted to student 1)
            var blockedForStudent1 = new[] { "discord.exe", "steam.exe" };
            var sendBlockResult = await hub.SendBlockListAsync(student1Id, blockedForStudent1);
            Assert.True(sendBlockResult);

            for (int i = 0; i < 40; i++)
            {
                if (fakeBlocker1.SetBlockListCount > 0) break;
                await Task.Delay(50);
            }

            Assert.Equal(2, fakeBlocker1.CurrentBlockList.Count);
            Assert.Contains("discord.exe", fakeBlocker1.CurrentBlockList);
            Assert.Contains("steam.exe", fakeBlocker1.CurrentBlockList);
            Assert.Empty(fakeBlocker2.CurrentBlockList);

            // 6. Test BroadcastBlockListAsync (to all students)
            var broadcastBlockList = new[] { "telegram.exe", "games.exe" };
            var broadcastBlockCount = await hub.BroadcastBlockListAsync(broadcastBlockList);
            Assert.Equal(2, broadcastBlockCount);

            for (int i = 0; i < 40; i++)
            {
                if (fakeBlocker1.CurrentBlockList.Contains("telegram.exe") &&
                    fakeBlocker2.CurrentBlockList.Contains("telegram.exe"))
                {
                    break;
                }
                await Task.Delay(50);
            }

            Assert.Equal(2, fakeBlocker1.CurrentBlockList.Count);
            Assert.Contains("telegram.exe", fakeBlocker1.CurrentBlockList);
            Assert.Equal(2, fakeBlocker2.CurrentBlockList.Count);
            Assert.Contains("telegram.exe", fakeBlocker2.CurrentBlockList);

            // 7. Test Fail-Open: Teacher stops class -> immediate blocker clear
            var initialClearCount1 = fakeBlocker1.ClearCount;
            var initialClearCount2 = fakeBlocker2.ClearCount;

            await hub.StopClassAsync();

            for (int i = 0; i < 60; i++)
            {
                if (fakeBlocker1.ClearCount > initialClearCount1 &&
                    fakeBlocker2.ClearCount > initialClearCount2 &&
                    client1.Session.Status == SessionStatus.Idle &&
                    client2.Session.Status == SessionStatus.Idle)
                {
                    break;
                }
                await Task.Delay(50);
            }

            Assert.True(fakeBlocker1.ClearCount > initialClearCount1, "Student 1 blocker must be cleared on StopClassAsync");
            Assert.True(fakeBlocker2.ClearCount > initialClearCount2, "Student 2 blocker must be cleared on StopClassAsync");
            Assert.Empty(fakeBlocker1.CurrentBlockList);
            Assert.Empty(fakeBlocker2.CurrentBlockList);
            Assert.Equal(SessionStatus.Idle, client1.Session.Status);
            Assert.Equal(SessionStatus.Idle, client2.Session.Status);
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
