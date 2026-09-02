using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using Xunit;

namespace NordControl.Tests;

[Collection("NetworkTests")]
public class ClassGroupTests
{
    private static async Task<ClassHub> StartHubAsync()
    {
        var (udp, tcp) = TestPorts.NextPair();
        var hub = new ClassHub(udp, tcp);
        await hub.StartClassAsync("Класс-101", "1234");
        return hub;
    }

    [Fact]
    public void Groups_BeforeStart_IsEmpty()
    {
        var (udp, tcp) = TestPorts.NextPair();
        var hub = new ClassHub(udp, tcp);
        Assert.Empty(hub.Groups);
    }

    [Fact]
    public void CreateGroup_WhenClassNotRunning_DoesNotCreate()
    {
        var (udp, tcp) = TestPorts.NextPair();
        var hub = new ClassHub(udp, tcp);
        var id = hub.CreateGroup("Группа А");
        Assert.True(string.IsNullOrEmpty(id));
        Assert.Empty(hub.Groups);
    }

    [Fact]
    public void SetStudentGroup_WhenClassNotRunning_ReturnsFalse()
    {
        var (udp, tcp) = TestPorts.NextPair();
        var hub = new ClassHub(udp, tcp);
        Assert.False(hub.SetStudentGroup("student-1", "group-missing"));
        Assert.Null(hub.GetStudentGroupId("student-1"));
    }

    [Fact]
    public async Task CreateGroup_EmptyOrWhitespaceName_DoesNotCreate()
    {
        await using var hub = await StartHubAsync();
        Assert.True(string.IsNullOrEmpty(hub.CreateGroup("")));
        Assert.True(string.IsNullOrEmpty(hub.CreateGroup("   ")));
        Assert.Empty(hub.Groups);
    }

    [Fact]
    public async Task CreateGroup_AfterStart_ReturnsIdAndAppearsInGroups()
    {
        await using var hub = await StartHubAsync();
        var id = hub.CreateGroup("  Группа А  ");
        Assert.False(string.IsNullOrEmpty(id));
        Assert.Single(hub.Groups);
        Assert.Equal(id, hub.Groups[0].Id);
        Assert.Equal("Группа А", hub.Groups[0].Name);
    }

    [Fact]
    public async Task CreateGroup_DuplicateNames_AreAllowedWithDistinctIds()
    {
        await using var hub = await StartHubAsync();
        var a = hub.CreateGroup("Одинаковое");
        var b = hub.CreateGroup("Одинаковое");
        Assert.False(string.IsNullOrEmpty(a));
        Assert.False(string.IsNullOrEmpty(b));
        Assert.NotEqual(a, b);
        Assert.Equal(2, hub.Groups.Count);
    }

    [Fact]
    public async Task SetStudentGroup_UnknownGroupId_ReturnsFalse()
    {
        await using var hub = await StartHubAsync();
        hub.CreateGroup("Есть");
        Assert.False(hub.SetStudentGroup("student-1", Guid.NewGuid().ToString()));
        Assert.Null(hub.GetStudentGroupId("student-1"));
    }

    [Fact]
    public async Task SetStudentGroup_ExistingGroup_SucceedsWithoutJoin()
    {
        await using var hub = await StartHubAsync();
        var groupId = hub.CreateGroup("А");
        Assert.True(hub.SetStudentGroup("student-1", groupId));
        Assert.Equal(groupId, hub.GetStudentGroupId("student-1"));
    }

    [Fact]
    public async Task SetStudentGroup_Null_UngroupsStudent()
    {
        await using var hub = await StartHubAsync();
        var groupId = hub.CreateGroup("А");
        Assert.True(hub.SetStudentGroup("student-1", groupId));
        Assert.True(hub.SetStudentGroup("student-1", null));
        Assert.Null(hub.GetStudentGroupId("student-1"));
    }

