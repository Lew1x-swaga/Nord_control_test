using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Protocol;

namespace NordControl.Core;

public enum StudentHubStatus
{
    Online,
    Reconnecting,
    Disconnected
}

public record ConnectedStudent(
    string Id,
    string DisplayName,
    string Hostname,
    string? SessionToken,
    DateTime LastSeen,
    StudentHubStatus Status
);

public class ClassHub : IAsyncDisposable
{
    private readonly int _udpPort;
    private readonly int _tcpPort;
    private readonly IClock _clock;

    private string? _className;
    private string? _pin;
    private CancellationTokenSource? _cts;

    private UdpClient? _udpListener;
    private TcpListener? _tcpListener;

    private readonly ConcurrentDictionary<string, ConnectedStudent> _studentsById = new();
    private readonly ConcurrentDictionary<string, string> _studentIdByToken = new();
    private readonly ConcurrentDictionary<string, StudentConnection> _activeConnections = new();

    private Task? _udpLoopTask;
    private Task? _tcpAcceptLoopTask;
    private Task? _maintenanceLoopTask;

    private int _heartbeatSeq;
    private bool _isDisposed;

    public int UdpPort => _udpPort;
    public int TcpPort => _tcpPort;
    public string? ClassName => _className;
    public string? Pin => _pin;
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    public IReadOnlyCollection<ConnectedStudent> Students => _studentsById.Values.ToList();

    public event Action<ConnectedStudent>? StudentJoined;
    public event Action<ConnectedStudent>? StudentStatusChanged;
    public event Action<ConnectedStudent>? StudentLeft;

    private sealed class StudentConnection
    {
        public string StudentId { get; }
        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);

