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
    public SessionStatus Status { get; private set; } = SessionStatus.Idle;
    public string? StudentId { get; private set; }
    public string? SessionToken { get; private set; }
    public bool StreamEnabled { get; set; } = false;
    public DateTime LastRecv { get; private set; }
    public bool StreamPaused { get; private set; } = false;

    public bool ShouldHoldPolicies => Status == SessionStatus.Online || Status == SessionStatus.Reconnecting;
    public bool ShouldCapture => Status == SessionStatus.Online && StreamEnabled && !StreamPaused;

    public event Action<SessionStatus, SessionStatus>? StatusChanged;

    public void OnJoinOk(string studentId, string token, DateTime now)
    {
        StudentId = studentId;
        SessionToken = token;
        LastRecv = now;
        StreamPaused = false;
        SetStatus(SessionStatus.Online);
    }

    public void OnMessageReceived(DateTime now)
    {
        LastRecv = now;
        StreamPaused = false;
        if (Status == SessionStatus.Reconnecting)
        {
            SetStatus(SessionStatus.Online);
        }
    }

    public void OnTcpDropped()
    {
        if (Status == SessionStatus.Online)
        {
            SetStatus(SessionStatus.Reconnecting);
        }
    }

    public void OnSessionEnd()
    {
        StreamPaused = false;
        SetStatus(SessionStatus.Ended);
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
    }

    public void ResetToIdle()
    {
        StudentId = null;
        SessionToken = null;
        StreamEnabled = false;
        StreamPaused = false;
        SetStatus(SessionStatus.Idle);
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
