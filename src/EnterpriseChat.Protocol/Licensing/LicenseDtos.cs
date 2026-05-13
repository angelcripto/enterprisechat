namespace EnterpriseChat.Protocol.Licensing;

public sealed record ApplyLicenseRequest(string Serial);

/// <summary>
/// Wire response from <c>POST /admin/license</c>. Mirrors
/// <c>ApplyLicenseResult</c> in <c>EnterpriseChat.Licensing.Abstractions</c>.
/// </summary>
public sealed record ApplyLicenseResponse(
    bool Success,
    string? ErrorMessage,
    object? Info);
