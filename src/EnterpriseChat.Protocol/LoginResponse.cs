namespace EnterpriseChat.Protocol;

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    int UserId,
    string Username,
    string FullName,
    string Role);
