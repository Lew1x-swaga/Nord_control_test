using System;
using NordControl.Protocol;

namespace NordControl.Core;

public enum SessionStatus
{
    Idle,
    Online,
    Reconnecting,
    Ended
}

public class StudentSession
{
    private bool _streamEnabled;
    private bool _streamPaused;
    private bool _previewEnabled;
    private bool _lastShouldCapture;

    public SessionStatus Status { get; private set; } = SessionStatus.Idle;
    public string? StudentId { get; private set; }
    public string? SessionToken { get; private set; }
    public DateTime LastRecv { get; private set; }

    public bool StreamEnabled
    {
        get => _streamEnabled;
        set
        {
            if (_streamEnabled != value)
            {
                _streamEnabled = value;
                CheckAndNotifyStreamState();
            }
        }
    }

    public bool StreamPaused
    {
        get => _streamPaused;
        private set
        {
            if (_streamPaused != value)
            {
                _streamPaused = value;
                CheckAndNotifyStreamState();
            }
        }
    }

    public bool PreviewEnabled
    {
        get => _previewEnabled;
        set => _previewEnabled = value;
    }

    public bool ShouldHoldPolicies => Status == SessionStatus.Online || Status == SessionStatus.Reconnecting;
    public bool ShouldCapture => Status == SessionStatus.Online && StreamEnabled && !StreamPaused;
    public bool ShouldPreview => Status == SessionStatus.Online && PreviewEnabled && !StreamPaused;

    public event Action<SessionStatus, SessionStatus>? StatusChanged;
    public event Action<bool>? StreamStateChanged;

    private void CheckAndNotifyStreamState()
    {
        var current = ShouldCapture;
        if (_lastShouldCapture != current)
        {
            _lastShouldCapture = current;
            StreamStateChanged?.Invoke(current);
        }
    }

    public void OnJoinOk(string studentId, string token, DateTime now)
    {
        StudentId = studentId;
        SessionToken = token;
        LastRecv = now;
        StreamPaused = false;
        SetStatus(SessionStatus.Online);
        CheckAndNotifyStreamState();
    }

    public void OnMessageReceived(DateTime now)
    {
        LastRecv = now;
        StreamPaused = false;
        if (Status == SessionStatus.Reconnecting)
        {
            SetStatus(SessionStatus.Online);
        }
        CheckAndNotifyStreamState();
    }

    public void OnTcpDropped()
    {
        if (Status == SessionStatus.Online)
        {
            SetStatus(SessionStatus.Reconnecting);
        }
        CheckAndNotifyStreamState();
    }

    public void OnSessionEnd()
    {
        StreamPaused = false;
        SetStatus(SessionStatus.Ended);
        CheckAndNotifyStreamState();
    }

    public void Tick(DateTime now)
    {
        if (Status == SessionStatus.Online || Status == SessionStatus.Reconnecting)
        {
            var elapsedMs = (now - LastRecv).TotalMilliseconds;
            if (elapsedMs >= ProtocolConstants.ReconnectWindowMs)
            {
                StreamPaused = false;
                SetStatus(SessionStatus.Ended);
            }
            else if (elapsedMs >= ProtocolConstants.StaleStreamMs)
            {
                StreamPaused = true;
            }
            else
            {
                StreamPaused = false;
            }
        }
        CheckAndNotifyStreamState();
    }

    public void ResetToIdle()
    {
        StudentId = null;
        SessionToken = null;
        _streamEnabled = false;
        _streamPaused = false;
        _previewEnabled = false;
        SetStatus(SessionStatus.Idle);
        CheckAndNotifyStreamState();
    }

    private void SetStatus(SessionStatus newStatus)
    {
        if (Status != newStatus)
        {
            var oldStatus = Status;
            Status = newStatus;
            StatusChanged?.Invoke(oldStatus, newStatus);
        }
    }
}
