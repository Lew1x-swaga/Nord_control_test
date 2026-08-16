using System.Collections.Generic;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class ProcessListMessageTests
{
    [Fact]
    public void Serialize_and_deserialize_process_list_message()
    {
        var msg = new WireMessage
        {
            Type = "process_list",
            ActiveExe = "chrome.exe",
            Items = new List<ProcessItemInfo>
            {
                new() { Exe = "chrome.exe", Pid = 1234, Title = "Вкладка — Браузер" },
                new() { Exe = "notepad.exe", Pid = 5678, Title = "Безымянный — Блокнот" }
            }
        };

        var json = WireMessage.Serialize(msg);
        Assert.Contains("\"type\":\"process_list\"", json);
        Assert.Contains("\"active_exe\":\"chrome.exe\"", json);
        Assert.Contains("\"items\":", json);
        Assert.Contains("\"pid\":1234", json);
        Assert.Contains("\"title\":\"Вкладка — Браузер\"", json);

        var deserialized = WireMessage.Deserialize(json);
        Assert.NotNull(deserialized);
        Assert.Equal("process_list", deserialized!.Type);
        Assert.Equal("chrome.exe", deserialized.ActiveExe);
        Assert.NotNull(deserialized.Items);
        Assert.Equal(2, deserialized.Items!.Count);
        Assert.Equal("chrome.exe", deserialized.Items[0].Exe);
        Assert.Equal(1234, deserialized.Items[0].Pid);
        Assert.Equal("Вкладка — Браузер", deserialized.Items[0].Title);
    }

    [Fact]
    public void Serialize_and_deserialize_stream_start_stop()
    {
        var start = new WireMessage { Type = "stream_start" };
        var startJson = WireMessage.Serialize(start);
        Assert.Contains("\"type\":\"stream_start\"", startJson);
        var startDeser = WireMessage.Deserialize(startJson);
        Assert.NotNull(startDeser);
        Assert.Equal("stream_start", startDeser!.Type);

        var stop = new WireMessage { Type = "stream_stop" };
        var stopJson = WireMessage.Serialize(stop);
        Assert.Contains("\"type\":\"stream_stop\"", stopJson);
        var stopDeser = WireMessage.Deserialize(stopJson);
        Assert.NotNull(stopDeser);
        Assert.Equal("stream_stop", stopDeser!.Type);
    }
}
