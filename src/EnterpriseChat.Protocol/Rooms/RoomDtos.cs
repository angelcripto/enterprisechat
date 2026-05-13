namespace EnterpriseChat.Protocol.Rooms;

public sealed record RoomSummary(
    int Id,
    string Name,
    bool IsPrivate,
    int CreatedByUserId,
    DateTimeOffset CreatedAt,
    bool IsMember,
    int MemberCount);

public sealed record CreateRoomRequest(string Name, bool IsPrivate = false);
