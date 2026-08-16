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

public class FakeAppBlocker : IAppBlocker
{
    public List<string> CurrentBlockList { get; } = new();
    public int ClearCount { get; private set; }
    public int SetBlockListCount { get; private set; }

    public void SetBlockList(IEnumerable<string> exeNames)
    {
        SetBlockListCount++;
        CurrentBlockList.Clear();
        if (exeNames != null)
        {
            CurrentBlockList.AddRange(exeNames);
        }
    }

    public void Clear()
    {
        ClearCount++;
        CurrentBlockList.Clear();
    }

    public IReadOnlyCollection<string> GetBlockList() => CurrentBlockList.ToList();

    public bool IsBlocked(string exeName) => CurrentBlockList.Contains(exeName, StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        Clear();
    }
}

public class FakeAppLauncher : IAppLauncher
{
    public List<(string exe, string? target)> Launched { get; } = new();

    public bool Launch(string exe, string? launchTarget = null)
    {
        Launched.Add((exe, launchTarget));
        return true;
    }
}

[Collection("NetworkTests")]
public class Stage3CoreTests
{
    [Fact]
    public void TeacherPreset_DefaultValues_AreEmptyLists()
    {
        var preset = new TeacherPreset();
        Assert.NotNull(preset.QuickApps);
        Assert.NotNull(preset.BlockedApps);
        Assert.Empty(preset.QuickApps);
        Assert.Empty(preset.BlockedApps);
    }

