namespace EnterpriseChat.Protocol;

/// <summary>
/// Lightweight projection of a <c>User</c> returned by <c>GET /users</c> and
/// surfaced in the client sidebar.
/// </summary>
public sealed record UserSummary(
    int Id,
    string Username,
    string FullName,
    string? Department,
    string Role,
    bool IsOnline,
    bool HasAvatar,
    int UnreadDirectMessages);
