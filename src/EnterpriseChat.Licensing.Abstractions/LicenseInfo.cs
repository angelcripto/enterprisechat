namespace EnterpriseChat.Licensing.Abstractions;

/// <summary>
/// Snapshot of the license currently active on the server. Returned by
/// <see cref="ILicenseValidator"/> so the server (and admin UI) can render
/// the current edition, capacity and expiry without knowing how it was
/// obtained (Free stub vs. signed JWT plugin).
/// </summary>
public sealed record LicenseInfo(
    LicenseEdition Edition,
    int MaxConcurrentUsers,
    DateTimeOffset? ExpiresAt,
    string? LicensedTo,
    string? LicenseId);

public enum LicenseEdition
{
    /// <summary>
    /// Built-in Free edition. Hard cap on concurrent users, no expiry.
    /// Used whenever no signed Pro plugin is loaded.
    /// </summary>
    Free = 0,

    /// <summary>
    /// Pro edition validated by a closed-source plugin against a signed
    /// license token. Capacity and expiry come from the token claims.
    /// </summary>
    Pro = 1
}
