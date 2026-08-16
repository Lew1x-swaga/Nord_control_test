using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

[Collection("NetworkTests")]
public class ClassHubTests
{
    private const int TestUdpPort = 47830;
    private const int TestTcpPort = 47831;

    [Fact]
    public async Task StartClass_PortBusy_ThrowsExpectedException()
    {
        using var blockerTcp = new TcpListener(IPAddress.Any, TestTcpPort);
        blockerTcp.Start();

        var hub = new ClassHub(TestUdpPort, TestTcpPort);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await hub.StartClass("TestClass", "1234", CancellationToken.None);
        });

        Assert.Contains("порт занят — закройте второй Teacher", ex.Message);
    }

    [Fact]
    public async Task UdpProbe_RespondsWithAnnounce_WithoutPin()
    {
        var hub = new ClassHub(TestUdpPort, TestTcpPort);
        await hub.StartClass("Класс-101", "5678", CancellationToken.None);

        try
        {
            using var udpClient = new UdpClient();
            var probeBytes = Encoding.UTF8.GetBytes(UdpPackets.Probe());
            await udpClient.SendAsync(probeBytes, probeBytes.Length, new IPEndPoint(IPAddress.Loopback, TestUdpPort));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var receiveTask = udpClient.ReceiveAsync(cts.Token).AsTask();
            var result = await receiveTask;

            var responseText = Encoding.UTF8.GetString(result.Buffer);
            Assert.Contains(ProtocolConstants.UdpMagic, responseText);
            Assert.Contains("announce", responseText);
            Assert.Contains("Класс-101", responseText);
            Assert.DoesNotContain("5678", responseText);
            Assert.DoesNotContain("pin=", responseText);

            Assert.True(UdpPackets.TryParseAnnounce(responseText, out var name, out var ip, out var tcpPort));
            Assert.Equal("Класс-101", name);
            Assert.Equal(TestTcpPort, tcpPort);
        }
        finally
        {
            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task JoinClass_BadPin_RejectsAndClosesSocket()
    {
        var hub = new ClassHub(TestUdpPort, TestTcpPort);
        await hub.StartClass("Класс-101", "1234", CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, TestTcpPort);
            using var stream = client.GetStream();

            var joinMsg = new WireMessage
            {
                V = ProtocolConstants.Version,
                Type = "join_class",
                Pin = "9999",
                DisplayName = "Иван",
                Hostname = "DESKTOP-IVAN",
                AgentVersion = ProtocolConstants.AgentVersion
            };

            await FrameCodec.WriteAsync(stream, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(joinMsg), CancellationToken.None);

            var responseFrame = await FrameCodec.ReadAsync(stream, CancellationToken.None);
            Assert.NotNull(responseFrame);
            Assert.Equal(ProtocolConstants.JsonMessageType, responseFrame!.Value.Type);

            var reply = WireMessage.Deserialize(responseFrame.Value.Payload);
            Assert.NotNull(reply);
            Assert.Equal("join_reject", reply!.Type);
            Assert.Equal("bad_pin", reply.Reason);

            // Socket should be closed by server
            var nextFrame = await FrameCodec.ReadAsync(stream, CancellationToken.None);
            Assert.Null(nextFrame);
        }
        finally
        {
            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task JoinClass_CorrectPin_ReturnsJoinOkWithGuids()
    {
        var hub = new ClassHub(TestUdpPort, TestTcpPort);
        ConnectedStudent? joinedStudent = null;
        hub.StudentJoined += s => joinedStudent = s;

        await hub.StartClass("Класс-101", "1234", CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, TestTcpPort);
            using var stream = client.GetStream();

            var joinMsg = new WireMessage
            {
                V = ProtocolConstants.Version,
                Type = "join_class",
                Pin = "1234",
                DisplayName = "Иван",
                Hostname = "DESKTOP-IVAN",
                AgentVersion = ProtocolConstants.AgentVersion
            };

            await FrameCodec.WriteAsync(stream, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(joinMsg), CancellationToken.None);

            var responseFrame = await FrameCodec.ReadAsync(stream, CancellationToken.None);
            Assert.NotNull(responseFrame);
            var reply = WireMessage.Deserialize(responseFrame!.Value.Payload);

            Assert.NotNull(reply);
            Assert.Equal("join_ok", reply!.Type);
            Assert.NotNull(reply.StudentId);
            Assert.NotNull(reply.SessionToken);
            Assert.Equal(ProtocolConstants.HeartbeatIntervalMs, reply.HeartbeatIntervalMs);
            Assert.Equal(ProtocolConstants.ReconnectWindowMs, reply.ReconnectWindowMs);

            Assert.NotNull(joinedStudent);
            Assert.Equal("Иван", joinedStudent!.DisplayName);
            Assert.Equal("DESKTOP-IVAN", joinedStudent.Hostname);
            Assert.Equal(reply.StudentId, joinedStudent.Id);
            Assert.Equal(StudentHubStatus.Online, joinedStudent.Status);
        }
        finally
        {
            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task JoinClass_ReconnectWithToken_PreservesStudentId()
    {
        var hub = new ClassHub(TestUdpPort, TestTcpPort);
        await hub.StartClass("Класс-101", "1234", CancellationToken.None);

        try
        {
            string studentId;
            string sessionToken;

            // First connection
            using (var client1 = new TcpClient())
            {
                await client1.ConnectAsync(IPAddress.Loopback, TestTcpPort);
                using var stream1 = client1.GetStream();

                var joinMsg = new WireMessage
                {
                    V = ProtocolConstants.Version,
                    Type = "join_class",
                    Pin = "1234",
                    DisplayName = "Иван",
                    Hostname = "DESKTOP-IVAN"
                };

                await FrameCodec.WriteAsync(stream1, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(joinMsg), CancellationToken.None);
                var frame1 = await FrameCodec.ReadAsync(stream1, CancellationToken.None);
                var reply1 = WireMessage.Deserialize(frame1!.Value.Payload)!;

                studentId = reply1.StudentId!;
                sessionToken = reply1.SessionToken!;
            }

            // Reconnect
            using (var client2 = new TcpClient())
            {
                await client2.ConnectAsync(IPAddress.Loopback, TestTcpPort);
                using var stream2 = client2.GetStream();

                var reconnectMsg = new WireMessage
                {
                    V = ProtocolConstants.Version,
                    Type = "join_class",
                    Pin = "1234",
                    DisplayName = "Иван",
                    Hostname = "DESKTOP-IVAN",
                    SessionToken = sessionToken
                };

                await FrameCodec.WriteAsync(stream2, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(reconnectMsg), CancellationToken.None);
                var frame2 = await FrameCodec.ReadAsync(stream2, CancellationToken.None);
                var reply2 = WireMessage.Deserialize(frame2!.Value.Payload)!;

                Assert.Equal("join_ok", reply2.Type);
                Assert.Equal(studentId, reply2.StudentId);
                Assert.Equal(sessionToken, reply2.SessionToken);
            }
        }
        finally
        {
            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task StopClassAsync_SendsSessionEndToClients()
    {
        var hub = new ClassHub(TestUdpPort, TestTcpPort);
        await hub.StartClass("Класс-101", "1234", CancellationToken.None);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, TestTcpPort);
        using var stream = client.GetStream();

        var joinMsg = new WireMessage
        {
            V = ProtocolConstants.Version,
            Type = "join_class",
            Pin = "1234",
            DisplayName = "Иван",
            Hostname = "DESKTOP-IVAN"
        };

        await FrameCodec.WriteAsync(stream, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(joinMsg), CancellationToken.None);
        var joinOkFrame = await FrameCodec.ReadAsync(stream, CancellationToken.None);
        Assert.NotNull(joinOkFrame);

        // Teacher stops class
        await hub.StopClassAsync();

        // Client should receive session_end frame
        var endFrame = await FrameCodec.ReadAsync(stream, CancellationToken.None);
        Assert.NotNull(endFrame);
        var endMsg = WireMessage.Deserialize(endFrame!.Value.Payload);
        Assert.NotNull(endMsg);
        Assert.Equal("session_end", endMsg!.Type);
        Assert.Equal("class_ended", endMsg.Reason);
    }

    [Fact]
    public async Task JoinClass_VersionMismatch_RejectsWithVersionReason()
    {
        var hub = new ClassHub(TestUdpPort, TestTcpPort);
        await hub.StartClass("Класс-101", "1234", CancellationToken.None);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, TestTcpPort);
            using var stream = client.GetStream();

            var joinMsg = new WireMessage
            {
                V = 99, // Incompatible version
                Type = "join_class",
                Pin = "1234",
                DisplayName = "Иван",
                Hostname = "DESKTOP-IVAN"
            };

            await FrameCodec.WriteAsync(stream, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(joinMsg), CancellationToken.None);

            var responseFrame = await FrameCodec.ReadAsync(stream, CancellationToken.None);
            Assert.NotNull(responseFrame);
            var reply = WireMessage.Deserialize(responseFrame!.Value.Payload);

            Assert.NotNull(reply);
            Assert.Equal("join_reject", reply!.Type);
            Assert.Equal("version", reply.Reason);
        }
        finally
        {
            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task StartClass_WhenAlreadyRunning_ThrowsInvalidOperationException()
    {
        var hub = new ClassHub(TestUdpPort, TestTcpPort);
        await hub.StartClass("Класс-101", "1234", CancellationToken.None);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await hub.StartClass("Класс-102", "5678", CancellationToken.None);
            });
        }
        finally
        {
            await hub.StopClassAsync();
        }
    }
}