    [Fact]
    public void TeacherPresetManager_LoadNonExistentFile_ReturnsDefaultPreset()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"preset_nonexistent_{Guid.NewGuid():N}.json");
        try
        {
            var preset = TeacherPresetManager.Load(tempFile);
            Assert.NotNull(preset);
            Assert.Empty(preset.QuickApps);
            Assert.Empty(preset.BlockedApps);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void TeacherPresetManager_SaveAndLoad_RoundTripsCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"preset_test_{Guid.NewGuid():N}.json");
        try
        {
            var original = new TeacherPreset
            {
                QuickApps = new List<InstalledAppInfo>
                {
                    new() { Name = "Browser", Exe = "chrome.exe", LaunchTarget = @"C:\Program Files\Google\Chrome\chrome.exe" },
                    new() { Name = "Notepad", Exe = "notepad.exe", LaunchTarget = null }
                },
                BlockedApps = new List<string> { "discord.exe", "steam.exe", "telegram.exe" }
            };

            TeacherPresetManager.Save(original, tempFile);
            Assert.True(File.Exists(tempFile));

            var loaded = TeacherPresetManager.Load(tempFile);
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded.QuickApps.Count);
            Assert.Equal("Browser", loaded.QuickApps[0].Name);
            Assert.Equal("chrome.exe", loaded.QuickApps[0].Exe);
            Assert.Equal(@"C:\Program Files\Google\Chrome\chrome.exe", loaded.QuickApps[0].LaunchTarget);
            Assert.Equal("Notepad", loaded.QuickApps[1].Name);
            Assert.Equal(3, loaded.BlockedApps.Count);
            Assert.Contains("discord.exe", loaded.BlockedApps);
            Assert.Contains("steam.exe", loaded.BlockedApps);
            Assert.Contains("telegram.exe", loaded.BlockedApps);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void TeacherPresetManager_LoadCorruptedJson_ReturnsDefaultPreset()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"preset_corrupt_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, "{ corrupted json invalid syntax ... ");
            var preset = TeacherPresetManager.Load(tempFile);
            Assert.NotNull(preset);
            Assert.Empty(preset.QuickApps);
            Assert.Empty(preset.BlockedApps);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ClassHub_SendLaunchAppAsync_SendsMessageToStudent()
    {
        var (udp, tcp) = TestPorts.NextPair();
        await using var hub = new ClassHub(udp, tcp);
        await hub.StartClass("Class-Stage3", "1111", CancellationToken.None);

        var fakeLauncher = new FakeAppLauncher();
        var fakeBlocker = new FakeAppBlocker();

        await using var client = new ClassClient(
            pin: "1111",
            udpPort: udp,
            tcpPort: tcp,
            manualTeacherIp: "127.0.0.1",
            appBlocker: fakeBlocker,
            appLauncher: fakeLauncher
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = client.RunAsync(cts.Token);

        try
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(4);
            while (client.Session.Status != SessionStatus.Online && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }
            Assert.Equal(SessionStatus.Online, client.Session.Status);

            var studentId = client.Session.StudentId!;
            var sent = await hub.SendLaunchAppAsync(studentId, "notepad.exe", "C:\\Windows\\notepad.exe");
            Assert.True(sent);

            var checkTimeout = DateTime.UtcNow.AddSeconds(3);
            while (fakeLauncher.Launched.Count == 0 && DateTime.UtcNow < checkTimeout)
            {
                await Task.Delay(50);
            }

            Assert.Single(fakeLauncher.Launched);
            Assert.Equal("notepad.exe", fakeLauncher.Launched[0].exe);
            Assert.Equal("C:\\Windows\\notepad.exe", fakeLauncher.Launched[0].target);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch { }
        }
    }

    [Fact]
    public async Task ClassHub_BroadcastLaunchAppAsync_SendsToAllStudents()
    {
        var (udp, tcp) = TestPorts.NextPair();
        await using var hub = new ClassHub(udp, tcp);
        await hub.StartClass("Class-Stage3-Broadcast", "2222", CancellationToken.None);

        var fakeLauncher1 = new FakeAppLauncher();
        var fakeBlocker1 = new FakeAppBlocker();
        await using var client1 = new ClassClient("2222", udp, tcp, "127.0.0.1", "Student1", null, fakeBlocker1, fakeLauncher1);

        var fakeLauncher2 = new FakeAppLauncher();
        var fakeBlocker2 = new FakeAppBlocker();
        await using var client2 = new ClassClient("2222", udp, tcp, "127.0.0.1", "Student2", null, fakeBlocker2, fakeLauncher2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask1 = client1.RunAsync(cts.Token);
        var runTask2 = client2.RunAsync(cts.Token);

        try
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(4);
            while ((client1.Session.Status != SessionStatus.Online || client2.Session.Status != SessionStatus.Online) && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }
            Assert.Equal(SessionStatus.Online, client1.Session.Status);
            Assert.Equal(SessionStatus.Online, client2.Session.Status);

            var count = await hub.BroadcastLaunchAppAsync("calc.exe", null);
            Assert.Equal(2, count);

            var checkTimeout = DateTime.UtcNow.AddSeconds(3);
            while ((fakeLauncher1.Launched.Count == 0 || fakeLauncher2.Launched.Count == 0) && DateTime.UtcNow < checkTimeout)
            {
                await Task.Delay(50);
            }

            Assert.Single(fakeLauncher1.Launched);
            Assert.Equal("calc.exe", fakeLauncher1.Launched[0].exe);
            Assert.Single(fakeLauncher2.Launched);
            Assert.Equal("calc.exe", fakeLauncher2.Launched[0].exe);
        }
        finally
        {
            cts.Cancel();
            try { await Task.WhenAll(runTask1, runTask2); } catch { }
        }
    }

    [Fact]
    public async Task ClassHub_SendBlockListAsync_UpdatesClientBlocker()
    {
        var (udp, tcp) = TestPorts.NextPair();
        await using var hub = new ClassHub(udp, tcp);
        await hub.StartClass("Class-Stage3-Block", "3333", CancellationToken.None);

        var fakeBlocker = new FakeAppBlocker();
        var fakeLauncher = new FakeAppLauncher();

        await using var client = new ClassClient(
            pin: "3333",
            udpPort: udp,
            tcpPort: tcp,
            manualTeacherIp: "127.0.0.1",
            appBlocker: fakeBlocker,
            appLauncher: fakeLauncher
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = client.RunAsync(cts.Token);

        try
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(4);
            while (client.Session.Status != SessionStatus.Online && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }
            Assert.Equal(SessionStatus.Online, client.Session.Status);

            var studentId = client.Session.StudentId!;
            var blockedList = new[] { "discord.exe", "steam.exe" };
            var sent = await hub.SendBlockListAsync(studentId, blockedList);
            Assert.True(sent);

            var checkTimeout = DateTime.UtcNow.AddSeconds(3);
            while (fakeBlocker.SetBlockListCount == 0 && DateTime.UtcNow < checkTimeout)
            {
                await Task.Delay(50);
            }

            Assert.Equal(2, fakeBlocker.CurrentBlockList.Count);
            Assert.Contains("discord.exe", fakeBlocker.CurrentBlockList);
            Assert.Contains("steam.exe", fakeBlocker.CurrentBlockList);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch { }
        }
    }

    [Fact]
    public async Task ClassHub_BroadcastBlockListAsync_UpdatesAllClients()
    {
        var (udp, tcp) = TestPorts.NextPair();
        await using var hub = new ClassHub(udp, tcp);
        await hub.StartClass("Class-Stage3-BcastBlock", "4444", CancellationToken.None);

        var fakeBlocker1 = new FakeAppBlocker();
        var fakeBlocker2 = new FakeAppBlocker();
        await using var client1 = new ClassClient("4444", udp, tcp, "127.0.0.1", "S1", null, fakeBlocker1, new FakeAppLauncher());
        await using var client2 = new ClassClient("4444", udp, tcp, "127.0.0.1", "S2", null, fakeBlocker2, new FakeAppLauncher());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask1 = client1.RunAsync(cts.Token);
        var runTask2 = client2.RunAsync(cts.Token);

        try
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(4);
            while ((client1.Session.Status != SessionStatus.Online || client2.Session.Status != SessionStatus.Online) && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }

            var blocked = new[] { "game.exe", "browser.exe" };
            var count = await hub.BroadcastBlockListAsync(blocked);
            Assert.Equal(2, count);

            var checkTimeout = DateTime.UtcNow.AddSeconds(3);
            while ((fakeBlocker1.SetBlockListCount == 0 || fakeBlocker2.SetBlockListCount == 0) && DateTime.UtcNow < checkTimeout)
            {
                await Task.Delay(50);
            }

            Assert.Equal(2, fakeBlocker1.CurrentBlockList.Count);
            Assert.Equal(2, fakeBlocker2.CurrentBlockList.Count);
        }
        finally
        {
            cts.Cancel();
            try { await Task.WhenAll(runTask1, runTask2); } catch { }
        }
    }

    [Fact]
    public async Task ClassClient_OnJoinOk_SendsInstalledHintsToHub()
    {
        var (udp, tcp) = TestPorts.NextPair();
        await using var hub = new ClassHub(udp, tcp);
        await hub.StartClass("Class-Hints", "5555", CancellationToken.None);

        var testApps = new List<InstalledAppInfo>
        {
            new() { Name = "Visual Studio Code", Exe = "code.exe", LaunchTarget = "C:\\Tools\\code.exe" },
            new() { Name = "Calculator", Exe = "calc.exe", LaunchTarget = null }
        };

        string? receivedStudentId = null;
        IReadOnlyList<InstalledAppInfo>? receivedApps = null;
        var hintsTcs = new TaskCompletionSource<bool>();

        hub.InstalledHintsReceived += (studentId, apps) =>
        {
            receivedStudentId = studentId;
            receivedApps = apps;
            hintsTcs.TrySetResult(true);
        };

        await using var client = new ClassClient(
            pin: "5555",
            udpPort: udp,
            tcpPort: tcp,
            manualTeacherIp: "127.0.0.1",
            installedAppsProvider: () => testApps
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = client.RunAsync(cts.Token);

        try
        {
            var completedTask = await Task.WhenAny(hintsTcs.Task, Task.Delay(4000, cts.Token));
            Assert.Same(hintsTcs.Task, completedTask);

            Assert.NotNull(receivedStudentId);
            Assert.NotNull(receivedApps);
            Assert.Equal(2, receivedApps.Count);
            Assert.Equal("Visual Studio Code", receivedApps[0].Name);
            Assert.Equal("code.exe", receivedApps[0].Exe);
            Assert.Equal("C:\\Tools\\code.exe", receivedApps[0].LaunchTarget);
            Assert.Equal("Calculator", receivedApps[1].Name);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch { }
        }
    }

    [Fact]
    public async Task FailOpen_OnSessionEnd_ClearsBlockListImmediately()
    {
        var (udp, tcp) = TestPorts.NextPair();
        await using var hub = new ClassHub(udp, tcp);
        await hub.StartClass("Class-FailOpen", "6666", CancellationToken.None);

        var fakeBlocker = new FakeAppBlocker();
        await using var client = new ClassClient(
            pin: "6666",
            udpPort: udp,
            tcpPort: tcp,
            manualTeacherIp: "127.0.0.1",
            appBlocker: fakeBlocker
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = client.RunAsync(cts.Token);

        try
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(4);
            while (client.Session.Status != SessionStatus.Online && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }
            Assert.Equal(SessionStatus.Online, client.Session.Status);

            var studentId = client.Session.StudentId!;
            await hub.SendBlockListAsync(studentId, new[] { "forbidden.exe" });

            var checkTimeout = DateTime.UtcNow.AddSeconds(3);
            while (fakeBlocker.CurrentBlockList.Count == 0 && DateTime.UtcNow < checkTimeout)
            {
                await Task.Delay(50);
            }
            Assert.Single(fakeBlocker.CurrentBlockList);

            var initialClearCount = fakeBlocker.ClearCount;

            // Teacher stops class -> sends session_end
            await hub.StopClassAsync();

            var clearWaitTimeout = DateTime.UtcNow.AddSeconds(3);
            while (fakeBlocker.ClearCount == initialClearCount && DateTime.UtcNow < clearWaitTimeout)
            {
                await Task.Delay(50);
            }

            Assert.True(fakeBlocker.ClearCount > initialClearCount, "Blocker.Clear() must be called on session_end");
            Assert.Empty(fakeBlocker.CurrentBlockList);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch { }
        }
    }

    [Fact]
    public async Task FailOpen_OnClientDispose_ClearsBlockList()
    {
        var fakeBlocker = new FakeAppBlocker();
        fakeBlocker.SetBlockList(new[] { "test.exe" });
        Assert.Single(fakeBlocker.CurrentBlockList);

        var client = new ClassClient(
            pin: "7777",
            appBlocker: fakeBlocker
        );

        await client.DisposeAsync();

        Assert.Empty(fakeBlocker.CurrentBlockList);
        Assert.True(fakeBlocker.ClearCount > 0);
    }
}
