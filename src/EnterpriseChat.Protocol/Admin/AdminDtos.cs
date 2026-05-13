namespace EnterpriseChat.Protocol.Admin;

public sealed record CreateUserRequest(
    string Username,
    string Password,
    string FullName,
    string? Email,
    int? DepartmentId,
    string Role = "User");

public sealed record UpdateUserRequest(
    string FullName,
    string? Email,
    int? DepartmentId,
    string Role,
    bool IsActive);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record AdminUserDetail(
    int Id,
    string Username,
    string FullName,
    string? Email,
    int? DepartmentId,
    string? DepartmentName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record DepartmentSummary(int Id, string Name);

public sealed record CreateDepartmentRequest(string Name);
