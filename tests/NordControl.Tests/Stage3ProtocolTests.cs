using System.Collections.Generic;
using System.Text.Json;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class Stage3ProtocolTests
{
    [Fact]
    public void Serialize_and_deserialize_installed_hints_message()
    {
        var msg = new WireMessage
        {
            Type = "installed_hints",
            Apps = new List<InstalledAppInfo>
            {
                new()
                {
                    Name = "Google Chrome",
                    Exe = "chrome.exe",
                    LaunchTarget = @"C:\Program Files\Google\Chrome\Application\chrome.exe"
                },
                new()
                {
                    Name = "Яндекс Браузер",
                    Exe = "browser.exe",
                    LaunchTarget = null
                }
            }
        };

        var json = WireMessage.Serialize(msg);
        Assert.Contains("\"v\":1", json);
        Assert.Contains("\"type\":\"installed_hints\"", json);
        Assert.Contains("\"apps\":", json);
        Assert.Contains("\"name\":\"Google Chrome\"", json);
        Assert.Contains("\"exe\":\"chrome.exe\"", json);
        Assert.Contains("\"launch_target\":", json);
        Assert.Contains("\"name\":\"Яндекс Браузер\"", json);

        var deserialized = WireMessage.Deserialize(json);
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized!.V);
        Assert.Equal("installed_hints", deserialized.Type);
        Assert.NotNull(deserialized.Apps);
        Assert.Equal(2, deserialized.Apps!.Count);
        Assert.Equal("Google Chrome", deserialized.Apps[0].Name);
        Assert.Equal("chrome.exe", deserialized.Apps[0].Exe);
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe", deserialized.Apps[0].LaunchTarget);
        Assert.Equal("Яндекс Браузер", deserialized.Apps[1].Name);
        Assert.Equal("browser.exe", deserialized.Apps[1].Exe);
        Assert.Null(deserialized.Apps[1].LaunchTarget);

        // UTF-8 bytes roundtrip
        var utf8Bytes = WireMessage.SerializeUtf8(msg);
        var utf8Deser = WireMessage.Deserialize(utf8Bytes);
        Assert.NotNull(utf8Deser);
        Assert.Equal(2, utf8Deser!.Apps!.Count);
        Assert.Equal("Яндекс Браузер", utf8Deser.Apps[1].Name);
    }

    [Fact]
    public void Serialize_and_deserialize_launch_app_message()
    {
        var msg = new WireMessage
        {
            Type = "launch_app",
            Exe = "chrome.exe",
            LaunchTarget = @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        };

        var json = WireMessage.Serialize(msg);
        Assert.Contains("\"v\":1", json);
        Assert.Contains("\"type\":\"launch_app\"", json);
        Assert.Contains("\"exe\":\"chrome.exe\"", json);
        Assert.Contains("\"launch_target\":", json);

        var deserialized = WireMessage.Deserialize(json);
        Assert.NotNull(deserialized);
        Assert.Equal("launch_app", deserialized!.Type);
        Assert.Equal("chrome.exe", deserialized.Exe);
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe", deserialized.LaunchTarget);

        // Without launch_target
        var msgWithoutTarget = new WireMessage
        {
            Type = "launch_app",
            Exe = "notepad.exe"
        };
        var jsonNoTarget = WireMessage.Serialize(msgWithoutTarget);
        Assert.DoesNotContain("\"launch_target\"", jsonNoTarget);

        var deserNoTarget = WireMessage.Deserialize(jsonNoTarget);
        Assert.NotNull(deserNoTarget);
        Assert.Equal("notepad.exe", deserNoTarget!.Exe);
        Assert.Null(deserNoTarget.LaunchTarget);
    }

    [Fact]
    public void Serialize_and_deserialize_set_block_list_message()
    {
        var msg = new WireMessage
        {
            Type = "set_block_list",
            ExeNames = new List<string> { "discord.exe", "steam.exe", "telegram.exe" }
        };

        var json = WireMessage.Serialize(msg);
        Assert.Contains("\"v\":1", json);
        Assert.Contains("\"type\":\"set_block_list\"", json);
        Assert.Contains("\"exe_names\":[\"discord.exe\",\"steam.exe\",\"telegram.exe\"]", json);

        var deserialized = WireMessage.Deserialize(json);
        Assert.NotNull(deserialized);
        Assert.Equal("set_block_list", deserialized!.Type);
        Assert.NotNull(deserialized.ExeNames);
        Assert.Equal(3, deserialized.ExeNames!.Count);
        Assert.Equal("discord.exe", deserialized.ExeNames[0]);
        Assert.Equal("steam.exe", deserialized.ExeNames[1]);
        Assert.Equal("telegram.exe", deserialized.ExeNames[2]);

        // Empty block list (clear / unblock)
        var emptyMsg = new WireMessage
        {
            Type = "set_block_list",
            ExeNames = new List<string>()
        };
        var emptyJson = WireMessage.Serialize(emptyMsg);
        Assert.Contains("\"exe_names\":[]", emptyJson);

        var deserEmpty = WireMessage.Deserialize(emptyJson);
        Assert.NotNull(deserEmpty);
        Assert.NotNull(deserEmpty!.ExeNames);
        Assert.Empty(deserEmpty.ExeNames!);
    }
}
