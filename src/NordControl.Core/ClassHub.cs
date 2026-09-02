using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core.Helpers;
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

public record ClassGroup(string Id, string Name);

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
    private readonly ConcurrentDictionary<string, ClassGroup> _groups = new();
    private readonly Dictionary<string, HashSet<string>> _groupIdsByStudentId = new(StringComparer.Ordinal);
    private readonly object _membershipLock = new();

    private Task? _udpLoopTask;
    private Task? _tcpAcceptLoopTask;
    private Task? _maintenanceLoopTask;

    private int _heartbeatSeq;
    private bool _isDisposed;
    private string? _selectedStudentId;
    private volatile string? _desiredStudentId;
    private readonly SemaphoreSlim _selectLock = new(1, 1);

    public int TcpPort => _tcpPort;
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public string? SelectedStudentId => _selectedStudentId;

    public IReadOnlyCollection<ConnectedStudent> Students => _studentsById.Values.ToList();
    public IReadOnlyList<ClassGroup> Groups => _groups.Values.ToList();

    public string? CreateGroup(string name)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var id = Guid.NewGuid().ToString();
        _groups[id] = new ClassGroup(id, name.Trim());
        return id;
    }

    public bool RenameGroup(string id, string name)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!_groups.TryGetValue(id, out var existing))
        {
            return false;
        }

        _groups[id] = existing with { Name = name.Trim() };
        return true;
    }

    public bool DisbandGroup(string id)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(id) || !_groups.TryRemove(id, out _))
        {
            return false;
        }

        lock (_membershipLock)
        {
            var empty = new List<string>();
            foreach (var kv in _groupIdsByStudentId)
            {
                kv.Value.Remove(id);
                if (kv.Value.Count == 0)
                {
                    empty.Add(kv.Key);
                }
            }

            foreach (var studentId in empty)
            {
                _groupIdsByStudentId.Remove(studentId);
            }
        }

        return true;
    }

    public bool AddStudentToGroup(string studentId, string groupId)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(groupId))
        {
            return false;
        }

        lock (_membershipLock)
        {
            if (!_groups.ContainsKey(groupId))
            {
                return false;
            }

            if (!_groupIdsByStudentId.TryGetValue(studentId, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _groupIdsByStudentId[studentId] = set;
            }

            set.Add(groupId);
        }

        return true;
    }

    public bool RemoveStudentFromGroup(string studentId, string groupId)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(groupId))
        {
            return false;
        }

        lock (_membershipLock)
        {
            if (!_groupIdsByStudentId.TryGetValue(studentId, out var set))
            {
                return false;
            }

            var removed = set.Remove(groupId);
            if (set.Count == 0)
            {
                _groupIdsByStudentId.Remove(studentId);
            }

            return removed;
        }
    }

    public bool SetStudentGroup(string studentId, string? groupId)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(studentId))
        {
            return false;
        }

        if (groupId == null)
        {
            RemoveAllGroupsForStudent(studentId);
            return true;
        }

        return AddStudentToGroup(studentId, groupId);
    }

    public string? GetStudentGroupId(string studentId)
    {
        var ids = GetStudentGroupIds(studentId);
        return ids.Count == 0 ? null : ids[0];
    }

    public IReadOnlyList<string> GetStudentGroupIds(string studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return [];
        }

        lock (_membershipLock)
        {
            if (_groupIdsByStudentId.TryGetValue(studentId, out var set) && set.Count > 0)
            {
                return set.ToList();
            }
        }

        return [];
    }

    public IReadOnlyList<string> GetOnlineStudentIdsInGroup(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId) || !_groups.ContainsKey(groupId))
        {
            return [];
        }

        List<string> candidates;
        lock (_membershipLock)
        {
            candidates = _groupIdsByStudentId
                .Where(kv => kv.Value.Contains(groupId))
                .Select(kv => kv.Key)
                .ToList();
        }

        var ids = new List<string>();
        foreach (var studentId in candidates)
        {
            if (_studentsById.TryGetValue(studentId, out var student) && student.Status == StudentHubStatus.Online)
            {
                ids.Add(studentId);
            }
        }

        return ids;
    }

    private void RemoveAllGroupsForStudent(string studentId)
    {
        lock (_membershipLock)
        {
            _groupIdsByStudentId.Remove(studentId);
        }
    }

    private void ClearGroups()
    {
        _groups.Clear();
        lock (_membershipLock)
        {
            _groupIdsByStudentId.Clear();
        }
    }

    public event Action<ConnectedStudent>? StudentJoined;
    public event Action<ConnectedStudent>? StudentStatusChanged;
    public event Action<ConnectedStudent>? StudentLeft;
    public event Action<string, JpegFrame>? ScreenFrameReceived;
    public event Action<string, JpegFrame>? PreviewFrameReceived;
    public event Action<string, WireMessage>? ProcessListReceived;
    public event Action<string, IReadOnlyList<InstalledAppInfo>>? InstalledHintsReceived;

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

        public async Task<bool> TrySendMessageAsync(WireMessage msg, int timeoutMs = 500, CancellationToken ct = default)
        {
            try
            {
                if (await SendLock.WaitAsync(timeoutMs, ct))
                {
                    try
                    {
                        await FrameCodec.WriteJsonMessageAsync(Stream, msg, ct);
                        return true;
                    }
                    finally
                    {
                        SendLock.Release();
                    }
                }
            }
            catch
            {
            }

            return false;
        }
    }

    public ClassHub(int udpPort = ProtocolConstants.UdpPort, int tcpPort = ProtocolConstants.TcpPort, IClock? clock = null)
    {
        _udpPort = udpPort;
        _tcpPort = tcpPort;
        _clock = clock ?? new SystemClock();
    }

    public async Task SelectStudentAsync(string? studentId)
    {
        _desiredStudentId = studentId;
        await _selectLock.WaitAsync();
        try
        {
            while (true)
            {
                var target = _desiredStudentId;
                var previousId = _selectedStudentId;
                if (string.Equals(previousId, target, StringComparison.Ordinal))
                {
                    return;
                }

                _selectedStudentId = target;

                if (previousId != null && _activeConnections.TryGetValue(previousId, out var prevConn))
                {
                    await prevConn.TrySendMessageAsync(new WireMessage { Type = "stream_stop" });
                }

                if (!string.Equals(_desiredStudentId, target, StringComparison.Ordinal))
                {
                    continue;
                }

                if (target != null && _activeConnections.TryGetValue(target, out var newConn))
                {
                    await newConn.TrySendMessageAsync(new WireMessage { Type = "stream_start" });
                }

                return;
            }
        }
        finally
        {
            _selectLock.Release();
        }
    }

    public Task<bool> SendLaunchAppAsync(string studentId, string exe, string? launchTarget = null) =>
        TrySendToStudentAsync(studentId, new WireMessage
        {
            Type = "launch_app",
            Exe = exe,
            LaunchTarget = launchTarget
        });

    public Task<int> BroadcastLaunchAppAsync(string exe, string? launchTarget = null) =>
        SendToEachAsync(GetOnlineStudentIds(), id => SendLaunchAppAsync(id, exe, launchTarget));

    public async Task<bool> SendLaunchAppAfterBlockListAsync(
        string studentId,
        IReadOnlyList<string> exeNames,
        string exe,
        string? launchTarget = null)
    {
        if (!await SendBlockListAsync(studentId, exeNames))
        {
            return false;
        }

        return await SendLaunchAppAsync(studentId, exe, launchTarget);
    }

    public Task<int> BroadcastLaunchAppAfterBlockListAsync(
        IReadOnlyList<string> exeNames,
        string exe,
        string? launchTarget = null) =>
        SendToEachAsync(GetOnlineStudentIds(), id => SendLaunchAppAfterBlockListAsync(id, exeNames, exe, launchTarget));

    public Task<bool> SendBlockListAsync(string studentId, IReadOnlyList<string> exeNames) =>
        TrySendToStudentAsync(studentId, new WireMessage
        {
            Type = "set_block_list",
            ExeNames = exeNames != null ? exeNames.ToList() : []
        });

    public Task<int> BroadcastBlockListAsync(IReadOnlyList<string> exeNames) =>
        SendToEachAsync(GetOnlineStudentIds(), id => SendBlockListAsync(id, exeNames));

    public Task<int> BroadcastPreviewEnableAsync() =>
        SendToEachAsync(GetOnlineStudentIds(), id => SendPreviewControlAsync(id, "preview_enable"));

    public Task<int> BroadcastPreviewDisableAsync() =>
        SendToEachAsync(GetOnlineStudentIds(), id => SendPreviewControlAsync(id, "preview_disable"));

    private Task<bool> SendPreviewControlAsync(string studentId, string type) =>
        TrySendToStudentAsync(studentId, new WireMessage { Type = type });

    public Task<int> SendLaunchAppToGroupAsync(string groupId, string exe, string? launchTarget = null) =>
        SendToEachAsync(GetOnlineStudentIdsInGroup(groupId), id => SendLaunchAppAsync(id, exe, launchTarget));

    public Task<int> SendBlockListToGroupAsync(string groupId, IReadOnlyList<string> exeNames) =>
        SendToEachAsync(GetOnlineStudentIdsInGroup(groupId), id => SendBlockListAsync(id, exeNames));

    public Task<int> SendLaunchAppAfterBlockListToGroupAsync(
        string groupId,
        IReadOnlyList<string> exeNames,
        string exe,
        string? launchTarget = null) =>
        SendToEachAsync(
            GetOnlineStudentIdsInGroup(groupId),
            id => SendLaunchAppAfterBlockListAsync(id, exeNames, exe, launchTarget));

    public async Task<bool> SendTeacherMessageAsync(string studentId, string text)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(studentId))
        {
            return false;
        }

        var clipped = ClipTeacherMessage(text);
        return await TrySendToStudentAsync(studentId, new WireMessage
        {
            Type = "teacher_message",
            MessageId = Guid.NewGuid().ToString(),
            Message = clipped
        });
    }

    public Task<int> BroadcastTeacherMessageAsync(string text)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(0);
        }

        return SendToEachAsync(GetOnlineStudentIds(), id => SendTeacherMessageAsync(id, text));
    }

    public Task<int> SendTeacherMessageToGroupAsync(string groupId, string text)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(0);
        }

        return SendToEachAsync(GetOnlineStudentIdsInGroup(groupId), id => SendTeacherMessageAsync(id, text));
    }

    private static string ClipTeacherMessage(string text)
    {
        if (text.Length <= ProtocolConstants.MaxTeacherMessageChars)
        {
            return text;
        }

        return text.Substring(0, ProtocolConstants.MaxTeacherMessageChars);
    }

    private async Task<bool> TrySendToStudentAsync(string studentId, WireMessage msg)
    {
        if (string.IsNullOrWhiteSpace(studentId) || !_activeConnections.TryGetValue(studentId, out var conn))
        {
            return false;
        }

        return await conn.TrySendMessageAsync(msg);
    }

    private async Task<int> SendToEachAsync(IEnumerable<string> studentIds, Func<string, Task<bool>> send)
    {
        var successCount = 0;
        foreach (var id in studentIds)
        {
            if (await send(id))
            {
                successCount++;
            }
        }

        return successCount;
    }

    private List<string> GetOnlineStudentIds()
    {
        return _studentsById.Values
            .Where(s => s.Status == StudentHubStatus.Online)
            .Select(s => s.Id)
            .ToList();
    }

    public Task StartClass(string className, string pin, CancellationToken ct = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Класс уже запущен");
        }

        _className = className;
        _pin = PinCode.Normalize(pin);
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

    public async Task<bool> KickStudentAsync(string studentId)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(studentId) || !_studentsById.ContainsKey(studentId))
        {
            return false;
        }

        if (string.Equals(_selectedStudentId, studentId, StringComparison.Ordinal)
            || string.Equals(_desiredStudentId, studentId, StringComparison.Ordinal))
        {
            await SelectStudentAsync(null);
        }

        if (_activeConnections.TryGetValue(studentId, out var conn))
        {
            await conn.TrySendMessageAsync(new WireMessage
            {
                Type = "set_block_list",
                ExeNames = []
            });
            await conn.TrySendMessageAsync(new WireMessage
            {
                Type = "session_end",
                Reason = "kicked"
            });

            try
            {
                if (conn.Client.Connected)
                {
                    conn.Client.Client?.Shutdown(SocketShutdown.Send);
                }
            }
            catch
            {
            }

            await Task.Delay(50);
            CloseClient(conn.Client);
            _activeConnections.TryRemove(studentId, out _);
        }

        RemoveAllGroupsForStudent(studentId);

        if (_studentsById.TryGetValue(studentId, out var student)
            && student.Status != StudentHubStatus.Disconnected)
        {
            var updated = student with { Status = StudentHubStatus.Disconnected };
            _studentsById[studentId] = updated;
            StudentStatusChanged?.Invoke(updated);
            StudentLeft?.Invoke(updated);
        }

        return true;
    }

    public async Task StopClassAsync()
    {
        if (_cts == null || _cts.IsCancellationRequested)
        {
            return;
        }

        var activeConns = _activeConnections.Values.ToList();

        // 1. First, send empty set_block_list to release all student RAM restrictions
        var emptyBlockMsg = new WireMessage
        {
            Type = "set_block_list",
            ExeNames = []
        };

        foreach (var conn in activeConns)
        {
            await conn.TrySendMessageAsync(emptyBlockMsg);
        }

        CleanupListeners();

        var sessionEnd = new WireMessage
        {
            Type = "session_end",
            Reason = "class_ended"
        };

        foreach (var conn in activeConns)
        {
            try
            {
                if (await conn.TrySendMessageAsync(sessionEnd))
                {
                    if (conn.Client.Connected)
                    {
                        conn.Client.Client?.Shutdown(SocketShutdown.Send);
                    }
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
            CloseClient(conn.Client);
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

        ClearGroups();
        _selectedStudentId = null;

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
                    var localIp = LanEndpoints.GetAnnounceIpv4(result.RemoteEndPoint.Address);
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
                    Type = "join_reject",
                    Reason = "version",
                    Message = "Несовместимая версия"
                };
                await FrameCodec.WriteJsonMessageAsync(stream, rejectVersion, ct);
                CloseClient(client);
                return;
            }

            if (!PinCode.Equals(joinMsg.Pin, _pin))
            {
                var rejectPin = new WireMessage
                {
                    Type = "join_reject",
                    Reason = "bad_pin",
                    Message = "Неверный PIN"
                };
                await FrameCodec.WriteJsonMessageAsync(stream, rejectPin, ct);
                CloseClient(client);
                return;
            }

            string studentId;
            string sessionToken;
            bool isReconnect = false;

            if (!string.IsNullOrEmpty(joinMsg.SessionToken) && _studentIdByToken.TryGetValue(joinMsg.SessionToken, out var existingId))
            {
                studentId = existingId;
                sessionToken = joinMsg.SessionToken!;
                isReconnect = true;
            }
            else if (TryFindStudentByHostname(joinMsg.Hostname, out var byHost))
            {
                studentId = byHost.Id;
                sessionToken = byHost.SessionToken ?? Guid.NewGuid().ToString();
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
                CloseClient(oldConn.Client);
            }

            var joinOk = new WireMessage
            {
                Type = "join_ok",
                StudentId = studentId,
                SessionToken = sessionToken,
                HeartbeatIntervalMs = ProtocolConstants.HeartbeatIntervalMs,
                ReconnectWindowMs = ProtocolConstants.ReconnectWindowMs
            };

            await FrameCodec.WriteJsonMessageAsync(stream, joinOk, ct);
            _activeConnections[studentId] = connection;

            // If this student is currently selected, send stream_start
            if (_selectedStudentId == studentId)
            {
                await connection.TrySendMessageAsync(new WireMessage { Type = "stream_start" }, 5000, ct);
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

                if (frame.Value.Type == ProtocolConstants.JpegMessageType)
                {
                    if (_selectedStudentId == studentId)
                    {
                        var jpeg = JpegFrame.Decode(frame.Value.Payload);
                        if (jpeg != null)
                        {
                            ScreenFrameReceived?.Invoke(studentId, jpeg.Value);
                        }
                    }
                }
                else if (frame.Value.Type == ProtocolConstants.JpegPreviewMessageType)
                {
                    var jpeg = JpegFrame.Decode(frame.Value.Payload);
                    if (jpeg != null)
                    {
                        PreviewFrameReceived?.Invoke(studentId, jpeg.Value);
                    }
                }
                else if (frame.Value.Type == ProtocolConstants.JsonMessageType)
                {
                    var wireMsg = WireMessage.Deserialize(frame.Value.Payload);
                    if (wireMsg != null)
                    {
                        if (wireMsg.Type == "process_list")
                        {
                            if (_selectedStudentId == studentId)
                            {
                                ProcessListReceived?.Invoke(studentId, wireMsg);
                            }
                        }
                        else if (wireMsg.Type == "installed_hints")
                        {
                            InstalledHintsReceived?.Invoke(studentId, wireMsg.Apps ?? (IReadOnlyList<InstalledAppInfo>)[]);
                        }
                    }
                }
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

                    if (!ct.IsCancellationRequested && _studentsById.TryGetValue(assignedStudentId, out var currentStudent)
                        && currentStudent.Status == StudentHubStatus.Online)
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
                        Type = "heartbeat",
                        Seq = seq
                    };

                    var activeConns = _activeConnections.Values.ToList();
                    foreach (var conn in activeConns)
                    {
                        _ = conn.TrySendMessageAsync(heartbeatMsg, timeoutMs: 1000, ct);
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
                        RemoveAllGroupsForStudent(student.Id);
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

    private bool TryFindStudentByHostname(string? hostname, out ConnectedStudent student)
    {
        student = null!;
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return false;
        }

        foreach (var existing in _studentsById.Values)
        {
            if (string.Equals(existing.Hostname, hostname, StringComparison.OrdinalIgnoreCase)
                && existing.Status != StudentHubStatus.Online)
            {
                student = existing;
                return true;
            }
        }

        return false;
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

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            await StopClassAsync();
        }
    }
}
