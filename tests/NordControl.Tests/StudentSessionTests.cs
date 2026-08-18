using System;
using NordControl.Core;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan span)
    {
        UtcNow += span;
    }
}

public class StudentSessionTests
{
    [Fact]
    public void Scenario1_JoinOk_leads_to_Online_and_ShouldHoldPolicies_true()
    {
        var clock = new FakeClock();
        var session = new StudentSession();

        Assert.Equal(SessionStatus.Idle, session.Status);
        Assert.False(session.ShouldHoldPolicies);
        Assert.False(session.StreamPaused);
        Assert.False(session.ShouldCapture);

        session.OnJoinOk("student-1", "token-1", clock.UtcNow);

        Assert.Equal(SessionStatus.Online, session.Status);
        Assert.Equal("student-1", session.StudentId);
        Assert.Equal("token-1", session.SessionToken);
        Assert.True(session.ShouldHoldPolicies);
        Assert.False(session.StreamPaused);
        Assert.False(session.ShouldCapture); // StreamEnabled is false by default

        session.StreamEnabled = true;
        Assert.True(session.ShouldCapture);
    }

    [Fact]
    public void Scenario2_10s_without_messages_pauses_stream_but_remains_Online()
    {
        var clock = new FakeClock();
        var session = new StudentSession();
        session.OnJoinOk("student-1", "token-1", clock.UtcNow);
        session.StreamEnabled = true;

        clock.Advance(TimeSpan.FromMilliseconds(ProtocolConstants.StaleStreamMs)); // 10s
        session.Tick(clock.UtcNow);

        Assert.Equal(SessionStatus.Online, session.Status);
        Assert.True(session.ShouldHoldPolicies);
        Assert.True(session.StreamPaused);
        Assert.False(session.ShouldCapture);
    }

    [Fact]
    public void Scenario3_120s_without_messages_ends_session_and_drops_policies()
    {
        var clock = new FakeClock();
        var session = new StudentSession();
        session.OnJoinOk("student-1", "token-1", clock.UtcNow);
        session.StreamEnabled = true;

        clock.Advance(TimeSpan.FromMilliseconds(ProtocolConstants.ReconnectWindowMs)); // 120s
        session.Tick(clock.UtcNow);

        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.False(session.ShouldHoldPolicies);
        Assert.False(session.ShouldCapture);
    }

    [Fact]
    public void Scenario4_JoinOk_then_OnSessionEnd_ends_session_immediately()
    {
        var clock = new FakeClock();
        var session = new StudentSession();
        session.OnJoinOk("student-1", "token-1", clock.UtcNow);
        session.StreamEnabled = true;

        session.OnSessionEnd();

        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.False(session.ShouldHoldPolicies);
        Assert.False(session.ShouldCapture);
    }

    [Fact]
    public void Scenario5_JoinOk_then_OnTcpDropped_sets_Reconnecting_then_OnMessageReceived_restores_Online()
    {
        var clock = new FakeClock();
        var session = new StudentSession();
        session.OnJoinOk("student-1", "token-1", clock.UtcNow);

        session.OnTcpDropped();

        Assert.Equal(SessionStatus.Reconnecting, session.Status);
        Assert.True(session.ShouldHoldPolicies);

        clock.Advance(TimeSpan.FromSeconds(2));
        session.OnMessageReceived(clock.UtcNow);

        Assert.Equal(SessionStatus.Online, session.Status);
        Assert.True(session.ShouldHoldPolicies);
    }

    [Fact]
    public void Scenario6_OnSessionEnd_from_Reconnecting_ends_session()
    {
        var clock = new FakeClock();
        var session = new StudentSession();
        session.OnJoinOk("student-1", "token-1", clock.UtcNow);
        session.OnTcpDropped();
        Assert.Equal(SessionStatus.Reconnecting, session.Status);

        session.OnSessionEnd();

        Assert.Equal(SessionStatus.Ended, session.Status);
        Assert.False(session.ShouldHoldPolicies);
    }

    [Fact]
    public void Scenario7_Ended_then_ResetToIdle_resets_session()
    {
        var clock = new FakeClock();
        var session = new StudentSession();
        session.OnJoinOk("student-1", "token-1", clock.UtcNow);
        session.OnSessionEnd();
        Assert.Equal(SessionStatus.Ended, session.Status);

        session.ResetToIdle();

        Assert.Equal(SessionStatus.Idle, session.Status);
        Assert.Null(session.StudentId);
        Assert.Null(session.SessionToken);
        Assert.False(session.ShouldHoldPolicies);
        Assert.False(session.StreamPaused);
        Assert.False(session.ShouldCapture);
    }

    [Fact]
    public void OnTcpDropped_when_Ended_or_Idle_is_noop()
    {
        var session = new StudentSession();
        Assert.Equal(SessionStatus.Idle, session.Status);

        session.OnTcpDropped();
        Assert.Equal(SessionStatus.Idle, session.Status);

        session.OnSessionEnd();
        Assert.Equal(SessionStatus.Ended, session.Status);

        session.OnTcpDropped();
        Assert.Equal(SessionStatus.Ended, session.Status);
    }

    [Fact]
    public void StreamStateChanged_fires_when_streaming_starts_and_stops()
    {
        var clock = new FakeClock();
        var session = new StudentSession();
        var streamEvents = new System.Collections.Generic.List<bool>();
        session.StreamStateChanged += isStreaming => streamEvents.Add(isStreaming);

        session.OnJoinOk("student-1", "token-1", clock.UtcNow);
        Assert.Empty(streamEvents);

        // Teacher starts viewing screen
        session.StreamEnabled = true;
        Assert.Single(streamEvents);
        Assert.True(streamEvents[0]);

        // Teacher switches away / stops viewing
        session.StreamEnabled = false;
        Assert.Equal(2, streamEvents.Count);
        Assert.False(streamEvents[1]);
    }

    [Fact]
    public void StreamStateChanged_fires_false_when_session_drops_or_ends()
    {
        var clock = new FakeClock();
        var session = new StudentSession();
        var streamEvents = new System.Collections.Generic.List<bool>();
        session.StreamStateChanged += isStreaming => streamEvents.Add(isStreaming);

        session.OnJoinOk("student-1", "token-1", clock.UtcNow);
        session.StreamEnabled = true;
        Assert.Single(streamEvents);
        Assert.True(streamEvents[0]);

        // Drop TCP connection
        session.OnTcpDropped();
        Assert.Equal(2, streamEvents.Count);
        Assert.False(streamEvents[1]);

        // Reconnect restores stream if enabled
        session.OnMessageReceived(clock.UtcNow);
        Assert.Equal(3, streamEvents.Count);
        Assert.True(streamEvents[2]);

        // End session
        session.OnSessionEnd();
        Assert.Equal(4, streamEvents.Count);
        Assert.False(streamEvents[3]);
    }
}
