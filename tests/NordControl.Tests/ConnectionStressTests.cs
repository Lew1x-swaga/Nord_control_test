using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

[Collection("NetworkTests")]
public class ConnectionStressTests
{
    [Fact]
    public async Task SequentialConnectDisconnect_MultipleCycles_CompletesCleanlyWithoutLeaks()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        var hub = new ClassHub(udpPort, tcpPort);
        await hub.StartClass("Stress-Room-Seq", "1234", CancellationToken.None);

        try
        {
            const int cycles = 5;
            for (var i = 0; i < cycles; i++)
            {
                var client = new ClassClient(
                    pin: "1234",
                    udpPort: udpPort,
                    tcpPort: tcpPort,
                    manualTeacherIp: "127.0.0.1",
                    displayName: $"Student-{i}"
                );

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var runTask = client.RunAsync(cts.Token);

                try
                {
                    var timeoutAt = DateTime.UtcNow.AddSeconds(3);
                    while (client.Session.Status != SessionStatus.Online && DateTime.UtcNow < timeoutAt)
                    {
                        await Task.Delay(30);
                    }

                    Assert.Equal(SessionStatus.Online, client.Session.Status);
                }
                finally
                {
                    client.RequestStop();
                    try
                    {
                        await runTask;
                    }
                    catch (OperationCanceledException) { }
                    client.Dispose();
                }

                // Short pause between iterations to let TCP socket close cleanly
                await Task.Delay(50);
            }
        }
        finally
        {
            await hub.StopClassAsync();
        }
    }

    [Fact]
    public async Task ConcurrentClients_ConnectAndDisconnect_HandlesLoadWithoutCrashes()
    {
        var (udpPort, tcpPort) = TestPorts.NextPair();
        var hub = new ClassHub(udpPort, tcpPort);
        await hub.StartClass("Stress-Room-Conc", "1234", CancellationToken.None);

        try
        {
            const int clientCount = 4;
            var clients = new List<ClassClient>();
            var runTasks = new List<Task>();
            var ctsList = new List<CancellationTokenSource>();

            for (var i = 0; i < clientCount; i++)
            {
                var client = new ClassClient(
                    pin: "1234",
                    udpPort: udpPort,
                    tcpPort: tcpPort,
                    manualTeacherIp: "127.0.0.1",
                    displayName: $"Student-Conc-{i}"
                );
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                clients.Add(client);
                ctsList.Add(cts);
                runTasks.Add(client.RunAsync(cts.Token));
            }

            // Wait for all clients to reach Online
            var timeoutAt = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < timeoutAt)
            {
                var onlineCount = 0;
                foreach (var c in clients)
                {
                    if (c.Session.Status == SessionStatus.Online)
                    {
                        onlineCount++;
                    }
                }

                if (onlineCount == clientCount)
                {
                    break;
                }

                await Task.Delay(40);
            }

            foreach (var c in clients)
            {
                Assert.Equal(SessionStatus.Online, c.Session.Status);
            }

            // Clean shutdown all clients
            for (var i = 0; i < clientCount; i++)
            {
                clients[i].RequestStop();
                try
                {
                    await runTasks[i];
                }
                catch (OperationCanceledException) { }
                clients[i].Dispose();
                ctsList[i].Dispose();
            }
        }
        finally
        {
            await hub.StopClassAsync();
        }
    }
}
