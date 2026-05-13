namespace EnterpriseChat.Protocol;

/// <summary>
/// Wire-level chat message exchanged between server and clients over SignalR.
/// This is the minimal shape: real persistence + delivery state will be added
/// in the MVP phase. Kept in <c>Protocol</c> so server and client compile
/// against the exact same record.
/// </summary>
public sealed record ChatMessage(
    Guid MessageId,
    long? ServerId,
    int FromUserId,
    int? ToUserId,
    int? RoomId,
    string Body,
    DateTimeOffset SentAt,
    long? AttachmentId = null,
    string? AttachmentFileName = null,
    long? AttachmentSizeBytes = null);
