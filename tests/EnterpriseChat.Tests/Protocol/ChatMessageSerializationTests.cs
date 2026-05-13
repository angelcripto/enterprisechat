using System.Text.Json;
using EnterpriseChat.Protocol;
using FluentAssertions;

namespace EnterpriseChat.Tests.Protocol;

public sealed class ChatMessageSerializationTests
{
    [Fact]
    public void Roundtrips_through_System_Text_Json()
    {
        var original = new ChatMessage(
            MessageId: Guid.NewGuid(),
            ServerId: 42,
            FromUserId: 7,
            ToUserId: 9,
            RoomId: null,
            Body: "hola, ¿qué tal?",
            SentAt: DateTimeOffset.UnixEpoch.AddMinutes(123));

        var json = JsonSerializer.Serialize(original);
        var rehydrated = JsonSerializer.Deserialize<ChatMessage>(json);

        rehydrated.Should().Be(original);
    }

    [Fact]
    public void Allows_room_targeted_message_without_recipient_user()
    {
        var roomMsg = new ChatMessage(
            MessageId: Guid.NewGuid(),
            ServerId: null,
            FromUserId: 1,
            ToUserId: null,
            RoomId: 12,
            Body: "buenos días al canal",
            SentAt: DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(roomMsg);
        var rehydrated = JsonSerializer.Deserialize<ChatMessage>(json)!;

        rehydrated.RoomId.Should().Be(12);
        rehydrated.ToUserId.Should().BeNull();
    }
}
