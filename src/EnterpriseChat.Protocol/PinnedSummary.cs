namespace EnterpriseChat.Protocol;

/// <summary>
/// One pinned-message record returned by <c>GET /rooms/{id}/pinned</c>.
/// The body is included so the SPA can render the ribbon at the top of the
/// chat without an extra round trip.
/// </summary>
public sealed record PinnedSummary(
    int RoomId,
    long MessageId,
    int PinnedByUserId,
    DateTimeOffset PinnedAt,
    int AuthorUserId,
    string Body,
    DateTimeOffset SentAt);
