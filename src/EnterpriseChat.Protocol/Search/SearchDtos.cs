namespace EnterpriseChat.Protocol.Search;

public sealed record SearchHit(
    long ServerId,
    int FromUserId,
    string FromUsername,
    int? ToUserId,
    int? RoomId,
    string? RoomName,
    string Body,
    DateTimeOffset SentAt);

public sealed record SearchResponse(string Query, IReadOnlyList<SearchHit> Hits);
