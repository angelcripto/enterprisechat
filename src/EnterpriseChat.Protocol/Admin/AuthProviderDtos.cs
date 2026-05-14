namespace EnterpriseChat.Protocol.Admin;

public sealed record AuthProviderSummary(
    int Id,
    string Kind,
    string DisplayName,
    bool IsEnabled,
    int Priority,
    string HashAlgorithm,
    bool PlaintextRiskAcknowledged,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AuthProviderDetail(
    int Id,
    string Kind,
    string DisplayName,
    bool IsEnabled,
    int Priority,
    string HashAlgorithm,
    bool PlaintextRiskAcknowledged,
    object Config,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateAuthProviderRequest(
    string Kind,
    string DisplayName,
    bool IsEnabled,
    int Priority,
    string HashAlgorithm,
    bool PlaintextRiskAcknowledged,
    object Config,
    object? Secrets);

public sealed record UpdateAuthProviderRequest(
    string DisplayName,
    bool IsEnabled,
    int Priority,
    string HashAlgorithm,
    bool PlaintextRiskAcknowledged,
    object Config,
    object? Secrets);

public sealed record AuthProviderTestRequest(
    string Kind,
    object Config,
    object? Secrets,
    string? TestUsername);

public sealed record AuthProviderTestResult(
    bool Connected,
    bool? UserFound,
    string? Detail);
