namespace EnterpriseChat.Licensing.Abstractions;

/// <summary>
/// Administrative surface for licensing. Implemented by the closed-source Pro
/// plugin (verifies signed JWT serials and persists them) and by a stub in
/// the open-source build that always reports "feature requires Pro plugin".
///
/// Kept separate from <see cref="ILicenseValidator"/> so the validator can stay
/// hot-path simple (just answer "edition + cap"); admin flows live here.
/// </summary>
public interface ILicenseAdministrator
{
    /// <summary>
    /// Apply a user-entered serial. Decodes, verifies RS256 signature, checks
    /// expiry, persists to local DB, swaps the active <see cref="ILicenseValidator"/>.
    /// </summary>
    Task<ApplyLicenseResult> ApplyAsync(string serial, CancellationToken ct = default);

    /// <summary>
    /// Revert to Free edition. Marks any stored license as inactive.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-read the active license from local storage on startup, so the server
    /// remembers its Pro state across restarts without re-pasting the serial.
    /// </summary>
    Task RestoreFromStorageAsync(CancellationToken ct = default);
}

public sealed record ApplyLicenseResult(
    bool Success,
    string? ErrorMessage,
    LicenseInfo? Info);