        public StudentConnection(string studentId, TcpClient client, NetworkStream stream)
        {
            StudentId = studentId;
            Client = client;
            Stream = stream;
        }
    }

    public ClassHub(int udpPort = ProtocolConstants.UdpPort, int tcpPort = ProtocolConstants.TcpPort, IClock? clock = null)
    {
        _udpPort = udpPort;
        _tcpPort = tcpPort;
        _clock = clock ?? new SystemClock();
    }

    public Task StartClass(string className, string pin, CancellationToken ct = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Класс уже запущен");
        }

        _className = className;
        _pin = pin;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            _udpListener = new UdpClient(new IPEndPoint(IPAddress.Any, _udpPort));
            _udpListener.EnableBroadcast = true;
        }
        catch (SocketException ex)
        {
            CleanupListeners();
            throw new InvalidOperationException("порт занят — закройте второй Teacher", ex);
        }

        try
        {
            _tcpListener = new TcpListener(IPAddress.Any, _tcpPort);
            _tcpListener.Start();
        }
        catch (SocketException ex)
        {
            CleanupListeners();
            throw new InvalidOperationException("порт занят — закройте второй Teacher", ex);
        }

        _udpLoopTask = Task.Run(() => UdpLoopAsync(_cts.Token));
        _tcpAcceptLoopTask = Task.Run(() => TcpAcceptLoopAsync(_cts.Token));
        _maintenanceLoopTask = Task.Run(() => MaintenanceLoopAsync(_cts.Token));

        return Task.CompletedTask;
    }

    public Task StartClassAsync(string className, string pin, CancellationToken ct = default) =>
        StartClass(className, pin, ct);

    public async Task StopClassAsync()
    {
        if (_cts == null || _cts.IsCancellationRequested)
        {
            return;
        }

        CleanupListeners();

        var sessionEnd = new WireMessage
        {
            V = ProtocolConstants.Version,
            Type = "session_end",
            Reason = "class_ended"
        };
        var endPayload = WireMessage.SerializeUtf8(sessionEnd);

        var activeConns = _activeConnections.Values.ToList();
        foreach (var conn in activeConns)
        {
            try
            {
                await conn.SendLock.WaitAsync(TimeSpan.FromMilliseconds(500));
                try
                {
                    await FrameCodec.WriteAsync(conn.Stream, ProtocolConstants.JsonMessageType, endPayload, CancellationToken.None);
                    try
                    {
                        if (conn.Client.Connected)
                        {
                            conn.Client.Client?.Shutdown(SocketShutdown.Send);
                        }
                    }
                    catch { }
                }
                finally
                {
                    conn.SendLock.Release();
                }
            }
            catch
            {
                // Ignore errors during termination
            }
        }

        // Allow a brief moment for clients on loopback/LAN to process session_end frame
        await Task.Delay(50);

        _cts.Cancel();

        foreach (var conn in activeConns)
        {
            try
            {
                conn.Client.Close();
                conn.Client.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
        _activeConnections.Clear();

        if (_udpLoopTask != null)
        {
            try { await _udpLoopTask; } catch { }
        }
        if (_tcpAcceptLoopTask != null)
        {
            try { await _tcpAcceptLoopTask; } catch { }
        }
        if (_maintenanceLoopTask != null)
        {
            try { await _maintenanceLoopTask; } catch { }
        }

        foreach (var student in _studentsById.Values)
        {
            if (student.Status != StudentHubStatus.Disconnected)
            {
                var updated = student with { Status = StudentHubStatus.Disconnected };
                _studentsById[student.Id] = updated;
                StudentStatusChanged?.Invoke(updated);
                StudentLeft?.Invoke(updated);
            }
        }

        _cts.Dispose();
        _cts = null;
    }

    private void CleanupListeners()
    {
        try
        {
            _udpListener?.Close();
            _udpListener?.Dispose();
        }
        catch { }
        _udpListener = null;

        try
        {
            _tcpListener?.Stop();
        }
        catch { }
        _tcpListener = null;
    }

    private async Task UdpLoopAsync(CancellationToken ct)
    {
        var probeHeader = $"{ProtocolConstants.UdpMagic}|probe";

        while (!ct.IsCancellationRequested && _udpListener != null)
        {
            try
            {
                var result = await _udpListener.ReceiveAsync(ct);
                var text = Encoding.UTF8.GetString(result.Buffer);

                if (text.StartsWith(probeHeader, StringComparison.Ordinal))
                {
                    var localIp = GetLocalIpAddress(result.RemoteEndPoint.Address);
                    var announcePacket = UdpPackets.Announce(_className ?? "Класс", localIp, _tcpPort);
                    var announceBytes = Encoding.UTF8.GetBytes(announcePacket);

                    await _udpListener.SendAsync(announceBytes, announceBytes.Length, result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (ct.IsCancellationRequested) break;
            }
            catch
            {
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    private async Task TcpAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _tcpListener != null)
        {
            try
            {
                var client = await _tcpListener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleTcpClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (ct.IsCancellationRequested) break;
            }
            catch
            {
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken ct)
    {
        client.NoDelay = true;
        client.LingerState = new LingerOption(true, 2);
        NetworkStream? stream = null;
        string? assignedStudentId = null;

        try
        {
            stream = client.GetStream();

            var initialFrame = await FrameCodec.ReadAsync(stream, ct);
            if (initialFrame == null || initialFrame.Value.Type != ProtocolConstants.JsonMessageType)
            {
                CloseClient(client);
                return;
            }

            var joinMsg = WireMessage.Deserialize(initialFrame.Value.Payload);
            if (joinMsg == null || joinMsg.Type != "join_class")
            {
                CloseClient(client);
                return;
            }

            if (joinMsg.V != ProtocolConstants.Version)
            {
                var rejectVersion = new WireMessage
                {
                    V = ProtocolConstants.Version,
                    Type = "join_reject",
                    Reason = "version",
                    Message = "Несовместимая версия"
                };
                await FrameCodec.WriteAsync(stream, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(rejectVersion), ct);
                CloseClient(client);
                return;
            }

            if (joinMsg.Pin != _pin)
            {
                var rejectPin = new WireMessage
                {
                    V = ProtocolConstants.Version,
                    Type = "join_reject",
                    Reason = "bad_pin",
                    Message = "Неверный PIN"
                };
                await FrameCodec.WriteAsync(stream, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(rejectPin), ct);
                CloseClient(client);
                return;
            }

            string studentId;
            string sessionToken;
            bool isReconnect = false;

            if (!string.IsNullOrEmpty(joinMsg.SessionToken) && _studentIdByToken.TryGetValue(joinMsg.SessionToken, out var existingId))
            {
                studentId = existingId;
                sessionToken = joinMsg.SessionToken;
                isReconnect = true;
            }
            else
            {
                studentId = Guid.NewGuid().ToString();
                sessionToken = Guid.NewGuid().ToString();
            }

            assignedStudentId = studentId;

            var student = new ConnectedStudent(
                studentId,
                joinMsg.DisplayName ?? "Ученик",
                joinMsg.Hostname ?? string.Empty,
                sessionToken,
                _clock.UtcNow,
                StudentHubStatus.Online
            );

            _studentsById[studentId] = student;
            _studentIdByToken[sessionToken] = studentId;

            var connection = new StudentConnection(studentId, client, stream);

            if (_activeConnections.TryRemove(studentId, out var oldConn))
            {
                try
                {
                    oldConn.Client.Close();
                    oldConn.Client.Dispose();
                }
                catch { }
            }

            _activeConnections[studentId] = connection;

            var joinOk = new WireMessage
            {
                V = ProtocolConstants.Version,
                Type = "join_ok",
                StudentId = studentId,
                SessionToken = sessionToken,
                HeartbeatIntervalMs = ProtocolConstants.HeartbeatIntervalMs,
                ReconnectWindowMs = ProtocolConstants.ReconnectWindowMs
            };

            await connection.SendLock.WaitAsync(ct);
            try
            {
                await FrameCodec.WriteAsync(stream, ProtocolConstants.JsonMessageType, WireMessage.SerializeUtf8(joinOk), ct);
            }
            finally
            {
                connection.SendLock.Release();
            }

            if (isReconnect)
            {
                StudentStatusChanged?.Invoke(student);
            }
            else
            {
                StudentJoined?.Invoke(student);
            }

            // Client message loop
            while (!ct.IsCancellationRequested && client.Connected)
            {
                var frame = await FrameCodec.ReadAsync(stream, ct);
                if (frame == null)
                {
                    break;
                }

                UpdateStudentActivity(studentId);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            if (assignedStudentId != null)
            {
                if (_activeConnections.TryGetValue(assignedStudentId, out var active) && ReferenceEquals(active.Client, client))
                {
                    _activeConnections.TryRemove(assignedStudentId, out _);
                }

                if (!ct.IsCancellationRequested && _studentsById.TryGetValue(assignedStudentId, out var currentStudent))
                {
                    if (currentStudent.Status == StudentHubStatus.Online)
                    {
                        var updated = currentStudent with { Status = StudentHubStatus.Reconnecting };
                        _studentsById[assignedStudentId] = updated;
                        StudentStatusChanged?.Invoke(updated);
                    }
                }
            }

            CloseClient(client);
        }
    }

    private void UpdateStudentActivity(string studentId)
    {
        if (_studentsById.TryGetValue(studentId, out var student))
        {
            var updated = student with { LastSeen = _clock.UtcNow, Status = StudentHubStatus.Online };
            _studentsById[studentId] = updated;
            if (student.Status != StudentHubStatus.Online)
            {
                StudentStatusChanged?.Invoke(updated);
            }
        }
    }

    private async Task MaintenanceLoopAsync(CancellationToken ct)
    {
        var lastHeartbeatTime = _clock.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, ct);

                var now = _clock.UtcNow;

                // 1. Heartbeat check
                if ((now - lastHeartbeatTime).TotalMilliseconds >= ProtocolConstants.HeartbeatIntervalMs)
                {
                    lastHeartbeatTime = now;
                    var seq = Interlocked.Increment(ref _heartbeatSeq);
                    var heartbeatMsg = new WireMessage
                    {
                        V = ProtocolConstants.Version,
                        Type = "heartbeat",
                        Seq = seq
                    };
                    var heartbeatPayload = WireMessage.SerializeUtf8(heartbeatMsg);

                    var activeConns = _activeConnections.Values.ToList();
                    foreach (var conn in activeConns)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (await conn.SendLock.WaitAsync(1000, ct))
                                {
                                    try
                                    {
                                        await FrameCodec.WriteAsync(conn.Stream, ProtocolConstants.JsonMessageType, heartbeatPayload, ct);
                                    }
                                    finally
                                    {
                                        conn.SendLock.Release();
                                    }
                                }
                            }
                            catch
                            {
                                // Error sending heartbeat, will be cleaned up by read loop
                            }
                        }, ct);
                    }
                }

                // 2. Student timeout check
                foreach (var student in _studentsById.Values)
                {
                    if (student.Status == StudentHubStatus.Disconnected)
                    {
                        continue;
                    }

                    var elapsedMs = (now - student.LastSeen).TotalMilliseconds;
                    if (elapsedMs >= ProtocolConstants.ReconnectWindowMs)
                    {
                        var updated = student with { Status = StudentHubStatus.Disconnected };
                        _studentsById[student.Id] = updated;
                        StudentStatusChanged?.Invoke(updated);
                        StudentLeft?.Invoke(updated);
                    }
                    else if (elapsedMs >= ProtocolConstants.StaleStreamMs && student.Status == StudentHubStatus.Online)
                    {
                        if (!_activeConnections.ContainsKey(student.Id))
                        {
                            var updated = student with { Status = StudentHubStatus.Reconnecting };
                            _studentsById[student.Id] = updated;
                            StudentStatusChanged?.Invoke(updated);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    private static void CloseClient(TcpClient client)
    {
        try
        {
            client.Close();
            client.Dispose();
        }
        catch { }
    }

    private static string GetLocalIpAddress(IPAddress remoteAddress)
    {
        if (IPAddress.IsLoopback(remoteAddress))
        {
            return "127.0.0.1";
        }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect(remoteAddress, 1);
            if (socket.LocalEndPoint is IPEndPoint endPoint && endPoint.Address.AddressFamily == AddressFamily.InterNetwork)
            {
                return endPoint.Address.ToString();
            }
        }
        catch
        {
        }

        try
        {
            foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                var ipProps = netInterface.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                    {
                        return addr.Address.ToString();
                    }
                }
            }
        }
        catch
        {
        }

        return "127.0.0.1";
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            await StopClassAsync();
        }
    }
}
