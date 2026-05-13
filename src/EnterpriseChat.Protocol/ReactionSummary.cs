namespace EnterpriseChat.Protocol;

/// <summary>
/// Aggregate of reactions for a message: one record per distinct emoji with
/// the total count and whether the calling user has reacted with it.
/// </summary>
public sealed record ReactionSummary(
    long MessageId,
    string Emoji,
    int Count,
    bool Mine);