    [Fact]
    public async Task SetStudentGroup_CanBelongToTwoGroups()
    {
        await using var hub = await StartHubAsync();
        var a = hub.CreateGroup("А");
        var b = hub.CreateGroup("Б");
        Assert.True(hub.SetStudentGroup("student-1", a));
        Assert.True(hub.SetStudentGroup("student-1", b));
        var ids = hub.GetStudentGroupIds("student-1");
        Assert.Equal(2, ids.Count);
        Assert.Contains(a, ids);
        Assert.Contains(b, ids);
    }

    [Fact]
    public async Task RemoveStudentFromGroup_KeepsOtherGroup()
    {
        await using var hub = await StartHubAsync();
        var a = hub.CreateGroup("А");
        var b = hub.CreateGroup("Б");
        Assert.True(hub.AddStudentToGroup("student-1", a!));
        Assert.True(hub.AddStudentToGroup("student-1", b!));
        Assert.True(hub.RemoveStudentFromGroup("student-1", a!));
        var ids = hub.GetStudentGroupIds("student-1");
        Assert.Single(ids);
        Assert.Equal(b, ids[0]);
    }

    [Fact]
    public async Task DisbandGroup_FormerMembersHaveNullGroupId()
    {
        await using var hub = await StartHubAsync();
        var groupId = hub.CreateGroup("А");
        Assert.False(string.IsNullOrEmpty(groupId));
        Assert.True(hub.SetStudentGroup("student-1", groupId));
        Assert.True(hub.SetStudentGroup("student-2", groupId));
        Assert.True(hub.DisbandGroup(groupId));
        Assert.Empty(hub.Groups);
        Assert.Null(hub.GetStudentGroupId("student-1"));
        Assert.Null(hub.GetStudentGroupId("student-2"));
    }

    [Fact]
    public async Task DisbandGroup_KeepsMembershipInOtherGroups()
    {
        await using var hub = await StartHubAsync();
        var a = hub.CreateGroup("А");
        var b = hub.CreateGroup("Б");
        Assert.True(hub.AddStudentToGroup("student-1", a!));
        Assert.True(hub.AddStudentToGroup("student-1", b!));
        Assert.True(hub.DisbandGroup(a!));
        var ids = hub.GetStudentGroupIds("student-1");
        Assert.Single(ids);
        Assert.Equal(b, ids[0]);
    }

    [Fact]
    public async Task DisbandGroup_UnknownId_ReturnsFalse()
    {
        await using var hub = await StartHubAsync();
        hub.CreateGroup("А");
        Assert.False(hub.DisbandGroup(Guid.NewGuid().ToString()));
        Assert.Single(hub.Groups);
    }

    [Fact]
    public async Task RenameGroup_UpdatesName()
    {
        await using var hub = await StartHubAsync();
        var id = hub.CreateGroup("Старое");
        Assert.True(hub.RenameGroup(id!, "  Новое  "));
        Assert.Equal("Новое", hub.Groups.Single(g => g.Id == id).Name);
    }

    [Fact]
    public async Task RenameGroup_UnknownOrEmptyName_ReturnsFalse()
    {
        await using var hub = await StartHubAsync();
        var id = hub.CreateGroup("А");
        Assert.False(hub.RenameGroup(Guid.NewGuid().ToString(), "Б"));
        Assert.False(hub.RenameGroup(id!, ""));
        Assert.False(hub.RenameGroup(id!, "   "));
        Assert.Equal("А", hub.Groups.Single().Name);
    }

