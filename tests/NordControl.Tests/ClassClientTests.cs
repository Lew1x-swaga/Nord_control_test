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
public class ClassClientTests
{
    private const int TestUdpPort = 47832;
    private const int TestTcpPort = 47833;

    [Fact]
    public async Task Connect_SuccessfulJoin_SetsSessionOnline()
    {
        var hub = new ClassHub(TestUdpPort, TestTcpPort);
        await hub.StartClass("Лаборатория-1", "1234", CancellationToken.None);

        var client = new ClassClient(
            pin: "1234",
            udpPort: TestUdpPort,
            tcpPort: TestTcpPort,
            manualTeacherIp: "127.0.0.1"
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
            Assert.NotNull(client.Session.StudentId);
            Assert.NotNull(client.Session.SessionToken);
            Assert.True(client.Session.ShouldHoldPolicies);
        }
        finally
        {
            client.RequestStop();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException) { }

            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task Connect_BadPin_TriggersErrorEvent_DoesNotCrash()
    {
        var hub = new ClassHub(TestUdpPort + 2, TestTcpPort + 2);
        await hub.StartClass("Лаборатория-2", "1234", CancellationToken.None);

        var client = new ClassClient(
            pin: "9999", // Bad PIN
            udpPort: TestUdpPort + 2,
            tcpPort: TestTcpPort + 2,
            manualTeacherIp: "127.0.0.1"
        );

        string? receivedError = null;
        client.Error += err => receivedError = err;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var runTask = client.RunAsync(cts.Token);

        try
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(3);
            while (receivedError == null && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }

            Assert.NotNull(receivedError);
            Assert.NotEqual(SessionStatus.Online, client.Session.Status);
        }
        finally
        {
            client.RequestStop();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException) { }

            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task SessionEnd_FromHub_ResetsClientToIdle()
    {
        var hub = new ClassHub(TestUdpPort + 4, TestTcpPort + 4);
        await hub.StartClass("Лаборатория-3", "1234", CancellationToken.None);

        var client = new ClassClient(
            pin: "1234",
            udpPort: TestUdpPort + 4,
            tcpPort: TestTcpPort + 4,
            manualTeacherIp: "127.0.0.1"
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var runTask = client.RunAsync(cts.Token);

        try
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(3);
            while (client.Session.Status != SessionStatus.Online && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }

            Assert.Equal(SessionStatus.Online, client.Session.Status);

            // Stop teacher class -> sends session_end
            await hub.StopClassAsync();

            timeoutAt = DateTime.UtcNow.AddSeconds(3);
            while (client.Session.Status != SessionStatus.Idle && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(50);
            }

            Assert.Equal(SessionStatus.Idle, client.Session.Status);
            Assert.False(client.Session.ShouldHoldPolicies);
        }
        finally
        {
            client.RequestStop();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task DiscoveryViaUdpProbe_FindsTeacherAndConnects()
    {
        var hub = new ClassHub(TestUdpPort + 6, TestTcpPort + 6);
        await hub.StartClass("Лаборатория-4", "5555", CancellationToken.None);

        // Client without manual IP -> uses UDP broadcast / loopback probe
        var client = new ClassClient(
            pin: "5555",
            udpPort: TestUdpPort + 6,
            tcpPort: TestTcpPort + 6,
            manualTeacherIp: null
        );

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
            Assert.Equal("5555", client.Pin);
        }
        finally
        {
            client.RequestStop();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException) { }

            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task SuddenTcpDrop_TransitionsToReconnecting_AndReconnects()
    {
        var tcpPort = TestTcpPort + 8;
        var listener = new TcpListener(IPAddress.Loopback, tcpPort);
        listener.Start();

        var client = new ClassClient(
            pin: "1234",
            udpPort: TestUdpPort + 8,
            tcpPort: tcpPort,
            manualTeacherIp: "127.0.0.1"
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var runTask = client.RunAsync(cts.Token);

        try
        {
            // Accept first connection and send join_ok
            using (var serverClient = await listener.AcceptTcpClientAsync(cts.Token))
            using (var stream = serverClient.GetStream())
            {
                var frame = await FrameCodec.ReadAsync(stream, cts.Token);
                Assert.NotNull(frame);

                var joinOkMsg = new WireMessage
                {
                    V = ProtocolConstants.Version,
                    Type = "join_ok",
                    StudentId = "test-student-id",
                    SessionToken = "test-token-123"
                };
                await FrameCodec.WriteAsync(stream, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(joinOkMsg), cts.Token);

                // Wait for client to become Online
                var timeout = DateTime.UtcNow.AddSeconds(3);
                while (client.Session.Status != SessionStatus.Online && DateTime.UtcNow < timeout)
                {
                    await Task.Delay(50);
                }
                Assert.Equal(SessionStatus.Online, client.Session.Status);

                // Abruptly close server socket without sending session_end
                serverClient.Client.Close(0);
            }

            // Client should transition to Reconnecting
            var reconnectTimeout = DateTime.UtcNow.AddSeconds(3);
            while (client.Session.Status != SessionStatus.Reconnecting && DateTime.UtcNow < reconnectTimeout)
            {
                await Task.Delay(50);
            }

            Assert.Equal(SessionStatus.Reconnecting, client.Session.Status);
            Assert.True(client.Session.ShouldHoldPolicies);

            // Accept reconnected client
            using (var serverClient2 = await listener.AcceptTcpClientAsync(cts.Token))
            using (var stream2 = serverClient2.GetStream())
            {
                var frame2 = await FrameCodec.ReadAsync(stream2, cts.Token);
                Assert.NotNull(frame2);
                var reconnectJoin = WireMessage.Deserialize(frame2!.Value.Payload);
                Assert.NotNull(reconnectJoin);
                Assert.Equal("test-token-123", reconnectJoin!.SessionToken);

                var joinOkMsg2 = new WireMessage
                {
                    V = ProtocolConstants.Version,
                    Type = "join_ok",
                    StudentId = "test-student-id",
                    SessionToken = "test-token-123"
                };
                await FrameCodec.WriteAsync(stream2, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(joinOkMsg2), cts.Token);

                // Client should become Online again
                var onlineTimeout = DateTime.UtcNow.AddSeconds(3);
                while (client.Session.Status != SessionStatus.Online && DateTime.UtcNow < onlineTimeout)
                {
                    await Task.Delay(50);
                }

                Assert.Equal(SessionStatus.Online, client.Session.Status);
            }
        }
        finally
        {
            client.RequestStop();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException) { }

            listener.Stop();
        }
    }

    [Fact]
    public void Tick_After120sWithoutMessages_EndsSession()
    {
        var clock = new FakeClock();
        var client = new ClassClient(
            pin: "1234",
            udpPort: TestUdpPort + 10,
            tcpPort: TestTcpPort + 10,
            clock: clock
        );

        client.Session.OnJoinOk("st-1", "tok-1", clock.UtcNow);
        Assert.Equal(SessionStatus.Online, client.Session.Status);

        client.Session.OnTcpDropped();
        Assert.Equal(SessionStatus.Reconnecting, client.Session.Status);
        Assert.True(client.Session.ShouldHoldPolicies);

        // Advance 120s
        clock.Advance(TimeSpan.FromMilliseconds(ProtocolConstants.ReconnectWindowMs));
        client.Session.Tick(clock.UtcNow);

        Assert.Equal(SessionStatus.Ended, client.Session.Status);
        Assert.False(client.Session.ShouldHoldPolicies);
    }
}
