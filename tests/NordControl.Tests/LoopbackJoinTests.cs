using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class LoopbackJoinTests
{
    private const int BaseUdpPort = 47850;
    private const int BaseTcpPort = 47851;

    [Fact]
    public async Task LoopbackJoin_ValidPin_EntersOnlineAndAppearsInHub()
    {
        const int udpPort = BaseUdpPort;
        const int tcpPort = BaseTcpPort;

        var hub = new ClassHub(udpPort, tcpPort);
        ConnectedStudent? joinedStudent = null;
        hub.StudentJoined += s => joinedStudent = s;

        await hub.StartClass("Информатика 10-А", "1234", CancellationToken.None);

        var client = new ClassClient(
            pin: "1234",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Алексей"
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

            // Student assertions
            Assert.Equal(SessionStatus.Online, client.Session.Status);
            Assert.NotNull(client.Session.StudentId);
            Assert.NotNull(client.Session.SessionToken);
            Assert.True(client.Session.ShouldHoldPolicies);

            // Hub assertions
            Assert.NotNull(joinedStudent);
            Assert.Equal("Алексей", joinedStudent!.DisplayName);
            Assert.Equal(client.Session.StudentId, joinedStudent.Id);
            Assert.Equal(StudentHubStatus.Online, joinedStudent.Status);

            var hubStudents = hub.Students;
            Assert.Single(hubStudents);
            Assert.Contains(hubStudents, s => s.Id == client.Session.StudentId && s.Status == StudentHubStatus.Online);
        }
        finally
        {
            client.RequestStop();
            try { await runTask; } catch (OperationCanceledException) { }
            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task LoopbackJoin_InvalidPin_RejectedAndNotAppearsInHub()
    {
        const int udpPort = BaseUdpPort + 2;
        const int tcpPort = BaseTcpPort + 2;

        var hub = new ClassHub(udpPort, tcpPort);
        ConnectedStudent? joinedStudent = null;
        hub.StudentJoined += s => joinedStudent = s;

        await hub.StartClass("Информатика 10-А", "1234", CancellationToken.None);

        var client = new ClassClient(
            pin: "9999", // Invalid PIN
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Злоумышленник"
        );

        string? receivedError = null;
        client.Error += err => receivedError = err;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var runTask = client.RunAsync(cts.Token);

        try
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(3);
            while (receivedError == null && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }

            // Client received reject error and remains alive and not Online
            Assert.NotNull(receivedError);
            Assert.Contains("PIN", receivedError, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(SessionStatus.Online, client.Session.Status);
            Assert.False(client.Session.ShouldHoldPolicies);

            // Hub did not accept student
            Assert.Null(joinedStudent);
            Assert.Empty(hub.Students);
        }
        finally
        {
            client.RequestStop();
            try { await runTask; } catch (OperationCanceledException) { }
            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task LoopbackJoin_StopClass_NotifiesStudentAndSetsEnded()
    {
        const int udpPort = BaseUdpPort + 4;
        const int tcpPort = BaseTcpPort + 4;

        var hub = new ClassHub(udpPort, tcpPort);
        await hub.StartClass("Информатика 10-А", "1234", CancellationToken.None);

        var client = new ClassClient(
            pin: "1234",
            udpPort: udpPort,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1",
            displayName: "Мария"
        );

        var recordedStatuses = new List<SessionStatus>();
        client.StatusChanged += session =>
        {
            lock (recordedStatuses)
            {
                recordedStatuses.Add(session.Status);
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var runTask = client.RunAsync(cts.Token);

        try
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(4);
            while (client.Session.Status != SessionStatus.Online && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }

            Assert.Equal(SessionStatus.Online, client.Session.Status);
            Assert.True(client.Session.ShouldHoldPolicies);

            // Teacher stops class
            await hub.StopClassAsync();

            // Student receives session_end -> Ended -> resets to Idle
            timeoutAt = DateTime.UtcNow.AddSeconds(4);
            while (client.Session.Status != SessionStatus.Idle && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }

            Assert.Equal(SessionStatus.Idle, client.Session.Status);
            Assert.False(client.Session.ShouldHoldPolicies);

            lock (recordedStatuses)
            {
                Assert.Contains(SessionStatus.Online, recordedStatuses);
                Assert.Contains(SessionStatus.Ended, recordedStatuses);
                Assert.Contains(SessionStatus.Idle, recordedStatuses);
            }
        }
        finally
        {
            client.RequestStop();
            try { await runTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task LoopbackJoin_MultipleStudents_Simultaneous()
    {
        const int udpPort = BaseUdpPort + 6;
        const int tcpPort = BaseTcpPort + 6;

        var hub = new ClassHub(udpPort, tcpPort);
        var joinedStudents = new List<ConnectedStudent>();
        var joinLock = new object();
        hub.StudentJoined += s =>
        {
            lock (joinLock)
            {
                joinedStudents.Add(s);
            }
        };

        await hub.StartClass("Информатика 10-А", "1234", CancellationToken.None);

        var clients = new List<ClassClient>
        {
            new("1234", udpPort, tcpPort, manualTeacherIp: "127.0.0.1", displayName: "Ученик-1"),
            new("1234", udpPort, tcpPort, manualTeacherIp: "127.0.0.1", displayName: "Ученик-2"),
            new("1234", udpPort, tcpPort, manualTeacherIp: "127.0.0.1", displayName: "Ученик-3")
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var runTasks = clients.Select(c => c.RunAsync(cts.Token)).ToList();

        try
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(5);
            while (clients.Any(c => c.Session.Status != SessionStatus.Online) && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }

            foreach (var client in clients)
            {
                Assert.Equal(SessionStatus.Online, client.Session.Status);
                Assert.NotNull(client.Session.StudentId);
                Assert.NotNull(client.Session.SessionToken);
                Assert.True(client.Session.ShouldHoldPolicies);
            }

            // Verify unique IDs and tokens for each student
            var studentIds = clients.Select(c => c.Session.StudentId).Distinct().ToList();
            var tokens = clients.Select(c => c.Session.SessionToken).Distinct().ToList();
            Assert.Equal(3, studentIds.Count);
            Assert.Equal(3, tokens.Count);

            // Verify hub state
            var hubStudents = hub.Students;
            Assert.Equal(3, hubStudents.Count);
            Assert.All(hubStudents, s => Assert.Equal(StudentHubStatus.Online, s.Status));

            lock (joinLock)
            {
                Assert.Equal(3, joinedStudents.Count);
            }
        }
        finally
        {
            foreach (var client in clients)
            {
                client.RequestStop();
            }

            try
            {
                await Task.WhenAll(runTasks);
            }
            catch (OperationCanceledException) { }

            await hub.StopClassAsync();
        }
    }
}
