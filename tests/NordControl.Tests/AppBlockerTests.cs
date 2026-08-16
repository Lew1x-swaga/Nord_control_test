using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core.Policies;
using Xunit;

namespace NordControl.Tests;

public class AppBlockerTests
{
    [Fact]
    public void SetBlockList_normalizes_and_stores_in_ram()
    {
        using var blocker = new RamAppBlocker(autoStartWatcher: false);

        blocker.SetBlockList(new[] { "Discord.exe", "STEAM.EXE", "notepad", @"C:\Games\Game.exe" });

        var list = blocker.GetBlockList();
        Assert.Equal(4, list.Count);
        Assert.Contains("discord.exe", list);
        Assert.Contains("steam.exe", list);
        Assert.Contains("notepad.exe", list);
        Assert.Contains("game.exe", list);

        Assert.True(blocker.IsBlocked("discord.exe"));
        Assert.True(blocker.IsBlocked("DISCORD.EXE"));
        Assert.True(blocker.IsBlocked("steam"));
        Assert.True(blocker.IsBlocked(@"D:\Apps\notepad.exe"));
        Assert.False(blocker.IsBlocked("calc.exe"));
    }

    [Fact]
    public void Clear_empties_block_list_immediately()
    {
        using var blocker = new RamAppBlocker(autoStartWatcher: false);

        blocker.SetBlockList(new[] { "discord.exe", "steam.exe" });
        Assert.Equal(2, blocker.GetBlockList().Count);

        blocker.Clear();
        Assert.Empty(blocker.GetBlockList());
        Assert.False(blocker.IsBlocked("discord.exe"));
    }

    [Fact]
    public void Protected_processes_are_never_blocked()
    {
        using var blocker = new RamAppBlocker(autoStartWatcher: false);

        // Attempt to block self and critical system / dev processes
        blocker.SetBlockList(new[]
        {
            "NordControl.Student.exe",
            "NordControl.Teacher.exe",
            "Teacher.exe",
            "Student.exe",
            "dotnet.exe",
            "testhost.exe",
            "devenv.exe",
            "msbuild.exe",
            "explorer.exe",
            "dwm.exe",
            "csrss.exe",
            "lsass.exe",
            "smss.exe",
            "winlogon.exe",
            "services.exe",
            "discord.exe"
        });

        // Protected ones must NOT be present in effective block list
        Assert.False(blocker.IsBlocked("NordControl.Student.exe"));
        Assert.False(blocker.IsBlocked("nordcontrol.student.exe"));
        Assert.False(blocker.IsBlocked("Teacher.exe"));
        Assert.False(blocker.IsBlocked("Student.exe"));
        Assert.False(blocker.IsBlocked("dotnet.exe"));
        Assert.False(blocker.IsBlocked("testhost.exe"));
        Assert.False(blocker.IsBlocked("devenv.exe"));
        Assert.False(blocker.IsBlocked("msbuild.exe"));
        Assert.False(blocker.IsBlocked("explorer.exe"));
        Assert.False(blocker.IsBlocked("dwm.exe"));
        Assert.False(blocker.IsBlocked("csrss.exe"));

        // Normal process must be blocked
        Assert.True(blocker.IsBlocked("discord.exe"));
    }

    [Fact]
    public void Watcher_iteration_kills_only_blocked_non_protected_processes()
    {
        var killedPids = new List<int>();

        var simulatedProcesses = new List<ProcessCandidate>
        {
            new(Pid: 101, ExeName: "discord.exe", KillAction: () => killedPids.Add(101)),
            new(Pid: 102, ExeName: "NordControl.Student.exe", KillAction: () => killedPids.Add(102)),
            new(Pid: 103, ExeName: "dotnet.exe", KillAction: () => killedPids.Add(103)),
            new(Pid: 104, ExeName: "explorer.exe", KillAction: () => killedPids.Add(104)),
            new(Pid: 105, ExeName: "chrome.exe", KillAction: () => killedPids.Add(105)),
            new(Pid: 106, ExeName: "steam.exe", KillAction: () => killedPids.Add(106))
        };

        using var blocker = new RamAppBlocker(
            processEnumerator: () => simulatedProcesses,
            autoStartWatcher: false
        );

        blocker.SetBlockList(new[] { "discord.exe", "NordControl.Student.exe", "steam.exe" });

        // Trigger single manual inspection pass
        blocker.CheckAndEnforce();

        Assert.Equal(2, killedPids.Count);
        Assert.Contains(101, killedPids);
        Assert.Contains(106, killedPids);
        Assert.DoesNotContain(102, killedPids); // NordControl protected
        Assert.DoesNotContain(103, killedPids);
        Assert.DoesNotContain(104, killedPids);
        Assert.DoesNotContain(105, killedPids);
    }

    [Fact]
    public async Task Watcher_background_loop_runs_and_enforces()
    {
        var killedPids = new List<int>();
        var simulatedProcesses = new List<ProcessCandidate>
        {
            new(Pid: 201, ExeName: "game.exe", KillAction: () => killedPids.Add(201))
        };

        using var blocker = new RamAppBlocker(
            processEnumerator: () => simulatedProcesses,
            checkIntervalMs: 50,
            autoStartWatcher: true
        );

        blocker.SetBlockList(new[] { "game.exe" });

        await Task.Delay(200);

        Assert.Contains(201, killedPids);
    }

    [Fact]
    public void CheckAndEnforce_skips_process_enumeration_when_blocklist_is_empty()
    {
        var enumerationCount = 0;
        using var blocker = new RamAppBlocker(
            processEnumerator: () =>
            {
                enumerationCount++;
                return new List<ProcessCandidate>();
            },
            autoStartWatcher: false
        );

        // When block list is empty, CheckAndEnforce should not invoke processEnumerator
        blocker.CheckAndEnforce();
        Assert.Equal(0, enumerationCount);

        // Once block list has an item, enumeration is performed
        blocker.SetBlockList(new[] { "discord.exe" });
        blocker.CheckAndEnforce();
        Assert.Equal(1, enumerationCount);
    }

    [Fact]
    public void Dispose_clears_blocklist_and_stops_watcher()
    {
        var blocker = new RamAppBlocker(autoStartWatcher: false);
        blocker.SetBlockList(new[] { "game.exe" });

        Assert.True(blocker.IsBlocked("game.exe"));

        blocker.Dispose();

        Assert.Empty(blocker.GetBlockList());
        Assert.False(blocker.IsBlocked("game.exe"));
    }
}
