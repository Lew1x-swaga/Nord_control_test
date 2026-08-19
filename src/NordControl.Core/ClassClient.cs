using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Core.Helpers;
using NordControl.Core.Policies;
using NordControl.Protocol;

namespace NordControl.Core;

public class ClassClient : IAsyncDisposable, IDisposable
{
    private readonly int _udpPort;
    private readonly int _tcpPort;
    private readonly IClock _clock;
    private readonly StudentSession _session;
    private readonly IAppBlocker _appBlocker;
    private readonly IAppLauncher _appLauncher;
    private readonly InstalledAppsScanner _installedAppsScanner = new();

    private string _pin;
    private string? _manualTeacherIp;
    private string? _displayName;

    private CancellationTokenSource? _cts;
    private int _heartbeatSeq;
    private bool _isDisposed;

    private string? _lastTeacherIp;
    private int? _lastTeacherTcpPort;
    private bool _pauseDiscoveryAfterClassEnd;

    public string? LastTeacherIp => _lastTeacherIp;
    public int? LastTeacherTcpPort => _lastTeacherTcpPort;

    public int UdpPort => _udpPort;
    public int TcpPort => _tcpPort;
    public string Pin => _pin;
    public string? ManualTeacherIp => _manualTeacherIp;
    public string? DisplayName => _displayName;
    public StudentSession Session => _session;
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    public IAppBlocker AppBlocker => _appBlocker;
    public IAppLauncher AppLauncher => _appLauncher;
    public Func<IReadOnlyList<InstalledAppInfo>>? InstalledAppsProvider { get; set; }

    public Func<CancellationToken, Task<JpegFrame?>>? CaptureFrameCallback { get; set; }
    public Func<WireMessage>? ProcessListCallback { get; set; }

    public event Action<StudentSession>? StatusChanged;
    public event Action<bool>? StreamStateChanged;
    public event Action<string>? Error;

    public ClassClient(
        string pin,
        int udpPort = ProtocolConstants.UdpPort,
        int tcpPort = ProtocolConstants.TcpPort,
        string? manualTeacherIp = null,
        string? displayName = null,
        IClock? clock = null,
        IAppBlocker? appBlocker = null,
        IAppLauncher? appLauncher = null,
        Func<IReadOnlyList<InstalledAppInfo>>? installedAppsProvider = null)
    {
        _pin = PinCode.Normalize(pin);
        _udpPort = udpPort;
        _tcpPort = tcpPort;
        _manualTeacherIp = string.IsNullOrWhiteSpace(manualTeacherIp) ? null : manualTeacherIp.Trim();
        _displayName = displayName;
        _clock = clock ?? new SystemClock();
        _appBlocker = appBlocker ?? new RamAppBlocker();
        _appLauncher = appLauncher ?? new AppLauncher();
        InstalledAppsProvider = installedAppsProvider;

        _session = new StudentSession();
        _session.StatusChanged += (oldStatus, newStatus) =>
        {
            if (newStatus == SessionStatus.Ended || newStatus == SessionStatus.Idle)
            {
                _appBlocker.Clear();
            }
            StatusChanged?.Invoke(_session);
        };
        _session.StreamStateChanged += isStreaming => StreamStateChanged?.Invoke(isStreaming);

        try
        {
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        }
        catch
        {
            // NetworkChange might not be supported in some restricted environments
        }
    }

    public void SetPin(string pin)
    {
        _pin = PinCode.Normalize(pin);
    }

    public void SetManualTeacherIp(string? manualTeacherIp)
    {
        _manualTeacherIp = string.IsNullOrWhiteSpace(manualTeacherIp) ? null : manualTeacherIp.Trim();
    }

    public void SetDisplayName(string? displayName)
    {
        _displayName = displayName;
    }