    [Fact]
    public async Task GetOnlineStudentIdsInGroup_WithoutJoin_IsEmpty()
    {
        await using var hub = await StartHubAsync();
        var groupId = hub.CreateGroup("А");
        Assert.True(hub.SetStudentGroup("student-offline", groupId));
        Assert.Empty(hub.GetOnlineStudentIdsInGroup(groupId!));
        Assert.Empty(hub.GetOnlineStudentIdsInGroup(Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task SendToGroup_UnknownOrEmptyGroup_ReturnsZero()
    {
        await using var hub = await StartHubAsync();
        var emptyGroup = hub.CreateGroup("Пустая");
        Assert.False(string.IsNullOrEmpty(emptyGroup));
        hub.SetStudentGroup("offline-student", emptyGroup);

        Assert.Equal(0, await hub.SendLaunchAppToGroupAsync(Guid.NewGuid().ToString(), "calc.exe"));
        Assert.Equal(0, await hub.SendLaunchAppToGroupAsync(emptyGroup!, "calc.exe"));
        Assert.Equal(0, await hub.SendBlockListToGroupAsync(Guid.NewGuid().ToString(), ["notepad.exe"]));
        Assert.Equal(0, await hub.SendBlockListToGroupAsync(emptyGroup!, ["notepad.exe"]));
        Assert.Equal(0, await hub.SendLaunchAppAfterBlockListToGroupAsync(
            Guid.NewGuid().ToString(), ["notepad.exe"], "calc.exe"));
        Assert.Equal(0, await hub.SendLaunchAppAfterBlockListToGroupAsync(
            emptyGroup!, ["notepad.exe"], "calc.exe"));
    }

    [Fact]
    public async Task StopClassAsync_ClearsGroups_AndCreateGroupDoesNotRepopulate()
    {
        var (udp, tcp) = TestPorts.NextPair();
        var hub = new ClassHub(udp, tcp);
        await hub.StartClassAsync("Класс-101", "1234");
        try
        {
            var groupId = hub.CreateGroup("А");
            hub.SetStudentGroup("student-1", groupId);
            Assert.NotEmpty(hub.Groups);
        }
        finally
        {
            await hub.StopClassAsync();
        }

        Assert.Empty(hub.Groups);
        Assert.Null(hub.GetStudentGroupId("student-1"));

        try
        {
            var afterStop = hub.CreateGroup("После стопа");
            Assert.True(string.IsNullOrEmpty(afterStop));
        }
        catch (InvalidOperationException)
        {
        }

        Assert.Empty(hub.Groups);
    }

    [Fact]
    public async Task KickStudent_WhenClassNotRunningOrUnknown_ReturnsFalse()
    {
        var (udp, tcp) = TestPorts.NextPair();
        var idle = new ClassHub(udp, tcp);
        Assert.False(await idle.KickStudentAsync("student-1"));

        await using var hub = await StartHubAsync();
        Assert.False(await hub.KickStudentAsync("missing"));
        Assert.False(await hub.KickStudentAsync(""));
    }

    [Fact]
    public async Task KickStudent_AfterJoin_SendsSessionEndAndDisconnects()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        await using var hub = new ClassHub(udpPort, tcpPort);
        await hub.StartClassAsync("Kick-class", "1234");

        var client = new ClassClient(
            pin: "1234",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Ученик-Kick");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var runTask = Task.Run(() => client.RunAsync(cts.Token));
        try
        {
            await WaitUntil.True(() => client.Session.Status == SessionStatus.Online, attempts: 80);
            Assert.Equal(SessionStatus.Online, client.Session.Status);
            var studentId = client.Session.StudentId!;
            var groupId = hub.CreateGroup("А");
            Assert.True(hub.AddStudentToGroup(studentId, groupId!));

            Assert.True(await hub.KickStudentAsync(studentId));
            await WaitUntil.True(() => client.Session.Status == SessionStatus.Idle, attempts: 80);
            Assert.Equal(SessionStatus.Idle, client.Session.Status);
            Assert.Empty(hub.GetStudentGroupIds(studentId));
            Assert.Equal(StudentHubStatus.Disconnected, hub.Students.Single(s => s.Id == studentId).Status);
        }
        finally
        {
            cts.Cancel();
            try { await runTask; } catch { }
            client.Dispose();
        }
    }
}
