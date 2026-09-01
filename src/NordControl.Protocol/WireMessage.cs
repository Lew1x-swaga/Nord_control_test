using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NordControl.Protocol;

public class ProcessItemInfo
{
    public string Exe { get; set; } = string.Empty;
    public int Pid { get; set; }
    public string Title { get; set; } = string.Empty;
}

public class InstalledAppInfo
{
    public string Name { get; set; } = string.Empty;
    public string Exe { get; set; } = string.Empty;
    public string? LaunchTarget { get; set; }
}

public class WireMessage
{
    public int V { get; set; } = ProtocolConstants.Version;
    public string Type { get; set; } = string.Empty;
    public string? Pin { get; set; }
    public string? DisplayName { get; set; }
    public string? Hostname { get; set; }
    public string? AgentVersion { get; set; }
    public string? SessionToken { get; set; }
    public string? StudentId { get; set; }
    public int? HeartbeatIntervalMs { get; set; }
    public int? ReconnectWindowMs { get; set; }
    public string? Reason { get; set; }
    public string? Message { get; set; }
    public string? MessageId { get; set; }
    public int? Seq { get; set; }
    public string? ActiveExe { get; set; }
    public List<ProcessItemInfo>? Items { get; set; }
    public List<InstalledAppInfo>? Apps { get; set; }
    public string? Exe { get; set; }
    public string? LaunchTarget { get; set; }
    public List<string>? ExeNames { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(WireMessage msg) => JsonSerializer.Serialize(msg, JsonOptions);

    public static byte[] SerializeUtf8(WireMessage msg) => JsonSerializer.SerializeToUtf8Bytes(msg, JsonOptions);

    public static WireMessage? Deserialize(string json) => JsonSerializer.Deserialize<WireMessage>(json, JsonOptions);

    public static WireMessage? Deserialize(ReadOnlySpan<byte> utf8) => JsonSerializer.Deserialize<WireMessage>(utf8, JsonOptions);
}