    public void RequestStop()
    {
        _cts?.Cancel();
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_session.Status == SessionStatus.Idle)
                {
                    if (_pauseDiscoveryAfterClassEnd)
                    {
                        try
                        {
                            await Task.Delay(Timeout.Infinite, token);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }

                        continue;
                    }

                    if (!string.IsNullOrEmpty(_manualTeacherIp) && !LanEndpoints.IsClassroomIpv4(_manualTeacherIp))
                    {
                        Error?.Invoke("IP учителя должен быть из локальной сети");
                        try
                        {
                            await Task.Delay(Timeout.Infinite, token);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            break;
                        }

                        continue;
                    }

                    var (ip, port) = await DiscoverTeacherAsync(token);
                    if (string.IsNullOrEmpty(ip))
                    {
                        await Task.Delay(500, token);
                        continue;
                    }

                    _lastTeacherIp = ip;
                    _lastTeacherTcpPort = port;

                    await ConnectAndProcessSessionAsync(ip, port, token);
                }
                else if (_session.Status == SessionStatus.Reconnecting)
                {
                    var targetIp = _lastTeacherIp ?? _manualTeacherIp;
                    var targetPort = _lastTeacherTcpPort ?? _tcpPort;

                    if (!string.IsNullOrEmpty(targetIp))
                    {
                        var reconnected = await TryReconnectAsync(targetIp, targetPort, token);
                        if (!reconnected)
                        {
                            var (discoveredIp, discoveredPort) = await DiscoverTeacherAsync(token, timeoutMs: 2000);
                            if (!string.IsNullOrEmpty(discoveredIp))
                            {
                                _lastTeacherIp = discoveredIp;
                                _lastTeacherTcpPort = discoveredPort;
                                await TryReconnectAsync(discoveredIp, discoveredPort, token);
                            }
                        }
                    }
                    else
                    {
                        var (discoveredIp, discoveredPort) = await DiscoverTeacherAsync(token, timeoutMs: 2000);
                        if (!string.IsNullOrEmpty(discoveredIp))
                        {
                            _lastTeacherIp = discoveredIp;
                            _lastTeacherTcpPort = discoveredPort;
                            await TryReconnectAsync(discoveredIp, discoveredPort, token);
                        }
                    }

                    if (_session.Status == SessionStatus.Reconnecting)
                    {
                        _session.Tick(_clock.UtcNow);
                        if (_session.Status == SessionStatus.Ended)
                        {
                            _session.ResetToIdle();
                        }
                        else
                        {
                            await Task.Delay(1000, token);
                        }
                    }
                }
                else if (_session.Status == SessionStatus.Ended)
                {
                    _session.ResetToIdle();
                }
                else
                {
                    await Task.Delay(500, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex.Message);
                await Task.Delay(1000, token);
            }
        }
    }

    private async Task<(string? ip, int port)> DiscoverTeacherAsync(CancellationToken ct, int timeoutMs = 2000)
    {
        if (!string.IsNullOrEmpty(_manualTeacherIp))
        {
            if (!LanEndpoints.IsClassroomIpv4(_manualTeacherIp))
            {
                return (null, 0);
            }

            return (_manualTeacherIp, _tcpPort);
        }

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        try
        {
            udp.EnableBroadcast = true;
        }
        catch { }

        var probeBytes = Encoding.UTF8.GetBytes(UdpPackets.Probe());

        try
        {
            await SendUdpProbesAsync(udp, probeBytes);
            await SendUdpProbesAsync(udp, probeBytes);
        }
        catch { }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);
        using var reg = timeoutCts.Token.Register(() =>
        {
            try { udp.Dispose(); } catch { }
        });

        try
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                var res = await udp.ReceiveAsync(timeoutCts.Token);
                var text = Encoding.UTF8.GetString(res.Buffer);

                if (UdpPackets.TryParseAnnounce(text, out var className, out var ip, out var tcpPort))
                {
                    var effectiveIp = ip;
                    if (effectiveIp == "0.0.0.0" || string.IsNullOrEmpty(effectiveIp))
                    {
                        effectiveIp = res.RemoteEndPoint.Address.ToString();
                    }

                    return (effectiveIp, tcpPort);
                }
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Timeout or socket disposed on timeout
        }

        return (null, 0);
    }

    private async Task SendUdpProbesAsync(UdpClient udp, byte[] probeBytes)
    {
        foreach (var dest in LanEndpoints.GetUdpProbeDestinations(_udpPort))
        {
            try
            {
                await udp.SendAsync(probeBytes, probeBytes.Length, dest);
            }
            catch
            {
            }
        }
    }

    private static async Task<WireMessage?> ReadJoinHandshakeAsync(NetworkStream stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var handshakeFrame = await FrameCodec.ReadAsync(stream, ct);
            if (handshakeFrame == null)
            {
                return null;
            }

            if (handshakeFrame.Value.Type != ProtocolConstants.JsonMessageType)
            {
                continue;
            }

            var reply = WireMessage.Deserialize(handshakeFrame.Value.Payload);
            if (reply == null)
            {
                continue;
            }

            if (reply.Type is "heartbeat" or "stream_start" or "stream_stop")
            {
                continue;
            }

            return reply;
        }

        return null;
    }

    private async Task<bool> TryReconnectAsync(string ip, int port, CancellationToken ct)
    {
        try
        {
            await ConnectAndProcessSessionAsync(ip, port, ct);
            return _session.Status == SessionStatus.Online;
        }
        catch
        {
            return false;
        }
    }

    private async Task ConnectAndProcessSessionAsync(string ip, int port, CancellationToken ct)
    {
        using var client = new TcpClient();
        client.NoDelay = true;

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(5000);

        try
        {
            await client.ConnectAsync(IPAddress.Parse(ip), port, connectCts.Token);
        }
        catch (Exception ex)
        {
            if (_session.Status == SessionStatus.Online)
            {
                _session.OnTcpDropped();
            }
            throw new IOException($"Не удалось подключиться к учителю ({ip}:{port}): {ex.Message}", ex);
        }

        using var stream = client.GetStream();
        using var sendLock = new SemaphoreSlim(1, 1);

        var joinMsg = new WireMessage
        {
            V = ProtocolConstants.Version,
            Type = "join_class",
            Pin = _pin,
            DisplayName = _displayName ?? Environment.MachineName,
            Hostname = Environment.MachineName,
            AgentVersion = ProtocolConstants.AgentVersion,
            SessionToken = _session.SessionToken
        };

        await FrameCodec.WriteJsonMessageAsync(stream, joinMsg, ct);

        var reply = await ReadJoinHandshakeAsync(stream, ct);
        if (reply == null)
        {
            if (_session.Status == SessionStatus.Online)
            {
                _session.OnTcpDropped();
            }
            return;
        }

        if (reply.Type == "join_reject")
        {
            var msg = reply.Message ?? (reply.Reason == "bad_pin" ? "Неверный PIN" : reply.Reason ?? "Отказ подключения");
            Error?.Invoke(msg);
            _session.ResetToIdle();
            return;
        }

        if (reply.Type != "join_ok")
        {
            throw new InvalidDataException($"Неожиданный ответ рукопожатия: {reply.Type}");
        }

        var studentId = reply.StudentId ?? Guid.NewGuid().ToString();
        var sessionToken = reply.SessionToken ?? Guid.NewGuid().ToString();

        _session.OnJoinOk(studentId, sessionToken, _clock.UtcNow);

        // Send installed_hints upon successful join
        try
        {
            var apps = InstalledAppsProvider?.Invoke() ?? _installedAppsScanner.ScanInstalledApps();
            if (apps != null && apps.Count > 0)
            {
                var hintsMsg = new WireMessage
                {
                    Type = "installed_hints",
                    Apps = apps.ToList()
                };
                await FrameCodec.WriteJsonMessageAsync(stream, hintsMsg, ct);
            }
        }
        catch
        {
            // Transient error scanning or sending hints
        }

        // Start active communication loops
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var heartbeatTask = Task.Run(() => HeartbeatLoopAsync(stream, sendLock, sessionCts.Token), sessionCts.Token);
        var tickTask = Task.Run(() => TickLoopAsync(sessionCts.Token), sessionCts.Token);
        var captureTask = Task.Run(() => ScreenCaptureLoopAsync(stream, sendLock, sessionCts.Token), sessionCts.Token);
        var processTask = Task.Run(() => ProcessListLoopAsync(stream, sendLock, sessionCts.Token), sessionCts.Token);

        try
        {
            while (!sessionCts.Token.IsCancellationRequested && _session.Status == SessionStatus.Online)
            {
                var frame = await FrameCodec.ReadAsync(stream, sessionCts.Token);
                if (frame == null)
                {
                    _session.OnTcpDropped();
                    break;
                }

                _session.OnMessageReceived(_clock.UtcNow);

                if (frame.Value.Type == ProtocolConstants.JsonMessageType)
                {
                    var wireMsg = WireMessage.Deserialize(frame.Value.Payload);
                    if (wireMsg != null)
                    {
                        if (wireMsg.Type == "session_end")
                        {
                            _pauseDiscoveryAfterClassEnd = true;
                            _appBlocker.Clear();
                            _session.OnSessionEnd();
                            _session.ResetToIdle();
                            RequestStop();
                            break;
                        }
                        else if (wireMsg.Type == "stream_start")
                        {
                            _session.StreamEnabled = true;
                        }
                        else if (wireMsg.Type == "stream_stop")
                        {
                            _session.StreamEnabled = false;
                        }
                        else if (wireMsg.Type == "launch_app")
                        {
                            _appLauncher.Launch(wireMsg.Exe ?? string.Empty, wireMsg.LaunchTarget);
                        }
                        else if (wireMsg.Type == "set_block_list")
                        {
                            _appBlocker.SetBlockList(wireMsg.ExeNames ?? (IEnumerable<string>)[]);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal cancellation
        }
        catch (Exception)
        {
            if (_session.Status == SessionStatus.Online)
            {
                _session.OnTcpDropped();
            }
        }
        finally
        {
            sessionCts.Cancel();
            try
            {
                await Task.WhenAll(heartbeatTask, tickTask, captureTask, processTask);
            }
            catch { }
        }
    }

    private async Task HeartbeatLoopAsync(NetworkStream stream, SemaphoreSlim sendLock, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ProtocolConstants.HeartbeatIntervalMs, ct);

                var hbMsg = new WireMessage
                {
                    Type = "heartbeat",
                    Seq = Interlocked.Increment(ref _heartbeatSeq)
                };

                await TrySendJsonMessageAsync(stream, sendLock, hbMsg, ct, 1000);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private async Task TickLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, ct);
                _session.Tick(_clock.UtcNow);

                if (_session.Status == SessionStatus.Ended)
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ScreenCaptureLoopAsync(NetworkStream stream, SemaphoreSlim sendLock, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_session.ShouldCapture || CaptureFrameCallback == null)
                {
                    await Task.Delay(100, ct);
                    continue;
                }

                var frame = await CaptureFrameCallback(ct);
                if (frame.HasValue && _session.ShouldCapture)
                {
                    var framePayload = frame.Value.Encode();
                    await sendLock.WaitAsync(ct);
                    try
                    {
                        await FrameCodec.WriteAsync(stream, ProtocolConstants.JpegMessageType, framePayload, ct);
                    }
                    finally
                    {
                        sendLock.Release();
                    }
                }

                // ~10 fps (100ms interval, 8-12 fps target)
                await Task.Delay(100, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient frame capture/send failures
            }
        }
    }

    private async Task ProcessListLoopAsync(NetworkStream stream, SemaphoreSlim sendLock, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_session.Status == SessionStatus.Online && ProcessListCallback != null)
                {
                    var procMsg = ProcessListCallback();
                    if (procMsg != null)
                    {
                        await TrySendJsonMessageAsync(stream, sendLock, procMsg, ct, 500);
                    }
                }

                await Task.Delay(2500, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient process list send failures
            }
        }
    }

    private static async Task<bool> TrySendJsonMessageAsync(NetworkStream stream, SemaphoreSlim sendLock, WireMessage msg, CancellationToken ct, int timeoutMs = 500)
    {
        try
        {
            if (await sendLock.WaitAsync(timeoutMs, ct))
            {
                try
                {
                    await FrameCodec.WriteJsonMessageAsync(stream, msg, ct);
                    return true;
                }
                finally
                {
                    sendLock.Release();
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        ResetAddressCache();
    }

    public void ResetAddressCache()
    {
        _lastTeacherIp = null;
        _lastTeacherTcpPort = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        }
        catch
        {
        }

        _appBlocker.Clear();
        _cts?.Cancel();
        _cts?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
