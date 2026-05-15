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

public sealed record AuthProviderIntrospectRequest(
    string Kind,
    object Config,
    object? Secrets,
    string? Table);

public sealed record AuthProviderIntrospectResult(
    IReadOnlyList<string>? Tables,
    IReadOnlyList<string>? Columns);

public sealed record AuthProviderBrowseRequest(
    string? Search,
    int Page = 0,
    int PageSize = 50,
    string? Sort = null,
    string? Dir = null);

public sealed record AuthProviderAllIdsRequest(
    string? Search,
    string? Sort = null,
    string? Dir = null);

public sealed record AuthProviderAllIdsResult(
    IReadOnlyList<string> Ids);

public sealed record AuthProviderBrowseRow(
    string ExternalId,
    string Username,
    string? FullName,
    string? Email,
    bool AlreadyImported);

public sealed record AuthProviderBrowseResult(
    IReadOnlyList<AuthProviderBrowseRow> Rows,
    int Total,
    int Page,
    int PageSize,
    int LicenseSlotsAvailable,
    int LicenseMaxUsers,
    int LicenseActiveUsers);

public sealed record AuthProviderImportRequest(
    IReadOnlyList<string> ExternalIds);

public sealed record AuthProviderImportResult(
    int Created,
    int Skipped,
    IReadOnlyList<string> SkippedReasons);

/// <summary>
/// Body opcional del DELETE de proveedor. <c>OnProvisionedUsers</c> acepta:
///   "keep" → dejar usuarios huérfanos.
///   "deactivate" (recomendado) → SetNull + IsActive=false.
///   "cascade" → borrar usuarios provisionados (falla si tienen mensajes).
/// </summary>
public sealed record DeleteAuthProviderRequest(string OnProvisionedUsers);
