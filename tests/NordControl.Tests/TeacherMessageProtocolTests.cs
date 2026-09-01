using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class TeacherMessageProtocolTests
{
    [Fact]
    public void MaxTeacherMessageChars_is_400()
    {
        Assert.Equal(400, ProtocolConstants.MaxTeacherMessageChars);
    }

    [Fact]
    public void Serialize_and_deserialize_teacher_message()
    {
        var msg = new WireMessage
        {
            Type = "teacher_message",
            MessageId = "550e8400-e29b-41d4-a716-446655440000",
            Message = "Откройте §3"
        };

        var json = WireMessage.Serialize(msg);
        Assert.Contains("\"v\":1", json);
        Assert.Contains("\"type\":\"teacher_message\"", json);
        Assert.Contains("\"message_id\":", json);
        Assert.Contains("\"message_id\":\"550e8400-e29b-41d4-a716-446655440000\"", json);
        Assert.Contains("\"message\":\"Откройте §3\"", json);
        Assert.DoesNotContain("\"reply\"", json);

        var deserialized = WireMessage.Deserialize(json);
        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized!.V);
        Assert.Equal("teacher_message", deserialized.Type);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", deserialized.MessageId);
        Assert.Equal("Откройте §3", deserialized.Message);

        var utf8Bytes = WireMessage.SerializeUtf8(msg);
        var utf8Deser = WireMessage.Deserialize(utf8Bytes);
        Assert.NotNull(utf8Deser);
        Assert.Equal("teacher_message", utf8Deser!.Type);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", utf8Deser.MessageId);
        Assert.Equal("Откройте §3", utf8Deser.Message);
        Assert.Equal(1, utf8Deser.V);
    }

    [Fact]
    public void Null_message_id_is_omitted_when_writing()
    {
        var msg = new WireMessage
        {
            Type = "teacher_message",
            Message = "Откройте §3"
        };

        var json = WireMessage.Serialize(msg);
        Assert.DoesNotContain("\"message_id\"", json);
        Assert.Contains("\"type\":\"teacher_message\"", json);
        Assert.Contains("\"message\":\"Откройте §3\"", json);

        var deserialized = WireMessage.Deserialize(json);
        Assert.NotNull(deserialized);
        Assert.Null(deserialized!.MessageId);
        Assert.Equal("Откройте §3", deserialized.Message);
    }
}
